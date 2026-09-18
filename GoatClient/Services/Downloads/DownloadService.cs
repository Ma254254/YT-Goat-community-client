using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using GoatClient.Models;
using GoatClient.Services.Integrity;
using GoatClient.Services.Logging;
using GoatClient.Services.Settings;

namespace GoatClient.Services.Downloads;

/// <summary>
/// Robust HTTP downloader: parallel transfers, retries with backoff, resume of partial files
/// (HTTP range requests), SHA-1 verification, progress and speed reporting, cancellation.
/// </summary>
public sealed class DownloadService : IDownloadService
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(200);

    private readonly HttpClient _http;
    private readonly IIntegrityService _integrity;
    private readonly ISettingsService _settings;
    private readonly ILogger _logger;

    public DownloadService(HttpClient http, IIntegrityService integrity, ISettingsService settings, ILogger logger)
    {
        _http = http;
        _integrity = integrity;
        _settings = settings;
        _logger = logger;
    }

    public async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        var attempts = RetryCount + 1;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransient(ex, cancellationToken) && attempt < attempts)
            {
                _logger.Warning($"Request failed (attempt {attempt}/{attempts}): {url} – {ex.Message}");
                await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public Task DownloadFileAsync(DownloadRequest request, CancellationToken cancellationToken)
        => DownloadWithRetryAsync(request, _ => { }, cancellationToken);

    public async Task DownloadManyAsync(
        IReadOnlyList<DownloadRequest> requests,
        string stage,
        bool verifyExisting,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        var unique = requests
            .GroupBy(r => Path.GetFullPath(r.Path), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        // 1. Detect files that are already present and valid.
        progress?.Report(new TransferProgress(verifyExisting ? $"{stage} – verifying files…" : $"{stage} – checking files…", 0, 0, 0, unique.Count, 0));
        var missing = new List<DownloadRequest>();
        var checkedCount = 0;
        var parallelism = Math.Clamp(Environment.ProcessorCount, 2, 8);
        await Parallel.ForEachAsync(unique, new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = cancellationToken }, async (request, ct) =>
        {
            var valid = await _integrity.IsValidAsync(request.Path, request.Sha1, request.Size, verifyExisting, ct).ConfigureAwait(false);
            if (!valid)
            {
                lock (missing)
                {
                    missing.Add(request);
                }
            }

            var done = Interlocked.Increment(ref checkedCount);
            if (done % 200 == 0)
            {
                progress?.Report(new TransferProgress($"{stage} – checking files…", 0, 0, done, unique.Count, 0));
            }
        }).ConfigureAwait(false);

        if (missing.Count == 0)
        {
            progress?.Report(new TransferProgress(stage, 1, 1, unique.Count, unique.Count, 0));
            return;
        }

        _logger.Info($"{stage}: {missing.Count} of {unique.Count} file(s) need to be downloaded.");

        // 2. Download missing/corrupt files in parallel.
        var totalBytes = missing.Sum(r => r.Size ?? 0);
        long doneBytes = 0;
        var doneFiles = 0;
        var stopwatch = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        long lastBytes = 0;
        double speed = 0;
        var reportLock = new object();

        void Report(bool force)
        {
            lock (reportLock)
            {
                var elapsed = stopwatch.Elapsed;
                if (!force && elapsed - lastReport < ReportInterval)
                {
                    return;
                }

                var bytes = Interlocked.Read(ref doneBytes);
                var window = (elapsed - lastReport).TotalSeconds;
                if (window > 0)
                {
                    var instant = (bytes - lastBytes) / window;
                    speed = speed <= 0 ? instant : (speed * 0.7) + (instant * 0.3);
                }

                lastReport = elapsed;
                lastBytes = bytes;
                progress?.Report(new TransferProgress(stage, bytes, Math.Max(totalBytes, bytes), Volatile.Read(ref doneFiles), missing.Count, speed));
            }
        }

        var maxParallel = Math.Clamp(_settings.Current.MaxParallelDownloads, 1, 32);
        await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = maxParallel, CancellationToken = cancellationToken }, async (request, ct) =>
        {
            await DownloadWithRetryAsync(request, delta =>
            {
                Interlocked.Add(ref doneBytes, delta);
                Report(false);
            }, ct).ConfigureAwait(false);
            Interlocked.Increment(ref doneFiles);
            Report(false);
        }).ConfigureAwait(false);

        Report(true);
    }

    private int RetryCount => Math.Clamp(_settings.Current.DownloadRetryCount, 0, 10);

    private async Task DownloadWithRetryAsync(DownloadRequest request, Action<long> onBytes, CancellationToken cancellationToken)
    {
        var attempts = RetryCount + 1;
        for (var attempt = 1; ; attempt++)
        {
            long reported = 0;
            try
            {
                await DownloadOnceAsync(request, delta =>
                {
                    reported += delta;
                    onBytes(delta);
                }, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (IsTransient(ex, cancellationToken) && attempt < attempts)
            {
                onBytes(-reported); // progress restarts for this file
                _logger.Warning($"Download failed (attempt {attempt}/{attempts}): {request.Url} – {ex.Message}");
                await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                onBytes(-reported);
                throw new DownloadFailedException($"Download failed: {Path.GetFileName(request.Path)} ({ex.Message})", ex);
            }
        }
    }

    private async Task DownloadOnceAsync(DownloadRequest request, Action<long> onBytes, CancellationToken cancellationToken)
    {
        var partPath = GetPartPath(request);
        Directory.CreateDirectory(Path.GetDirectoryName(partPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.Path))!);

        var existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
        if (request.Size is { } size && existing >= size)
        {
            // Complete or oversized part file from an earlier run – verify below or start over.
            if (existing > size)
            {
                File.Delete(partPath);
                existing = 0;
            }
        }

        using var message = new HttpRequestMessage(HttpMethod.Get, request.Url);
        if (existing > 0 && (request.Size is null || existing < request.Size))
        {
            message.Headers.Range = new RangeHeaderValue(existing, null);
        }

        if (request.Size is null || existing < request.Size)
        {
            using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                File.Delete(partPath);
                throw new IOException("Partial file could not be resumed; restarting.");
            }

            response.EnsureSuccessStatusCode();
            var resumed = response.StatusCode == HttpStatusCode.PartialContent;
            if (!resumed)
            {
                existing = 0;
            }
            else
            {
                onBytes(existing);
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (var target = new FileStream(partPath, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    onBytes(read);
                }
            }
        }
        else
        {
            onBytes(existing);
        }

        if (!await _integrity.IsValidAsync(partPath, request.Sha1, request.Size, verifyHash: true, cancellationToken).ConfigureAwait(false))
        {
            File.Delete(partPath);
            throw new IOException("Integrity check failed (size or SHA-1 mismatch).");
        }

        File.Move(partPath, request.Path, overwrite: true);
    }

    private string GetPartPath(DownloadRequest request)
    {
        var directory = _settings.Current.DownloadDirectory;
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
        {
            directory = Core.Constants.AppPaths.Downloads;
        }

        var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(request.Path).ToLowerInvariant()))).ToLowerInvariant();
        return Path.Combine(directory, key + ".part");
    }

    private static bool IsTransient(Exception ex, CancellationToken cancellationToken)
        => !cancellationToken.IsCancellationRequested
           && ex is HttpRequestException or IOException or TaskCanceledException;

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromMilliseconds(Math.Min(8000, 500 * Math.Pow(2, attempt - 1)));
}
