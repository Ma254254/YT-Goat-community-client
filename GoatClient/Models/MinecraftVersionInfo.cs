using GoatClient.Core;

namespace GoatClient.Models;

public enum MinecraftVersionType
{
    Release,
    Snapshot,
    OldBeta,
    OldAlpha,
}

public enum VersionInstallState
{
    NotInstalled,
    Installed,
}

/// <summary>A Minecraft version from the official manifest (or a locally installed version).</summary>
public sealed class MinecraftVersionInfo : ObservableObject
{
    private VersionInstallState _installState;
    private int? _requiredJavaMajor;

    public required string Id { get; init; }

    public MinecraftVersionType Type { get; init; } = MinecraftVersionType.Release;

    /// <summary>URL of the official version JSON (null for local-only versions).</summary>
    public string? Url { get; init; }

    /// <summary>SHA-1 of the version JSON as published in the manifest.</summary>
    public string? Sha1 { get; init; }

    public DateTimeOffset? ReleaseTime { get; init; }

    public required string Source { get; init; }

    /// <summary>
    /// Java major version from the official version JSON ("javaVersion.majorVersion").
    /// Null until the version JSON has been downloaded – never guessed.
    /// </summary>
    public int? RequiredJavaMajor
    {
        get => _requiredJavaMajor;
        set
        {
            if (SetProperty(ref _requiredJavaMajor, value))
            {
                OnPropertyChanged(nameof(JavaText));
            }
        }
    }

    public VersionInstallState InstallState
    {
        get => _installState;
        set
        {
            if (SetProperty(ref _installState, value))
            {
                OnPropertyChanged(nameof(IsInstalled));
                OnPropertyChanged(nameof(InstallStateText));
            }
        }
    }

    public bool IsInstalled => InstallState == VersionInstallState.Installed;

    public string DisplayName => $"Minecraft {Id}";

    public string TypeText => Type switch
    {
        MinecraftVersionType.Release => "Release",
        MinecraftVersionType.Snapshot => "Snapshot",
        MinecraftVersionType.OldBeta => "Beta",
        _ => "Alpha",
    };

    public string JavaText => RequiredJavaMajor is { } major ? $"Java {major}" : "Java: determined on install";

    public string InstallStateText => IsInstalled ? "Installed" : "Not Installed";

    public string ReleaseDateText => ReleaseTime?.ToLocalTime().ToString("yyyy-MM-dd") ?? string.Empty;

    public override string ToString() => Id;
}
