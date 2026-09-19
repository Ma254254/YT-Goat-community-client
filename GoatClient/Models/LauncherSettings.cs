using GoatClient.Core.Constants;

namespace GoatClient.Models;

/// <summary>How PLAY starts Minecraft.</summary>
public enum LaunchMode
{
    /// <summary>
    /// Default. GOAT CLIENT creates its profile in the official Minecraft Launcher and opens it.
    /// Sign-in, downloads and Java are handled by the official launcher – no Azure app needed.
    /// </summary>
    OfficialLauncher,

    /// <summary>GOAT CLIENT installs and starts Minecraft itself (needs an approved Azure client ID).</summary>
    Direct,
}

/// <summary>Root object of config\settings.json.</summary>
public sealed class LauncherSettings
{
    public const string FallbackVersion = "1.21.11";

    public int SchemaVersion { get; set; } = 3;

    // LAUNCHER
    public LaunchMode LaunchMode { get; set; } = LaunchMode.OfficialLauncher;

    public bool LaunchWithWindows { get; set; }

    public bool Notifications { get; set; } = true;

    public bool ConfirmBeforeClosing { get; set; }

    /// <summary>Only used in <see cref="LaunchMode.Direct"/>. Public Azure client ID, not a secret.</summary>
    public string MicrosoftClientId { get; set; } = string.Empty;

    // MINECRAFT
    /// <summary>Root folder for new profile game directories.</summary>
    public string MinecraftDirectory { get; set; } = AppPaths.DefaultGameDirectory;

    public string DefaultVersion { get; set; } = FallbackVersion;

    public Guid? DefaultProfileId { get; set; }

    public int DefaultRamMb { get; set; } = 4096;

    public string DefaultJvmArguments { get; set; } = string.Empty;

    public bool ShowSnapshots { get; set; }

    // APPEARANCE
    public bool EnablePageTransitions { get; set; } = true;

    public LauncherSettings Clone() => (LauncherSettings)MemberwiseClone();
}
