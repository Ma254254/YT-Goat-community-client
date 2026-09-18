namespace GoatClient.Models;

/// <summary>Real Minecraft profile returned by api.minecraftservices.com. Not secret; cached for display.</summary>
public sealed record MinecraftAccount(string Username, string Uuid, string? SkinUrl, string? SkinVariant)
{
    /// <summary>UUID in the dashed 8-4-4-4-12 format.</summary>
    public string FormattedUuid => Uuid.Length == 32
        ? $"{Uuid[..8]}-{Uuid[8..12]}-{Uuid[12..16]}-{Uuid[16..20]}-{Uuid[20..]}"
        : Uuid;
}

/// <summary>In-memory session used for launching. Never written to disk or logs.</summary>
public sealed record MinecraftSession(MinecraftAccount Account, string AccessToken, string? Xuid, DateTimeOffset ExpiresAt)
{
    public bool IsValid => DateTimeOffset.UtcNow < ExpiresAt.AddMinutes(-5);
}

/// <summary>Device code sign-in data shown to the user.</summary>
public sealed record DeviceCodeInfo(string UserCode, string VerificationUri, string DeviceCode, int IntervalSeconds, DateTimeOffset ExpiresAt, string Message);

public enum AuthState
{
    SignedOut,
    SignedIn,
    SessionExpired,
}
