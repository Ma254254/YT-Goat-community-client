using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoatClient.Models;
using GoatClient.Services.Downloads;
using GoatClient.Services.Logging;

namespace GoatClient.Services.Java;

public sealed class JavaService : IJavaService
{
    /// <summary>Mojang's official Java runtime index (the same source the official launcher uses).</summary>
    public const string RuntimeIndexUrl =
        "https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

    private const string Platform = "windows-x64";
    private const string MarkerFileName = ".goatruntime.json";
    private const string ManifestFileName = ".manifest.json";

    private readonly IDownloadService _downloads;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _installLock = new(1, 1);

    public JavaService(string runtimeRoot, IDownloadService downloads, ILogger logger)
    {
        RuntimeRoot = runtimeRoot;
        _downloads = downloads;
        _logger = logger;
        Info = JavaRuntimeInfo.Empty(runtimeRoot);
    }

    public string RuntimeRoot { get; }

    public JavaRuntimeInfo Info { get; private set; }

    public event EventHandler? RuntimesChanged;

    public async Task<JavaRuntimeInfo> ScanAsync(CancellationToken cancellationToken = default)
    {
        var info = await Task.Run(() => ScanCore(cancellationToken), cancellationToken);
        Info = info;
        _logger.Info(info.HasManagedRuntime
            ? $"Managed Java runtimes: {string.Join(", ", info.ManagedRuntimes.Select(r => r.DisplayName))}."
            : "No managed Java runtime installed yet.");
        RuntimesChanged?.Invoke(this, EventArgs.Empty);
        return info;
    }

    public JavaRequirement ResolveRequirement(JavaRuntimePreference preference, JavaRequirement? versionRequirement) => preference switch
    {
        JavaRuntimePreference.Java17 => new JavaRequirement(17, versionRequirement?.MajorVersion == 17 ? versionRequirement.Component : null),
        JavaRuntimePreference.Java21 => new JavaRequirement(21, versionRequirement?.MajorVersion == 21 ? versionRequirement.Component : null),
        _ => versionRequirement ?? throw new JavaRuntimeUnavailableException("The Java requirement of this Minecraft version is not known yet. Install the version first."),
    };

    public JavaRuntime? FindManagedRuntime(int majorVersion)
        => Info.ManagedRuntimes.FirstOrDefault(r => r.MajorVersion == majorVersion && File.Exists(r.ExecutablePath));

    public async Task<JavaRuntime> EnsureRuntimeAsync(JavaRequirement requirement, bool verify, IProgress<TransferProgress>? progress, CancellationToken cancellationToken)
    {
        await _installLock.WaitAsync(cancellationToken);
        try
        {
            var existing = FindManagedRuntime(requirement.MajorVersion);
            if (existing is not null && !verify)
            {
                return existing;
            }

            var home = Path.Combine(RuntimeRoot, $"java-{requirement.MajorVersion}");
            progress?.Report(TransferProgress.Indeterminate($"Preparing Java {requirement.MajorVersion}…"));

            // 1. Find the runtime in Mojang's index.
            var (component, manifestUrl, manifestSha1, versionName) = await ResolveFromIndexAsync(requirement, cancellationToken);

            // 2. Download and verify the file manifest of the runtime.
            var manifestJson = await _downloads.GetStringAsync(manifestUrl, cancellationToken);
            if (!string.IsNullOrEmpty(manifestSha1) && !Sha1Matches(manifestJson, manifestSha1))
            {
                throw new JavaRuntimeUnavailableException("The Java runtime manifest failed its integrity check. Please try again.");
            }

            var files = JsonNode.Parse(manifestJson)?["files"]?.AsObject()
                        ?? throw new JavaRuntimeUnavailableException("The Java runtime manifest is invalid.");

            // 3. Create directories, download files (hash verified), recreate links.
            var requests = new List<DownloadRequest>();
            var links = new List<(string Path, string Target)>();
            foreach (var (relative, node) in files)
            {
                if (node is null)
                {
                    continue;
                }

                var target = SafeCombine(home, relative);
                switch (node["type"]?.GetValue<string>())
                {
                    case "directory":
                        Directory.CreateDirectory(target);
                        break;
                    case "file":
                        var raw = node["downloads"]?["raw"];
                        var url = raw?["url"]?.GetValue<string>();
                        if (url is not null)
                        {
                            requests.Add(new DownloadRequest(url, target, raw?["sha1"]?.GetValue<string>(), raw?["size"]?.GetValue<long>()));
                        }

                        break;
                    case "link":
                        if (node["target"]?.GetValue<string>() is { } linkTarget)
                        {
                            links.Add((target, linkTarget));
                        }

                        break;
                }
            }

            await _downloads.DownloadManyAsync(requests, $"Installing Java {requirement.MajorVersion}", verify, progress, cancellationToken);

            foreach (var (path, linkTarget) in links)
            {
                var source = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, linkTarget));
                if (File.Exists(source) && !File.Exists(path))
                {
                    File.Copy(source, path);
                }
            }

            var javaw = Path.Combine(home, "bin", "javaw.exe");
            if (!File.Exists(javaw))
            {
                throw new JavaRuntimeUnavailableException($"Java {requirement.MajorVersion} was downloaded, but bin\\javaw.exe is missing.");
            }

            await File.WriteAllTextAsync(Path.Combine(home, ManifestFileName), manifestJson, cancellationToken);
            var marker = new RuntimeMarker(component, versionName, requirement.MajorVersion, manifestSha1, DateTimeOffset.Now);
            await File.WriteAllTextAsync(Path.Combine(home, MarkerFileName), JsonSerializer.Serialize(marker), cancellationToken);

            _logger.Info($"Java runtime ready: {component} {versionName} in '{home}'.");
            await ScanAsync(cancellationToken);
            return FindManagedRuntime(requirement.MajorVersion)
                   ?? throw new JavaRuntimeUnavailableException($"Java {requirement.MajorVersion} could not be detected after installation.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not JavaRuntimeUnavailableException)
        {
            throw new JavaRuntimeUnavailableException($"Java {requirement.MajorVersion} could not be installed: {ex.Message}", ex);
        }
        finally
        {
            _installLock.Release();
        }
    }

    private async Task<(string Component, string ManifestUrl, string? ManifestSha1, string VersionName)> ResolveFromIndexAsync(JavaRequirement requirement, CancellationToken cancellationToken)
    {
        var index = JsonNode.Parse(await _downloads.GetStringAsync(RuntimeIndexUrl, cancellationToken))?[Platform]?.AsObject()
                    ?? throw new JavaRuntimeUnavailableException("Mojang's Java runtime index has no Windows x64 entries.");

        IEnumerable<string> candidates = requirement.Component is not null
            ? new[] { requirement.Component }
            : index.Select(kv => kv.Key);

        foreach (var component in candidates)
        {
            var entry = index[component]?.AsArray().FirstOrDefault();
            var versionName = entry?["version"]?["name"]?.GetValue<string>();
            var url = entry?["manifest"]?["url"]?.GetValue<string>();
            if (versionName is null || url is null || ParseMajor(versionName) != requirement.MajorVersion)
            {
                continue;
            }

            return (component, url, entry?["manifest"]?["sha1"]?.GetValue<string>(), versionName);
        }

        throw new JavaRuntimeUnavailableException($"Java {requirement.MajorVersion} is not offered for Windows x64 by the official runtime distribution.");
    }

    private JavaRuntimeInfo ScanCore(CancellationToken cancellationToken)
    {
        var managed = new List<JavaRuntime>();
        Directory.CreateDirectory(RuntimeRoot);

        foreach (var directory in Directory.EnumerateDirectories(RuntimeRoot, "java-*"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runtime = TryReadManaged(directory) ?? TryReadRelease(directory, JavaRuntimeSource.Managed);
            if (runtime is not null)
            {
                managed.Add(runtime);
            }
        }

        return new JavaRuntimeInfo
        {
            RuntimeDirectory = RuntimeRoot,
            ManagedRuntimes = managed.OrderByDescending(r => r.MajorVersion).ToList(),
            SystemRuntime = DetectSystemRuntime(),
            ScannedAt = DateTimeOffset.Now,
        };
    }

    private static JavaRuntime? TryReadManaged(string home)
    {
        var markerPath = Path.Combine(home, MarkerFileName);
        var javaw = Path.Combine(home, "bin", "javaw.exe");
        if (!File.Exists(markerPath) || !File.Exists(javaw))
        {
            return null;
        }

        try
        {
            var marker = JsonSerializer.Deserialize<RuntimeMarker>(File.ReadAllText(markerPath));
            return marker is null
                ? null
                : new JavaRuntime(marker.Major, marker.VersionName, "Mojang runtime", home, javaw, JavaRuntimeSource.Managed, marker.Component);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Optional detection of a system Java via JAVA_HOME. Purely informational.</summary>
    private JavaRuntime? DetectSystemRuntime()
    {
        try
        {
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            return string.IsNullOrWhiteSpace(javaHome) ? null : TryReadRelease(javaHome, JavaRuntimeSource.System);
        }
        catch (Exception ex)
        {
            _logger.Warning("System Java detection failed.", ex);
            return null;
        }
    }

    /// <summary>Reads a runtime via its standard "release" file. Nothing is guessed.</summary>
    private static JavaRuntime? TryReadRelease(string home, JavaRuntimeSource source)
    {
        var executable = new[] { "javaw.exe", "java.exe" }
            .Select(name => Path.Combine(home, "bin", name))
            .FirstOrDefault(File.Exists);
        var releaseFile = Path.Combine(home, "release");
        if (executable is null || !File.Exists(releaseFile))
        {
            return null;
        }

        var values = File.ReadAllLines(releaseFile)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[0].Trim())
            .ToDictionary(g => g.Key, g => g.First()[1].Trim().Trim('"'));

        if (!values.TryGetValue("JAVA_VERSION", out var version) || ParseMajor(version) <= 0)
        {
            return null;
        }

        values.TryGetValue("IMPLEMENTOR", out var vendor);
        return new JavaRuntime(ParseMajor(version), version, string.IsNullOrWhiteSpace(vendor) ? null : vendor, home, executable, source);
    }

    private static int ParseMajor(string version)
    {
        var parts = version.Split('.', '_', '+', '-');
        if (!int.TryParse(parts[0], out var first))
        {
            return 0;
        }

        return first == 1 && parts.Length > 1 && int.TryParse(parts[1], out var second) ? second : first;
    }

    private static bool Sha1Matches(string content, string expected)
    {
        var hash = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(content));
        return string.Equals(Convert.ToHexString(hash), expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Prevents path traversal from manifest entries.</summary>
    private static string SafeCombine(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Invalid path in runtime manifest: {relative}");
        }

        return full;
    }

    private sealed record RuntimeMarker(string Component, string VersionName, int Major, string? ManifestSha1, DateTimeOffset InstalledAt);
}
