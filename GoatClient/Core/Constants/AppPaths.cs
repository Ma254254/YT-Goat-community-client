using System.IO;

namespace GoatClient.Core.Constants;

/// <summary>
/// Central definition of the GOAT CLIENT data directory (%APPDATA%\GoatClient).
/// Directories are created only when they are actually needed.
/// </summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GoatClient");

    public static string Config => Path.Combine(Root, "config");

    public static string ProfilesDirectory => Path.Combine(Root, "profiles");

    public static string Logs => Path.Combine(Root, "logs");

    /// <summary>Managed Java runtimes (runtime\java-21, runtime\java-17, ...).</summary>
    public static string Runtime => Path.Combine(Root, "runtime");

    /// <summary>Default root for per-profile game directories.</summary>
    public static string Instances => Path.Combine(Root, "instances");

    public static string Versions => Path.Combine(Root, "versions");

    public static string Libraries => Path.Combine(Root, "libraries");

    public static string Assets => Path.Combine(Root, "assets");

    /// <summary>Default location for partial downloads.</summary>
    public static string Downloads => Path.Combine(Root, "downloads");

    public static string SettingsFile => Path.Combine(Config, "settings.json");

    public static string ProfilesFile => Path.Combine(ProfilesDirectory, "profiles.json");

    /// <summary>Non-secret account cache (username, UUID, skin URL). Tokens are never stored here.</summary>
    public static string AccountFile => Path.Combine(Config, "account.json");

    public static string VersionManifestCache => Path.Combine(Versions, "version_manifest_v2.json");

    /// <summary>Default root for game directories (Phase 1 used the data root itself).</summary>
    public static string DefaultGameDirectory => Instances;

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Config);
        Directory.CreateDirectory(ProfilesDirectory);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Runtime);
        MigrateLegacyFile(Path.Combine(Root, "settings.json"), SettingsFile);
        MigrateLegacyFile(Path.Combine(Root, "profiles.json"), ProfilesFile);
    }

    /// <summary>Phase 1 stored settings/profiles directly in the root folder.</summary>
    private static void MigrateLegacyFile(string legacyPath, string newPath)
    {
        if (File.Exists(legacyPath) && !File.Exists(newPath))
        {
            File.Move(legacyPath, newPath);
        }
    }

    /// <summary>Creates a file-system friendly folder name ("GOAT Survival" → "goat-survival").</summary>
    public static string ToSlug(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) && c < 128 ? c : '-')
            .ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "profile" : slug;
    }
}
