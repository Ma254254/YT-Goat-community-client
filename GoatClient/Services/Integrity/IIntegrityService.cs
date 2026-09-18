namespace GoatClient.Services.Integrity;

/// <summary>SHA-1 based file verification against official metadata.</summary>
public interface IIntegrityService
{
    Task<string> ComputeSha1Async(string path, CancellationToken cancellationToken);

    /// <summary>
    /// True if the file exists and matches the expected size/hash.
    /// Without a hash, only existence and (if known) size are checked.
    /// </summary>
    Task<bool> IsValidAsync(string path, string? expectedSha1, long? expectedSize, bool verifyHash, CancellationToken cancellationToken);
}
