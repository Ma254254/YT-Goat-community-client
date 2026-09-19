using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoatClient.Models;

namespace GoatClient.Services.Skins;

/// <summary>
/// Public Mojang profile lookup by Minecraft name (the same public data NameMC shows).
/// No sign-in, no tokens – works together with the official Minecraft Launcher mode.
/// </summary>
public sealed partial class MojangProfileLookup
{
    private readonly HttpClient _http;

    public MojangProfileLookup(HttpClient http)
    {
        _http = http;
    }

    public static bool IsValidName(string? name) => name is not null && NameRegex().IsMatch(name);

    /// <summary>Returns the public profile, or null if no Minecraft account has this name.</summary>
    public async Task<MinecraftAccount?> FindByNameAsync(string name, CancellationToken cancellationToken)
    {
        if (!IsValidName(name))
        {
            throw new ArgumentException("A Minecraft name has 3–16 characters: letters, digits and _.", nameof(name));
        }

        string? uuid = null;
        using (var response = await _http.GetAsync($"https://api.mojang.com/users/profiles/minecraft/{Uri.EscapeDataString(name)}", cancellationToken))
        {
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
            {
                return null;
            }

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                uuid = doc.RootElement.GetProperty("id").GetString();
            }
        }

        if (uuid is null)
        {
            // Newer endpoint as fallback when the classic API is unavailable.
            using var response = await _http.GetAsync($"https://api.minecraftservices.com/minecraft/profile/lookup/name/{Uri.EscapeDataString(name)}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            uuid = doc.RootElement.GetProperty("id").GetString();
        }

        return uuid is null ? null : await GetByUuidAsync(uuid, cancellationToken);
    }

    /// <summary>Current name and skin of a profile from the public session server.</summary>
    public async Task<MinecraftAccount?> GetByUuidAsync(string uuid, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"https://sessionserver.mojang.com/session/minecraft/profile/{Uri.EscapeDataString(uuid)}", cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = doc.RootElement;
        var name = root.GetProperty("name").GetString() ?? string.Empty;
        var id = root.GetProperty("id").GetString() ?? uuid;

        string? skinUrl = null;
        string? variant = null;
        if (root.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateArray())
            {
                if (property.GetProperty("name").GetString() != "textures")
                {
                    continue;
                }

                var json = Encoding.UTF8.GetString(Convert.FromBase64String(property.GetProperty("value").GetString() ?? string.Empty));
                using var textures = JsonDocument.Parse(json);
                if (textures.RootElement.TryGetProperty("textures", out var t) && t.TryGetProperty("SKIN", out var skin))
                {
                    skinUrl = skin.GetProperty("url").GetString()?.Replace("http://", "https://", StringComparison.OrdinalIgnoreCase);
                    variant = skin.TryGetProperty("metadata", out var meta) && meta.TryGetProperty("model", out var model) && model.GetString() == "slim"
                        ? "SLIM"
                        : "CLASSIC";
                }
            }
        }

        return new MinecraftAccount(name, id, skinUrl, variant);
    }

    [GeneratedRegex("^[A-Za-z0-9_]{3,16}$")]
    private static partial Regex NameRegex();
}
