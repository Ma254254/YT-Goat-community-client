namespace GoatClient.Models;

public enum JavaRuntimeSource
{
    /// <summary>Runtime located inside %APPDATA%\GoatClient\runtime (owned by GOAT CLIENT).</summary>
    Managed,

    /// <summary>A system-wide Java installation. Informational only – never required.</summary>
    System,
}

/// <summary>A Java runtime that was actually found on disk (never invented).</summary>
public sealed record JavaRuntime(
    int MajorVersion,
    string Version,
    string? Vendor,
    string HomeDirectory,
    string ExecutablePath,
    JavaRuntimeSource Source)
{
    public string DisplayName => Vendor is null ? $"Java {Version}" : $"Java {Version} ({Vendor})";
}

/// <summary>Result of a runtime scan.</summary>
public sealed class JavaRuntimeInfo
{
    public static JavaRuntimeInfo Empty(string runtimeDirectory) => new()
    {
        RuntimeDirectory = runtimeDirectory,
        ScannedAt = null,
    };

    public required string RuntimeDirectory { get; init; }

    public IReadOnlyList<JavaRuntime> ManagedRuntimes { get; init; } = Array.Empty<JavaRuntime>();

    /// <summary>Optional information about a system Java. GOAT CLIENT does not depend on it.</summary>
    public JavaRuntime? SystemRuntime { get; init; }

    public DateTimeOffset? ScannedAt { get; init; }

    public bool HasManagedRuntime => ManagedRuntimes.Count > 0;
}
