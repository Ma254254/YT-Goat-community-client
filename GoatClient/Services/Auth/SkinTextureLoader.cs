using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GoatClient.Services.Auth;

public sealed record SkinImages(BitmapSource Texture, BitmapSource Face, BitmapSource Hat, BitmapSource Front);

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

    public async Task<SkinImages?> LoadAsync(string? url, CancellationToken cancellationToken, bool slim = false)
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
        var images = FromBytes(bytes, slim);
        _cached = images;
        _cachedUrl = url;
        return images;
    }

    /// <summary>Raw PNG of a skin URL (e.g. to save the current skin into the library).</summary>
    public Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken)
        => _http.GetByteArrayAsync(url, cancellationToken);

    /// <summary>Builds texture, face, hat and a flat front preview from PNG bytes.</summary>
    public static SkinImages FromBytes(byte[] bytes, bool slim)
    {
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
        return new SkinImages(texture, face, hat, RenderFront(texture, slim));
    }

    public static SkinImages FromFile(string path, bool slim) => FromBytes(File.ReadAllBytes(path), slim);

    /// <summary>
    /// Flat front view (16×32 px): head, body, arms, legs plus the outer layer.
    /// Legacy 64×32 skins mirror the right arm/leg, exactly like the game does.
    /// </summary>
    public static BitmapSource RenderFront(BitmapSource texture, bool slim)
    {
        var modern = texture.PixelHeight >= 64;
        var armWidth = slim ? 3 : 4;
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);

        using (var dc = visual.RenderOpen())
        {
            void Part(int sx, int sy, int w, int h, int dx, int dy, bool mirror = false)
            {
                var part = new CroppedBitmap(texture, new Int32Rect(sx, sy, w, h));
                if (mirror)
                {
                    dc.PushTransform(new ScaleTransform(-1, 1, dx + (w / 2.0), 0));
                }

                dc.DrawImage(part, new Rect(dx, dy, w, h));
                if (mirror)
                {
                    dc.Pop();
                }
            }

            // Base layer
            Part(8, 8, 8, 8, 4, 0);                      // head
            Part(20, 20, 8, 12, 4, 8);                   // body
            Part(44, 20, armWidth, 12, 4 - armWidth, 8); // right arm (viewer's left)
            if (modern)
            {
                Part(36, 52, armWidth, 12, 12, 8);       // left arm
                Part(20, 52, 4, 12, 8, 20);              // left leg
            }
            else
            {
                Part(44, 20, armWidth, 12, 12, 8, mirror: true);
                Part(4, 20, 4, 12, 8, 20, mirror: true);
            }

            Part(4, 20, 4, 12, 4, 20);                   // right leg

            // Outer layer
            Part(40, 8, 8, 8, 4, 0);                     // hat
            if (modern)
            {
                Part(20, 36, 8, 12, 4, 8);               // jacket
                Part(44, 36, armWidth, 12, 4 - armWidth, 8);
                Part(52, 52, armWidth, 12, 12, 8);
                Part(4, 36, 4, 12, 4, 20);
                Part(4, 52, 4, 12, 8, 20);
            }
        }

        var bitmap = new RenderTargetBitmap(16, 32, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
