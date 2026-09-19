using GoatClient.Models;

namespace GoatClient.Services.Launch;

/// <summary>
/// Integration with the official Minecraft Launcher (the same approach the Fabric installer uses):
/// GOAT CLIENT writes its own profile into launcher_profiles.json and opens the launcher.
/// Account files of the official launcher are never read or modified.
/// </summary>
public interface IOfficialLauncherService
{
    /// <summary>%APPDATA%\.minecraft</summary>
    string MinecraftDirectory { get; }

    /// <summary>True if the official launcher was found (classic installer or Microsoft Store / Xbox app).</summary>
    bool IsInstalled { get; }

    /// <summary>Human-readable description of the detected launcher.</summary>
    string StatusText { get; }

    /// <summary>True if the official launcher is currently running.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Creates or updates the GOAT CLIENT profile for <paramref name="profile"/> and removes
    /// GOAT CLIENT profiles whose launcher profile no longer exists. Other profiles stay untouched.
    /// Returns the profile name shown in the official launcher.
    /// </summary>
    Task<string> SyncProfileAsync(LauncherProfile profile, IReadOnlyCollection<Guid> existingProfileIds, CancellationToken cancellationToken);

    void OpenLauncher();
}

public sealed class OfficialLauncherNotFoundException : Exception
{
    public OfficialLauncherNotFoundException()
        : base("The official Minecraft Launcher was not found. Install it from minecraft.net (or the Microsoft Store / Xbox app), start it once, then press PLAY again.")
    {
    }
}
