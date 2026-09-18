namespace GoatClient.Models;

/// <summary>Which Java runtime a profile should use once launching is implemented.</summary>
public enum JavaRuntimePreference
{
    /// <summary>GOAT CLIENT picks the runtime required by the selected Minecraft version.</summary>
    Automatic,
    Java17,
    Java21,
}

/// <summary>A persisted launcher profile (stored in profiles.json).</summary>
public sealed class LauncherProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Default";

    public string MinecraftVersion { get; set; } = string.Empty;

    public int RamMb { get; set; } = 4096;

    public JavaRuntimePreference JavaPreference { get; set; } = JavaRuntimePreference.Automatic;

    public string GameDirectory { get; set; } = string.Empty;

    public string JvmArguments { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset? LastModified { get; set; }

    public LauncherProfile Clone() => (LauncherProfile)MemberwiseClone();
}

/// <summary>Root object of profiles.json.</summary>
public sealed class ProfileStoreData
{
    public int SchemaVersion { get; set; } = 1;

    public Guid? SelectedProfileId { get; set; }

    public List<LauncherProfile> Profiles { get; set; } = new();
}
