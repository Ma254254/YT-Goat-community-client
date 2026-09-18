using GoatClient.Models;

namespace GoatClient.Services.Java;

/// <summary>
/// Managed Java runtimes. GOAT CLIENT never requires a system-wide Java installation:
/// runtimes are downloaded from Mojang's official runtime distribution into
/// %APPDATA%\GoatClient\runtime\java-{major}\ and verified file by file (SHA-1).
/// </summary>
public interface IJavaService
{
    string RuntimeRoot { get; }

    JavaRuntimeInfo Info { get; }

    event EventHandler? RuntimesChanged;

    Task<JavaRuntimeInfo> ScanAsync(CancellationToken cancellationToken = default);

    /// <summary>Runtime requirement for a profile: explicit preference or the version's official requirement.</summary>
    JavaRequirement ResolveRequirement(JavaRuntimePreference preference, JavaRequirement? versionRequirement);

    JavaRuntime? FindManagedRuntime(int majorVersion);

    /// <summary>
    /// Makes sure the runtime exists (downloads it if missing). With <paramref name="verify"/> every
    /// file is hash-checked and corrupt/missing files are re-downloaded.
    /// </summary>
    Task<JavaRuntime> EnsureRuntimeAsync(JavaRequirement requirement, bool verify, IProgress<TransferProgress>? progress, CancellationToken cancellationToken);
}

public sealed class JavaRuntimeUnavailableException : Exception
{
    public JavaRuntimeUnavailableException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
