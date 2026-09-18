using System.Reflection;

namespace GoatClient.Core.Constants;

public static class AppInfo
{
    public const string ProductName = "GOAT CLIENT";
    public const string Tagline = "Built for the next level.";
    public const string Phase = "Phase 1";

    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
}
