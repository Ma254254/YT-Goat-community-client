using GoatClient.Models;

namespace GoatClient.Services.Java;

/// <summary>
/// Java runtime management. GOAT CLIENT never requires a system-wide Java installation:
/// runtimes live in %APPDATA%\GoatClient\runtime\java-{major}\.
/// Phase 1: scanning + selection logic. Automatic provisioning (download) follows in a later phase.
/// </summary>
public interface IJavaService
{
    string RuntimeRoot { get; }

    JavaRuntimeInfo Info { get; }

    /// <summary>Always false in Phase 1 – there is no download/provisioning implementation yet.</summary>
    bool SupportsAutomaticProvisioning { get; }

    event EventHandler? RuntimesChanged;

    Task<JavaRuntimeInfo> ScanAsync(CancellationToken cancellationToken = default);

    /// <summary>Java major that a profile would use for a given Minecraft version.</summary>
    int ResolveTargetMajor(JavaRuntimePreference preference, MinecraftVersionInfo? version);

    JavaRuntime? FindManagedRuntime(int majorVersion);

    string GetManagedRuntimeDirectory(int majorVersion);
}
