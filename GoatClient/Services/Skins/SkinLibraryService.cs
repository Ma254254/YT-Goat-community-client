using System.IO;
using System.Windows.Media.Imaging;
using GoatClient.Core.Constants;
using GoatClient.Core.Helpers;
using GoatClient.Services.Logging;

namespace GoatClient.Services.Skins;

/// <summary>A skin PNG stored locally in %APPDATA%\GoatClient\skins.</summary>
public sealed class SkinLibraryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public bool Slim { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class SkinLibraryData
{
    public List<SkinLibraryEntry> Skins { get; set; } = new();
}

/// <summary>Local skin library: add, list, rename-free delete. Files are validated as real Minecraft skins.</summary>
public sealed class SkinLibraryService
{
    public static string Directory => Path.Combine(AppPaths.Root, "skins");

    private static string IndexFile => Path.Combine(Directory, "library.json");

    private readonly JsonFileStore _store;
    private readonly ILogger _logger;
    private SkinLibraryData _data = new();

    public SkinLibraryService(JsonFileStore store, ILogger logger)
    {
        _store = store;
        _logger = logger;
    }

    public IReadOnlyList<SkinLibraryEntry> Skins => _data.Skins;

    public event EventHandler? Changed;

    public string PathOf(SkinLibraryEntry entry) => Path.Combine(Directory, entry.FileName);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _data = await _store.LoadAsync<SkinLibraryData>(IndexFile, cancellationToken) ?? new SkinLibraryData();
        var removed = _data.Skins.RemoveAll(s => !File.Exists(PathOf(s)));
        if (removed > 0)
        {
            await SaveAsync(cancellationToken);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Checks that a PNG is a Minecraft skin (64×64, or legacy 64×32). Returns an error text or null.
    /// </summary>
    public static string? Validate(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            return frame.PixelWidth == 64 && (frame.PixelHeight == 64 || frame.PixelHeight == 32)
                ? null
                : $"Not a Minecraft skin: {frame.PixelWidth}×{frame.PixelHeight} px (expected 64×64 or 64×32).";
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException)
        {
            return "The file is not a valid PNG image.";
        }
    }

    public async Task<SkinLibraryEntry> AddFileAsync(string sourcePath, string name, bool slim, CancellationToken cancellationToken = default)
    {
        if (Validate(sourcePath) is { } error)
        {
            throw new InvalidDataException(error);
        }

        return await AddBytesAsync(await File.ReadAllBytesAsync(sourcePath, cancellationToken), name, slim, cancellationToken);
    }

    public async Task<SkinLibraryEntry> AddBytesAsync(byte[] png, string name, bool slim, CancellationToken cancellationToken = default)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var entry = new SkinLibraryEntry
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Skin" : name.Trim(),
            Slim = slim,
        };
        entry.FileName = entry.Id.ToString("N") + ".png";
        await File.WriteAllBytesAsync(PathOf(entry), png, cancellationToken);

        if (Validate(PathOf(entry)) is { } error)
        {
            File.Delete(PathOf(entry));
            throw new InvalidDataException(error);
        }

        _data.Skins.Insert(0, entry);
        await SaveAsync(cancellationToken);
        _logger.Info($"Skin '{entry.Name}' added to the library.");
        Changed?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    public async Task RemoveAsync(SkinLibraryEntry entry, CancellationToken cancellationToken = default)
    {
        _data.Skins.Remove(entry);
        try
        {
            File.Delete(PathOf(entry));
        }
        catch (IOException ex)
        {
            _logger.Warning($"Skin file '{entry.FileName}' could not be deleted.", ex);
        }

        await SaveAsync(cancellationToken);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Task SaveAsync(CancellationToken cancellationToken) => _store.SaveAsync(IndexFile, _data, cancellationToken);
}
