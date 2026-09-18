using System.Reflection;

namespace GoatClient.Core.Constants;

public static class AppInfo
{
    public const string ProductName = "GOAT CLIENT";
    public const string Tagline = "Built for the next level.";
    public const string Phase = "Phase 2";

    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
}

public static class AppLinks
{
    /// <summary>Official GOAT CLIENT Discord invite.</summary>
    public const string DiscordInvite = "https://discord.gg/N8mPvMTPhz";
}
