using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using GoatClient.Services.Auth;
using GoatClient.Services.Logging;

namespace GoatClient.Services.Skins;

/// <summary>
/// Changes the account's skin via the official Minecraft services API.
/// Requires the GOAT CLIENT Microsoft sign-in (direct launch) – the official launcher's login is never used.
/// </summary>
public sealed class SkinUploadService
{
    private const string SkinsEndpoint = "https://api.minecraftservices.com/minecraft/profile/skins";

    private readonly HttpClient _http;
    private readonly IAuthService _auth;
    private readonly ILogger _logger;

    public SkinUploadService(HttpClient http, IAuthService auth, ILogger logger)
    {
        _http = http;
        _auth = auth;
        _logger = logger;
    }

    public bool CanUpload => _auth.State == Models.AuthState.SignedIn;

    public async Task UploadAsync(string pngPath, bool slim, CancellationToken cancellationToken)
    {
        if (SkinLibraryService.Validate(pngPath) is { } error)
        {
            throw new InvalidDataException(error);
        }

        var session = await _auth.GetSessionAsync(cancellationToken);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(slim ? "slim" : "classic"), "variant");
        var file = new ByteArrayContent(await File.ReadAllBytesAsync(pngPath, cancellationToken));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", "skin.png");

        using var request = new HttpRequestMessage(HttpMethod.Post, SkinsEndpoint) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        EnsureSuccess(response, "upload");

        _logger.Info("Skin uploaded to the Minecraft account.");
        await _auth.RefreshProfileAsync(cancellationToken);
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        var session = await _auth.GetSessionAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Delete, SkinsEndpoint + "/active");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        EnsureSuccess(response, "reset");

        _logger.Info("Skin reset to the default skin.");
        await _auth.RefreshProfileAsync(cancellationToken);
    }

    private static void EnsureSuccess(HttpResponseMessage response, string action)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new InvalidOperationException(response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Your sign-in expired. Please sign in again.",
            HttpStatusCode.TooManyRequests => "Minecraft allows only a few skin changes per minute. Please wait a moment.",
            HttpStatusCode.BadRequest => "Minecraft rejected this skin file.",
            _ => $"The skin {action} failed (HTTP {(int)response.StatusCode}).",
        });
    }
}
