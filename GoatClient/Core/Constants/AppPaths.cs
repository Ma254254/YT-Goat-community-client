using System.IO;

namespace GoatClient.Core.Constants;

/// <summary>
/// Central definition of the GOAT CLIENT data directory (%APPDATA%\GoatClient).
/// Only directories that are actually used in Phase 1 are created.
/// </summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GoatClient");

    /// <summary>Log files (goatclient-yyyy-MM-dd.log).</summary>
    public static string Logs => Path.Combine(Root, "logs");

    /// <summary>Managed Java runtimes (runtime\java-21, runtime\java-17, ...).</summary>
    public static string Runtime => Path.Combine(Root, "runtime");

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string ProfilesFile => Path.Combine(Root, "profiles.json");

    /// <summary>Default game directory for new profiles.</summary>
    public static string DefaultGameDirectory => Root;

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Runtime);
    }
}
