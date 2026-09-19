using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using GoatClient.Models;
using GoatClient.Services.Logging;

namespace GoatClient.Services.Launch;

public sealed class OfficialLauncherService : IOfficialLauncherService
{
    private const string ProfilesFileName = "launcher_profiles.json";
    private const string BackupSuffix = ".goatbackup";
    private const string KeyPrefix = "goatclient-";
    private const string StorePackageFamily = "Microsoft.4297127D64EC6_8wekyb3d8bbwe";
    private const string StoreAppUserModelId = StorePackageFamily + "!Minecraft";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly ILogger _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private string? _iconDataUri;

    public OfficialLauncherService(ILogger logger)
    {
        _logger = logger;
        MinecraftDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".minecraft");
    }

    public string MinecraftDirectory { get; }

    public bool IsInstalled => FindClassicLauncher() is not null || IsStoreLauncherInstalled();

    public string StatusText
    {
        get
        {
            if (FindClassicLauncher() is { } exe)
            {
                return $"Found: {exe}";
            }

            return IsStoreLauncherInstalled()
                ? "Found: Minecraft Launcher (Microsoft Store / Xbox app)"
                : "Not found – install the official Minecraft Launcher from minecraft.net";
        }
    }

    public bool IsRunning
    {
        get
        {
            try
            {
                var processes = Process.GetProcessesByName("MinecraftLauncher");
                var running = processes.Length > 0;
                foreach (var process in processes)
                {
                    process.Dispose();
                }

                return running;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public async Task<string> SyncProfileAsync(LauncherProfile profile, IReadOnlyCollection<Guid> existingProfileIds, CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(MinecraftDirectory);
            Directory.CreateDirectory(profile.GameDirectory);

            var path = Path.Combine(MinecraftDirectory, ProfilesFileName);
            JsonObject root;
            if (File.Exists(path))
            {
                var text = await File.ReadAllTextAsync(path, cancellationToken);
                root = JsonNode.Parse(text) as JsonObject
                       ?? throw new InvalidDataException("launcher_profiles.json has an unexpected format. It was not changed.");

                // Safety copy before every change.
                File.Copy(path, path + BackupSuffix, overwrite: true);
            }
            else
            {
                // The official launcher has never been started – create the minimal structure it expects.
                root = new JsonObject
                {
                    ["profiles"] = new JsonObject(),
                    ["settings"] = new JsonObject(),
                    ["version"] = 3,
                };
            }

            if (root["profiles"] is not JsonObject profiles)
            {
                profiles = new JsonObject();
                root["profiles"] = profiles;
            }

            // Remove GOAT CLIENT profiles that belong to deleted GOAT CLIENT profiles. Never touch others.
            var validKeys = existingProfileIds.Select(KeyFor).ToHashSet(StringComparer.Ordinal);
            foreach (var stale in profiles.Select(kv => kv.Key)
                         .Where(k => k.StartsWith(KeyPrefix, StringComparison.Ordinal) && !validKeys.Contains(k))
                         .ToList())
            {
                profiles.Remove(stale);
            }

            var key = KeyFor(profile.Id);
            var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var entry = profiles[key] as JsonObject ?? new JsonObject { ["created"] = now };
            var name = $"GOAT CLIENT - {profile.Name}";

            entry["name"] = name;
            entry["type"] = "custom";
            entry["lastVersionId"] = profile.MinecraftVersion;
            entry["gameDir"] = profile.GameDirectory;
            entry["javaArgs"] = BuildJavaArgs(profile);
            entry["lastUsed"] = now; // most recently used → preselected in the official launcher
            if (GetIconDataUri() is { } icon)
            {
                entry["icon"] = icon;
            }

            profiles[key] = entry;

            // Write atomically: temp file, then replace.
            var temp = path + ".goattmp";
            await File.WriteAllTextAsync(temp, root.ToJsonString(WriteOptions), cancellationToken);
            File.Move(temp, path, overwrite: true);

            _logger.Info($"Official launcher profile '{name}' updated (Minecraft {profile.MinecraftVersion}).");
            return name;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public void OpenLauncher()
    {
        if (FindClassicLauncher() is { } exe)
        {
            _logger.Info($"Starting official Minecraft Launcher: {exe}");
            using var process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
            return;
        }

        if (IsStoreLauncherInstalled())
        {
            _logger.Info("Starting official Minecraft Launcher (Microsoft Store / Xbox app).");
            using var process = Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{StoreAppUserModelId}") { UseShellExecute = true });
            return;
        }

        throw new OfficialLauncherNotFoundException();
    }

    private static string KeyFor(Guid id) => KeyPrefix + id.ToString("N");

    /// <summary>RAM from the profile plus the profile's own JVM arguments.</summary>
    private static string BuildJavaArgs(LauncherProfile profile)
    {
        var args = $"-Xmx{profile.RamMb}M";
        return string.IsNullOrWhiteSpace(profile.JvmArguments) ? args : $"{args} {profile.JvmArguments.Trim()}";
    }

    private static string? FindClassicLauncher()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Minecraft Launcher", "MinecraftLauncher.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Minecraft Launcher", "MinecraftLauncher.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Minecraft Launcher", "MinecraftLauncher.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static bool IsStoreLauncherInstalled()
        => Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", StorePackageFamily));

    /// <summary>The official GOAT CLIENT logo as profile icon (data URI, same mechanism as the Fabric installer).</summary>
    private string? GetIconDataUri()
    {
        if (_iconDataUri is not null)
        {
            return _iconDataUri;
        }

        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Logo/ProfileIcon.png", UriKind.Absolute));
            if (resource is null)
            {
                return null;
            }

            using var stream = resource.Stream;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            _iconDataUri = "data:image/png;base64," + Convert.ToBase64String(memory.ToArray());
            return _iconDataUri;
        }
        catch (Exception ex)
        {
            _logger.Warning("Profile icon could not be loaded – the profile is created without icon.", ex);
            return null;
        }
    }
}
