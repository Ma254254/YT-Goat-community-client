using System.IO;
using GoatClient.Models;
using GoatClient.Services.Logging;

namespace GoatClient.Services.Java;

public sealed class JavaService : IJavaService
{
    private readonly ILogger _logger;

    public JavaService(string runtimeRoot, ILogger logger)
    {
        RuntimeRoot = runtimeRoot;
        _logger = logger;
        Info = JavaRuntimeInfo.Empty(runtimeRoot);
    }

    public string RuntimeRoot { get; }

    public JavaRuntimeInfo Info { get; private set; }

    public bool SupportsAutomaticProvisioning => false;

    public event EventHandler? RuntimesChanged;

    public async Task<JavaRuntimeInfo> ScanAsync(CancellationToken cancellationToken = default)
    {
        var info = await Task.Run(() => ScanCore(cancellationToken), cancellationToken);
        Info = info;

        _logger.Info(info.HasManagedRuntime
            ? $"Managed Java runtimes found: {string.Join(", ", info.ManagedRuntimes.Select(r => r.DisplayName))}."
            : "No managed Java runtime installed yet (automatic provisioning arrives in a future phase).");
        if (info.SystemRuntime is not null)
        {
            _logger.Info($"System Java detected (informational only, not required): {info.SystemRuntime.DisplayName}.");
        }

        RuntimesChanged?.Invoke(this, EventArgs.Empty);
        return info;
    }

    public int ResolveTargetMajor(JavaRuntimePreference preference, MinecraftVersionInfo? version) => preference switch
    {
        JavaRuntimePreference.Java17 => 17,
        JavaRuntimePreference.Java21 => 21,
        _ => version?.RequiredJavaMajor ?? 21,
    };

    public JavaRuntime? FindManagedRuntime(int majorVersion)
        => Info.ManagedRuntimes.FirstOrDefault(r => r.MajorVersion == majorVersion);

    public string GetManagedRuntimeDirectory(int majorVersion)
        => Path.Combine(RuntimeRoot, $"java-{majorVersion}");

    private JavaRuntimeInfo ScanCore(CancellationToken cancellationToken)
    {
        var managed = new List<JavaRuntime>();
        Directory.CreateDirectory(RuntimeRoot);

        foreach (var directory in Directory.EnumerateDirectories(RuntimeRoot, "java-*"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runtime = TryReadRuntime(directory, JavaRuntimeSource.Managed);
            if (runtime is not null)
            {
                managed.Add(runtime);
            }
            else
            {
                _logger.Warning($"'{directory}' is not a valid Java runtime (missing bin\\javaw.exe or release file).");
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

    /// <summary>Optional detection via JAVA_HOME or PATH. Purely informational.</summary>
    private JavaRuntime? DetectSystemRuntime()
    {
        try
        {
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrWhiteSpace(javaHome))
            {
                var fromHome = TryReadRuntime(javaHome, JavaRuntimeSource.System);
                if (fromHome is not null)
                {
                    return fromHome;
                }
            }

            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(entry.Trim(), "java.exe");
                if (File.Exists(candidate))
                {
                    var home = Directory.GetParent(entry.Trim().TrimEnd('\\', '/'))?.FullName;
                    if (home is not null && TryReadRuntime(home, JavaRuntimeSource.System) is { } runtime)
                    {
                        return runtime;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("System Java detection failed.", ex);
        }

        return null;
    }

    /// <summary>
    /// Reads a runtime from its home directory using the standard "release" file shipped with every
    /// modern JDK/JRE. Nothing is guessed: without a readable version the runtime is ignored.
    /// </summary>
    private static JavaRuntime? TryReadRuntime(string home, JavaRuntimeSource source)
    {
        var bin = Path.Combine(home, "bin");
        var executable = new[] { "javaw.exe", "java.exe" }
            .Select(name => Path.Combine(bin, name))
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

        if (!values.TryGetValue("JAVA_VERSION", out var version) || string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var major = ParseMajor(version);
        if (major <= 0)
        {
            return null;
        }

        values.TryGetValue("IMPLEMENTOR", out var vendor);
        return new JavaRuntime(major, version, string.IsNullOrWhiteSpace(vendor) ? null : vendor, home, executable, source);
    }

    private static int ParseMajor(string version)
    {
        var parts = version.Split('.', '_', '+', '-');
        if (!int.TryParse(parts[0], out var first))
        {
            return 0;
        }

        // Legacy format 1.8.0_xxx → 8
        return first == 1 && parts.Length > 1 && int.TryParse(parts[1], out var second) ? second : first;
    }
}
