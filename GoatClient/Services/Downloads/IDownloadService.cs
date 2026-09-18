using GoatClient.Models;

namespace GoatClient.Services.Downloads;

public interface IDownloadService
{
    /// <summary>Downloads a text resource (metadata JSON).</summary>
    Task<string> GetStringAsync(string url, CancellationToken cancellationToken);

    /// <summary>Downloads a single file with retry, resume and hash verification.</summary>
    Task DownloadFileAsync(DownloadRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Downloads many files in parallel. Existing files are detected and skipped when valid
    /// (size check, or full SHA-1 check when <paramref name="verifyExisting"/> is true).
    /// </summary>
    Task DownloadManyAsync(
        IReadOnlyList<DownloadRequest> requests,
        string stage,
        bool verifyExisting,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>A download that failed after all retries.</summary>
public sealed class DownloadFailedException : Exception
{
    public DownloadFailedException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
