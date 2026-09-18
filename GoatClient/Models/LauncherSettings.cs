using GoatClient.Core.Constants;

namespace GoatClient.Models;

/// <summary>Root object of config\settings.json.</summary>
public sealed class LauncherSettings
{
    public const string FallbackVersion = "1.21.11";

    public int SchemaVersion { get; set; } = 2;

    // LAUNCHER
    public bool LaunchWithWindows { get; set; }

    public bool Notifications { get; set; } = true;

    public bool ConfirmBeforeClosing { get; set; }

    /// <summary>
    /// Public client ID of the Azure app registration used for Microsoft sign-in.
    /// This is not a secret (public client, device code flow) – but it must be registered by the owner.
    /// </summary>
    public string MicrosoftClientId { get; set; } = string.Empty;

    // MINECRAFT
    /// <summary>Root folder for new profile game directories.</summary>
    public string MinecraftDirectory { get; set; } = AppPaths.DefaultGameDirectory;

    public string DefaultVersion { get; set; } = FallbackVersion;

    public Guid? DefaultProfileId { get; set; }

    public int DefaultRamMb { get; set; } = 4096;

    public string DefaultJvmArguments { get; set; } = string.Empty;

    public bool ShowSnapshots { get; set; }

    // JAVA
    public bool InstallMissingRuntimeAutomatically { get; set; } = true;

    // DOWNLOADS
    public string DownloadDirectory { get; set; } = AppPaths.Downloads;

    public int MaxParallelDownloads { get; set; } = 8;

    public int DownloadRetryCount { get; set; } = 3;

    // APPEARANCE
    public bool EnablePageTransitions { get; set; } = true;

    public bool ShowHomeLogo { get; set; } = true;

    public LauncherSettings Clone() => (LauncherSettings)MemberwiseClone();
}
