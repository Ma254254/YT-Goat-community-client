using GoatClient.Core.Constants;

namespace GoatClient.Models;

/// <summary>Root object of settings.json.</summary>
public sealed class LauncherSettings
{
    public const string FallbackVersion = "1.21.11";

    public int SchemaVersion { get; set; } = 1;

    // GENERAL
    public bool LaunchWithWindows { get; set; }

    public bool Notifications { get; set; } = true;

    public bool ConfirmBeforeClosing { get; set; }

    // MINECRAFT
    public string MinecraftDirectory { get; set; } = AppPaths.DefaultGameDirectory;

    public string DefaultVersion { get; set; } = FallbackVersion;

    /// <summary>Profile selected on startup. Null = last selected profile.</summary>
    public Guid? DefaultProfileId { get; set; }

    public int DefaultRamMb { get; set; } = 4096;

    public string DefaultJvmArguments { get; set; } = string.Empty;

    // APPEARANCE
    public bool EnablePageTransitions { get; set; } = true;

    public bool ShowHomeLogo { get; set; } = true;

    public LauncherSettings Clone() => (LauncherSettings)MemberwiseClone();
}
