namespace GoatClient.Models;

public enum MinecraftVersionType
{
    Release,
    Snapshot,
}

public enum VersionInstallState
{
    NotInstalled,
    Installed,
}

/// <summary>Describes a Minecraft version known to the launcher.</summary>
public sealed class MinecraftVersionInfo
{
    public required string Id { get; init; }

    public MinecraftVersionType Type { get; init; } = MinecraftVersionType.Release;

    /// <summary>Java major version Mojang requires for this Minecraft version.</summary>
    public int RequiredJavaMajor { get; init; }

    public VersionInstallState InstallState { get; init; } = VersionInstallState.NotInstalled;

    /// <summary>Name of the provider that supplied this entry (e.g. local development data).</summary>
    public required string Source { get; init; }

    public bool IsInstalled => InstallState == VersionInstallState.Installed;

    public string DisplayName => $"Minecraft {Id}";

    public string TypeText => Type == MinecraftVersionType.Release ? "Release" : "Snapshot";

    public string InstallStateText => IsInstalled ? "Installed" : "Not Installed";

    public override string ToString() => Id;
}
