using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Imaging;

namespace GoatClient.Services.Auth;

public sealed record SkinImages(BitmapSource Texture, BitmapSource Face, BitmapSource Hat);

/// <summary>Downloads the real skin texture of the signed-in account (textures.minecraft.net).</summary>
public sealed class SkinTextureLoader
{
    private readonly HttpClient _http;
    private string? _cachedUrl;
    private SkinImages? _cached;

    public SkinTextureLoader(HttpClient http)
    {
        _http = http;
    }

    public async Task<SkinImages?> LoadAsync(string? url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        if (url == _cachedUrl && _cached is not null)
        {
            return _cached;
        }

        var bytes = await _http.GetByteArrayAsync(url, cancellationToken);
        var texture = new BitmapImage();
        texture.BeginInit();
        texture.CacheOption = BitmapCacheOption.OnLoad;
        texture.StreamSource = new MemoryStream(bytes);
        texture.EndInit();
        texture.Freeze();

        // Face (8,8) and hat layer (40,8) are at the same position in 64x64 and legacy 64x32 skins.
        var face = new CroppedBitmap(texture, new Int32Rect(8, 8, 8, 8));
        face.Freeze();
        var hat = new CroppedBitmap(texture, new Int32Rect(40, 8, 8, 8));
        hat.Freeze();

        _cachedUrl = url;
        _cached = new SkinImages(texture, face, hat);
        return _cached;
    }
}
