using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoatClient.Core;
using GoatClient.Core.Constants;
using GoatClient.Core.Helpers;
using GoatClient.Models;
using GoatClient.Services.Logging;
using GoatClient.Services.Settings;

namespace GoatClient.Services.Auth;

/// <summary>
/// Real Microsoft account sign-in for Minecraft:
/// Microsoft identity (OAuth 2.0 device code flow) → Xbox Live → XSTS → Minecraft Services → profile.
/// Only the Microsoft refresh token is persisted – in the Windows Credential Manager.
/// Access tokens live in memory only and are never logged.
/// </summary>
public sealed class MicrosoftAuthService : ObservableObject, IAuthService
{
    private const string Authority = "https://login.microsoftonline.com/consumers/oauth2/v2.0";
    private const string Scope = "XboxLive.signin offline_access";
    private const string RefreshTokenKey = "msa-refresh-token";

    private readonly HttpClient _http;
    private readonly ISecureTokenStore _tokens;
    private readonly ISettingsService _settings;
    private readonly MicrosoftAuthConfig _bundled;
    private readonly JsonFileStore _store;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);

    /// <summary>Xbox endpoints expect the exact (PascalCase) property names – no camelCase policy.</summary>
    private static readonly JsonSerializerOptions ExactNames = new();

    private MinecraftAccount? _account;
    private AuthState _state = AuthState.SignedOut;
    private MinecraftSession? _session;

    public MicrosoftAuthService(HttpClient http, ISecureTokenStore tokens, ISettingsService settings, JsonFileStore store, MicrosoftAuthConfig bundled, ILogger logger)
    {
        _bundled = bundled;
        _http = http;
        _tokens = tokens;
        _settings = settings;
        _store = store;
        _logger = logger;
    }

    public MinecraftAccount? Account
    {
        get => _account;
        private set => SetProperty(ref _account, value);
    }

    public AuthState State
    {
        get => _state;
        private set => SetProperty(ref _state, value);
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);

    /// <summary>Settings override (advanced) first, otherwise the ID shipped in microsoft-auth.json.</summary>
    private string ClientId => !string.IsNullOrWhiteSpace(_settings.Current.MicrosoftClientId)
        ? _settings.Current.MicrosoftClientId
        : _bundled.ClientId ?? string.Empty;

    /// <summary>Where the active client ID comes from (for the Settings page).</summary>
    public string ClientIdSource => !string.IsNullOrWhiteSpace(_settings.Current.MicrosoftClientId)
        ? "Settings override"
        : _bundled.Source;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        OnPropertyChanged(nameof(IsConfigured));
        var cached = await _store.LoadAsync<MinecraftAccount>(AppPaths.AccountFile, cancellationToken);
        var hasToken = ReadRefreshToken() is not null;
        if (cached is null || !hasToken)
        {
            State = AuthState.SignedOut;
            return;
        }

        Account = cached;
        State = AuthState.SignedIn;
        try
        {
            await GetSessionAsync(cancellationToken);
        }
        catch (AuthException ex) when (!ex.RequiresSignIn)
        {
            // Network problem: keep the cached account, refresh again before launch.
            _logger.Warning($"Silent sign-in refresh failed: {ex.Message}");
        }
    }

    public async Task<DeviceCodeInfo> StartDeviceLoginAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var response = await PostFormAsync($"{Authority}/devicecode", new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["scope"] = Scope,
        }, cancellationToken);

        if (response["error"] is not null)
        {
            throw new AuthException(DescribeOAuthError(response), requiresSignIn: true);
        }

        return new DeviceCodeInfo(
            Required(response, "user_code"),
            Required(response, "verification_uri"),
            Required(response, "device_code"),
            response["interval"]?.GetValue<int>() ?? 5,
            DateTimeOffset.UtcNow.AddSeconds(response["expires_in"]?.GetValue<int>() ?? 900),
            response["message"]?.GetValue<string>() ?? string.Empty);
    }

    public async Task CompleteDeviceLoginAsync(DeviceCodeInfo code, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var interval = TimeSpan.FromSeconds(Math.Max(code.IntervalSeconds, 1));
        while (true)
        {
            if (DateTimeOffset.UtcNow > code.ExpiresAt)
            {
                throw new AuthException("The sign-in code expired. Please start the sign-in again.", requiresSignIn: true);
            }

            await Task.Delay(interval, cancellationToken);
            var response = await PostFormAsync($"{Authority}/token", new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = ClientId,
                ["device_code"] = code.DeviceCode,
            }, cancellationToken);

            switch (response["error"]?.GetValue<string>())
            {
                case null:
                    await CompleteWithMicrosoftTokensAsync(response, cancellationToken);
                    return;
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += TimeSpan.FromSeconds(5);
                    continue;
                case "authorization_declined":
                    throw new AuthException("The sign-in was declined.", requiresSignIn: true);
                case "expired_token":
                    throw new AuthException("The sign-in code expired. Please start the sign-in again.", requiresSignIn: true);
                default:
                    throw new AuthException(DescribeOAuthError(response), requiresSignIn: true);
            }
        }
    }

    public async Task<MinecraftSession> GetSessionAsync(CancellationToken cancellationToken)
    {
        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            if (_session is { IsValid: true })
            {
                return _session;
            }

            EnsureConfigured();
            var refreshToken = ReadRefreshToken()
                               ?? throw new AuthException("Please sign in with your Microsoft account.", requiresSignIn: true);

            JsonNode response;
            try
            {
                response = await PostFormAsync($"{Authority}/token", new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = ClientId,
                    ["refresh_token"] = refreshToken,
                    ["scope"] = Scope,
                }, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new AuthException("Microsoft sign-in is not reachable. Check your internet connection.", requiresSignIn: false, ex);
            }

            if (response["error"] is not null)
            {
                var error = response["error"]?.ToString() ?? "unknown";
                _logger.Warning("Token refresh rejected: " + error);
                MarkExpired();
                throw new AuthException("Please sign in again.", requiresSignIn: true);
            }

            await CompleteWithMicrosoftTokensAsync(response, cancellationToken);
            return _session ?? throw new AuthException("Sign-in did not return a session.", requiresSignIn: true);
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task SignOutAsync()
    {
        _session = null;
        try
        {
            _tokens.Delete(RefreshTokenKey);
        }
        catch (Exception ex)
        {
            _logger.Error("Removing the stored sign-in from the Credential Manager failed.", ex);
        }

        try
        {
            if (File.Exists(AppPaths.AccountFile))
            {
                File.Delete(AppPaths.AccountFile);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("Could not delete the cached account profile.", ex);
        }

        Account = null;
        State = AuthState.SignedOut;
        _logger.Info("Signed out.");
        await Task.CompletedTask;
    }

    /// <summary>Microsoft tokens → Xbox Live → XSTS → Minecraft → profile.</summary>
    private async Task CompleteWithMicrosoftTokensAsync(JsonNode microsoftTokens, CancellationToken cancellationToken)
    {
        var msAccessToken = Required(microsoftTokens, "access_token");
        var newRefreshToken = microsoftTokens["refresh_token"]?.GetValue<string>();

        // Xbox Live user token
        var xbl = await PostJsonAsync("https://user.auth.xboxlive.com/user/authenticate", new
        {
            Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + msAccessToken },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT",
        }, cancellationToken);
        var xblToken = Required(xbl.Body, "Token");

        // XSTS token for Minecraft services
        var xsts = await PostJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize", new
        {
            Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xblToken } },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT",
        }, cancellationToken, throwOnError: false);

        if (xsts.Status == HttpStatusCode.Unauthorized)
        {
            throw new AuthException(DescribeXstsError(xsts.Body["XErr"]?.GetValue<long>()), requiresSignIn: true);
        }

        EnsureSuccess(xsts, "Xbox Live security token");
        var xstsToken = Required(xsts.Body, "Token");
        var userHash = xsts.Body["DisplayClaims"]?["xui"]?[0]?["uhs"]?.GetValue<string>()
                       ?? throw new AuthException("Xbox Live did not return a user hash.", requiresSignIn: true);
        var xuid = xsts.Body["DisplayClaims"]?["xui"]?[0]?["xid"]?.GetValue<string>();

        // Minecraft services login
        var minecraft = await PostJsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox", new
        {
            identityToken = $"XBL3.0 x={userHash};{xstsToken}",
        }, cancellationToken, throwOnError: false);

        if (minecraft.Status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            throw new AuthException(
                "Minecraft services rejected the sign-in. The configured Microsoft application may not be approved for Minecraft yet (see README → Microsoft Authentication).",
                requiresSignIn: true);
        }

        EnsureSuccess(minecraft, "Minecraft services");
        var minecraftToken = Required(minecraft.Body, "access_token");
        var expiresIn = minecraft.Body["expires_in"]?.GetValue<int>() ?? 86400;

        var account = await FetchProfileAsync(minecraftToken, cancellationToken);

        if (!string.IsNullOrEmpty(newRefreshToken))
        {
            _tokens.Write(RefreshTokenKey, newRefreshToken);
        }

        await _store.SaveAsync(AppPaths.AccountFile, account, cancellationToken);

        _session = new MinecraftSession(account, minecraftToken, xuid, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
        Account = account;
        State = AuthState.SignedIn;
        _logger.Info($"Signed in as {account.Username}.");
    }

    public async Task RefreshProfileAsync(CancellationToken cancellationToken)
    {
        var session = await GetSessionAsync(cancellationToken);
        var account = await FetchProfileAsync(session.AccessToken, cancellationToken);
        _session = session with { Account = account };
        await _store.SaveAsync(AppPaths.AccountFile, account, cancellationToken);
        Account = account;
    }

    /// <summary>Real profile – 404 means the account does not own Minecraft: Java Edition.</summary>
    private async Task<MinecraftAccount> FetchProfileAsync(string minecraftToken, CancellationToken cancellationToken)
    {
        using var profileRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
        profileRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", minecraftToken);
        using var profileResponse = await _http.SendAsync(profileRequest, cancellationToken);
        if (profileResponse.StatusCode == HttpStatusCode.NotFound)
        {
            throw new AuthException("This Microsoft account does not own Minecraft: Java Edition (no Minecraft profile found).", requiresSignIn: true);
        }

        profileResponse.EnsureSuccessStatusCode();
        var profile = JsonNode.Parse(await profileResponse.Content.ReadAsStringAsync(cancellationToken))
                      ?? throw new AuthException("The Minecraft profile response was empty.", requiresSignIn: true);

        var activeSkin = profile["skins"]?.AsArray()
            .FirstOrDefault(s => s?["state"]?.GetValue<string>() == "ACTIVE");
        return new MinecraftAccount(
            Required(profile, "name"),
            Required(profile, "id"),
            activeSkin?["url"]?.GetValue<string>()?.Replace("http://", "https://", StringComparison.OrdinalIgnoreCase),
            activeSkin?["variant"]?.GetValue<string>());
    }

    private string? ReadRefreshToken()
    {
        try
        {
            return _tokens.Read(RefreshTokenKey);
        }
        catch (Exception ex)
        {
            _logger.Error("Reading the stored sign-in from the Credential Manager failed.", ex);
            return null;
        }
    }

    private void MarkExpired()
    {
        _session = null;
        State = AuthState.SessionExpired;
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new AuthException(
                "Microsoft sign-in is not configured. Put your approved Azure app (client) ID into microsoft-auth.json next to GoatClient.exe (see README).",
                requiresSignIn: true);
        }
    }

    private async Task<JsonNode> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(url, content, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseOrEmpty(text);
    }

    private async Task<(HttpStatusCode Status, JsonNode Body)> PostJsonAsync(string url, object payload, CancellationToken cancellationToken, bool throwOnError = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload, payload.GetType(), options: ExactNames) };
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await _http.SendAsync(request, cancellationToken);
        var body = ParseOrEmpty(await response.Content.ReadAsStringAsync(cancellationToken));
        var result = (response.StatusCode, body);
        if (throwOnError)
        {
            EnsureSuccess(result, new Uri(url).Host);
        }

        return result;
    }

    private static void EnsureSuccess((HttpStatusCode Status, JsonNode Body) result, string step)
    {
        if ((int)result.Status is < 200 or > 299)
        {
            throw new AuthException($"Sign-in failed at {step} (HTTP {(int)result.Status}).", requiresSignIn: true);
        }
    }

    private static JsonNode ParseOrEmpty(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(text) ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private static string Required(JsonNode node, string property)
        => node[property]?.GetValue<string>() is { Length: > 0 } value
            ? value
            : throw new AuthException($"Sign-in response is missing '{property}'.", requiresSignIn: true);

    private static string DescribeOAuthError(JsonNode response)
    {
        var error = response["error"]?.GetValue<string>() ?? "unknown_error";
        return error switch
        {
            "invalid_client" or "unauthorized_client" => "The configured Microsoft application (client) ID is invalid or not enabled for personal accounts and device code sign-in.",
            "invalid_scope" => "The Microsoft application is not allowed to request Xbox Live access.",
            _ => $"Microsoft sign-in failed ({error}).",
        };
    }

    private static string DescribeXstsError(long? code) => code switch
    {
        2148916233 => "This Microsoft account has no Xbox profile yet. Sign in once at xbox.com to create one, then try again.",
        2148916235 => "Xbox Live is not available in your country or region.",
        2148916236 or 2148916237 => "This account requires adult verification on xbox.com (South Korea).",
        2148916238 => "This is a child account. It must be added to a Microsoft family by an adult before it can sign in.",
        _ => "Xbox Live denied the sign-in.",
    };
}
