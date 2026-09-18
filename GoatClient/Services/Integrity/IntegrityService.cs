using System.IO;
using System.Security.Cryptography;

namespace GoatClient.Services.Integrity;

public sealed class IntegrityService : IIntegrityService
{
    public async Task<string> ComputeSha1Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var hash = await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<bool> IsValidAsync(string path, string? expectedSha1, long? expectedSize, bool verifyHash, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return false;
        }

        if (expectedSize is > 0 && info.Length != expectedSize)
        {
            return false;
        }

        if (!verifyHash || string.IsNullOrEmpty(expectedSha1))
        {
            return true;
        }

        var actual = await ComputeSha1Async(path, cancellationToken).ConfigureAwait(false);
        return string.Equals(actual, expectedSha1, StringComparison.OrdinalIgnoreCase);
    }
}
