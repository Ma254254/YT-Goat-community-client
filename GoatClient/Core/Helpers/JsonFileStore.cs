using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoatClient.Services.Logging;

namespace GoatClient.Core.Helpers;

/// <summary>
/// Reads and writes JSON files safely:
/// atomic writes (temp file + move) and quarantining of corrupt files instead of crashing.
/// </summary>
public sealed class JsonFileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ILogger _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public JsonFileStore(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>Returns null if the file does not exist or was corrupt (corrupt files are moved aside).</summary>
    public async Task<T?> LoadAsync<T>(string path, CancellationToken cancellationToken = default)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.Error($"The file '{path}' is corrupt and will be replaced with defaults.", ex);
            Quarantine(path);
            return null;
        }
    }

    public async Task SaveAsync<T>(string path, T data, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = path + ".tmp";
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, data, Options, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void Quarantine(string path)
    {
        try
        {
            var target = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(path, target, overwrite: true);
            _logger.Warning($"Corrupt file backed up to '{target}'.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not back up corrupt file '{path}'.", ex);
        }
    }
}
