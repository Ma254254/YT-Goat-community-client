using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GoatClient.Core;
using GoatClient.Core.Constants;
using GoatClient.Models;
using GoatClient.Services.Logging;
using GoatClient.Services.Minecraft;

namespace GoatClient.Services.Launch;

/// <summary>
/// Starts the real Minecraft Java process. Arguments are taken from the version JSON
/// ("arguments.jvm/game" or legacy "minecraftArguments") and placeholders are substituted.
/// The access token is passed only on the command line and is redacted from every log.
/// </summary>
public sealed class MinecraftLauncherService : ObservableObject, IMinecraftLauncherService
{
    private static readonly Regex Placeholder = new(@"\$\{([a-zA-Z0-9_]+)\}", RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, bool> Features = new Dictionary<string, bool>
    {
        ["is_demo_user"] = false,
        ["has_custom_resolution"] = false,
        ["has_quick_plays_support"] = false,
        ["is_quick_play_singleplayer"] = false,
        ["is_quick_play_multiplayer"] = false,
        ["is_quick_play_realms"] = false,
    };

    private readonly IMinecraftInstallationService _installation;
    private readonly ILogger _logger;
    private readonly object _logLock = new();

    private MinecraftProcessState _processState = MinecraftProcessState.NotRunning;
    private Process? _process;
    private StreamWriter? _logWriter;
    private string? _secret;

    public MinecraftLauncherService(IMinecraftInstallationService installation, ILogger logger)
    {
        _installation = installation;
        _logger = logger;
    }

    public MinecraftProcessState ProcessState
    {
        get => _processState;
        private set
        {
            if (SetProperty(ref _processState, value))
            {
                OnPropertyChanged(nameof(IsRunning));
            }
        }
    }

    public bool IsRunning => ProcessState is MinecraftProcessState.Starting or MinecraftProcessState.Running;

    public int? LastExitCode { get; private set; }

    public string? CurrentLogFile { get; private set; }

    public event EventHandler<string>? LogLine;

    public event EventHandler<int>? ProcessExited;

    public async Task LaunchAsync(LauncherProfile profile, VersionManifest version, JavaRuntime runtime, MinecraftSession session, CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            throw new LaunchFailedException("Minecraft is already running.", LaunchFix.None);
        }

        ProcessState = MinecraftProcessState.Starting;
        try
        {
            OpenLog();
            _secret = session.AccessToken;
            Log($"Launching Minecraft {version.Id}");
            Log($"Profile: {profile.Name}");
            Log($"Java Runtime: {runtime.DisplayName} ({runtime.ExecutablePath})");
            Log($"Game Directory: {profile.GameDirectory}");
            Log($"RAM: {profile.RamMb / 1024} GB");
            Log($"Account: {session.Account.Username}");

            if (!File.Exists(runtime.ExecutablePath))
            {
                throw new LaunchFailedException($"The Java runtime is missing: {runtime.ExecutablePath}", LaunchFix.InstallJava);
            }

            Directory.CreateDirectory(profile.GameDirectory);

            // Libraries & client jar – every file must exist.
            Log("Loading libraries...");
            var classpath = new List<string>();
            foreach (var library in version.Libraries)
            {
                if (library.Artifact?.Path is { } relative)
                {
                    var path = MinecraftInstallationService.GetLibraryPath(relative);
                    RequireFile(path, $"Required library is missing: {library.Name}");
                    if (!classpath.Contains(path, StringComparer.OrdinalIgnoreCase))
                    {
                        classpath.Add(path);
                    }
                }
            }

            var clientJar = LocalVersionStore.GetJarPath(version.Id);
            RequireFile(clientJar, "The Minecraft client file is missing.");
            classpath.Add(clientJar);

            // Natives (legacy versions ship them as classifier jars that must be extracted).
            var nativesDirectory = LocalVersionStore.GetNativesDirectory(version.Id);
            await Task.Run(() => ExtractNatives(version, nativesDirectory), cancellationToken);

            // Assets (legacy layouts).
            var assetsRoot = AppPaths.Assets;
            var gameAssets = assetsRoot;
            var assetIndex = await _installation.LoadAssetIndexAsync(version, cancellationToken);
            if (version.AssetIndex is not null && assetIndex is null)
            {
                throw new LaunchFailedException("The asset index is missing.", LaunchFix.RepairInstallation);
            }

            if (assetIndex?["objects"] is JsonObject objects)
            {
                if (assetIndex["map_to_resources"]?.GetValue<bool>() == true)
                {
                    gameAssets = Path.Combine(profile.GameDirectory, "resources");
                    await Task.Run(() => MinecraftInstallationService.CopyAssetsTo(objects, gameAssets), cancellationToken);
                }
                else if (assetIndex["virtual"]?.GetValue<bool>() == true)
                {
                    gameAssets = MinecraftInstallationService.GetVirtualAssetsDirectory(version.AssetIndex!.Id);
                }
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["auth_player_name"] = session.Account.Username,
                ["auth_uuid"] = session.Account.Uuid,
                ["auth_access_token"] = session.AccessToken,
                ["auth_session"] = session.AccessToken,
                ["auth_xuid"] = session.Xuid ?? "0",
                ["clientid"] = string.Empty,
                ["user_type"] = "msa",
                ["user_properties"] = "{}",
                ["version_name"] = version.Id,
                ["version_type"] = version.Type,
                ["game_directory"] = profile.GameDirectory,
                ["assets_root"] = assetsRoot,
                ["game_assets"] = gameAssets,
                ["assets_index_name"] = version.AssetIndex?.Id ?? version.Assets ?? "legacy",
                ["natives_directory"] = nativesDirectory,
                ["library_directory"] = AppPaths.Libraries,
                ["classpath_separator"] = ";",
                ["classpath"] = string.Join(';', classpath),
                ["launcher_name"] = "GoatClient",
                ["launcher_version"] = AppInfo.Version,
            };

            // JVM arguments: memory first, then user arguments, then version arguments.
            var arguments = new List<string> { $"-Xmx{profile.RamMb}M", "-Xms512M" };
            arguments.AddRange(SplitArguments(profile.JvmArguments));

            if (version.JvmArguments is { } jvm)
            {
                arguments.AddRange(Expand(jvm).Select(a => Substitute(a, values)));
            }
            else
            {
                arguments.Add(Substitute("-Djava.library.path=${natives_directory}", values));
                arguments.Add("-cp");
                arguments.Add(values["classpath"]);
            }

            if (version.Logging is { } logging)
            {
                var configPath = MinecraftInstallationService.GetLoggingConfigPath(logging.FileId);
                if (File.Exists(configPath))
                {
                    arguments.Add(logging.Argument.Replace("${path}", configPath, StringComparison.Ordinal));
                }
            }

            arguments.Add(version.MainClass);

            if (version.GameArguments is { } game)
            {
                arguments.AddRange(Expand(game).Select(a => Substitute(a, values)));
            }
            else if (version.LegacyArguments is { } legacy)
            {
                arguments.AddRange(legacy.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(a => Substitute(a, values)));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var startInfo = new ProcessStartInfo
            {
                FileName = runtime.ExecutablePath,
                WorkingDirectory = profile.GameDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            Log($"Command: \"{runtime.ExecutablePath}\" {string.Join(' ', arguments.Select(QuoteForLog))}");
            Log("Starting process...");

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => OnOutput(e.Data);
            process.ErrorDataReceived += (_, e) => OnOutput(e.Data);
            process.Exited += (_, _) => OnExited(process);

            if (!process.Start())
            {
                throw new LaunchFailedException("The Java process could not be started.", LaunchFix.InstallJava);
            }

            _process = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            Log($"Process started (PID {process.Id}).");
            ProcessState = MinecraftProcessState.Running;
        }
        catch (Exception ex)
        {
            ProcessState = MinecraftProcessState.NotRunning;
            Log($"Launch failed: {ex.Message}");
            CloseLog();
            _secret = null;
            if (ex is LaunchFailedException or OperationCanceledException)
            {
                throw;
            }

            if (ex is System.ComponentModel.Win32Exception)
            {
                throw new LaunchFailedException("Java could not be executed. Repair the Java runtime.", LaunchFix.InstallJava, ex);
            }

            throw new LaunchFailedException($"Minecraft could not be started: {ex.Message}", LaunchFix.RepairInstallation, ex);
        }
    }

    private void OnOutput(string? line)
    {
        if (line is not null)
        {
            Log(line);
        }
    }

    private void OnExited(Process process)
    {
        var exitCode = 0;
        try
        {
            process.WaitForExit(); // flush redirected output
            exitCode = process.ExitCode;
        }
        catch (Exception ex)
        {
            _logger.Warning("Could not read the Minecraft exit code.", ex);
        }

        LastExitCode = exitCode;
        Log($"Process exited with code {exitCode}.");
        _logger.Info($"Minecraft exited with code {exitCode}.");
        ProcessState = exitCode == 0 ? MinecraftProcessState.Exited : MinecraftProcessState.Crashed;
        CloseLog();
        _secret = null;
        _process = null;
        process.Dispose();
        ProcessExited?.Invoke(this, exitCode);
    }

    private static IEnumerable<string> Expand(JsonArray arguments)
    {
        foreach (var item in arguments)
        {
            switch (item)
            {
                case JsonValue value when value.TryGetValue<string>(out var text):
                    yield return text;
                    break;
                case JsonObject conditional when RuleEvaluator.Allows(conditional["rules"] as JsonArray, Features):
                    if (conditional["value"] is JsonArray many)
                    {
                        foreach (var entry in many)
                        {
                            if (entry?.GetValue<string>() is { } single)
                            {
                                yield return single;
                            }
                        }
                    }
                    else if (conditional["value"]?.GetValue<string>() is { } one)
                    {
                        yield return one;
                    }

                    break;
            }
        }
    }

    private static string Substitute(string argument, IReadOnlyDictionary<string, string> values)
        => Placeholder.Replace(argument, m => values.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);

    /// <summary>Splits user JVM arguments, honouring double quotes.</summary>
    private static IEnumerable<string> SplitArguments(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var current = new StringBuilder();
        var quoted = false;
        foreach (var c in text)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static void ExtractNatives(VersionManifest version, string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var library in version.Libraries)
        {
            if (library.Native?.Path is not { } relative)
            {
                continue;
            }

            var jar = MinecraftInstallationService.GetLibraryPath(relative);
            if (!File.Exists(jar))
            {
                throw new LaunchFailedException($"Required native library is missing: {library.Name}", LaunchFix.RepairInstallation);
            }

            using var archive = ZipFile.OpenRead(jar);
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/') || library.NativeExcludes.Any(e => entry.FullName.StartsWith(e, StringComparison.Ordinal)))
                {
                    continue;
                }

                var target = Path.GetFullPath(Path.Combine(directory, entry.FullName));
                if (!target.StartsWith(Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                try
                {
                    entry.ExtractToFile(target, overwrite: true);
                }
                catch (IOException) when (File.Exists(target))
                {
                    // File in use by another running instance – the existing copy is identical.
                }
            }
        }
    }

    private static void RequireFile(string path, string message)
    {
        if (!File.Exists(path))
        {
            throw new LaunchFailedException(message, LaunchFix.RepairInstallation);
        }
    }

    private string Redact(string text)
        => _secret is { Length: > 0 } secret ? text.Replace(secret, "***", StringComparison.Ordinal) : text;

    private string QuoteForLog(string argument) => argument.Contains(' ') ? $"\"{argument}\"" : argument;

    private void OpenLog()
    {
        lock (_logLock)
        {
            Directory.CreateDirectory(AppPaths.Logs);
            CurrentLogFile = Path.Combine(AppPaths.Logs, $"minecraft-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
            _logWriter = new StreamWriter(new FileStream(CurrentLogFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        }
    }

    private void CloseLog()
    {
        lock (_logLock)
        {
            _logWriter?.Dispose();
            _logWriter = null;
        }
    }

    private void Log(string message)
    {
        var line = Redact(message);
        lock (_logLock)
        {
            try
            {
                _logWriter?.WriteLine(line);
            }
            catch (IOException)
            {
                // Logging must never break the game process handling.
            }
        }

        LogLine?.Invoke(this, line);
    }
}
