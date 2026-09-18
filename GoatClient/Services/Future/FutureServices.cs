using GoatClient.Models;

// ---------------------------------------------------------------------------------------------
// Contracts for future phases. They are intentionally NOT implemented in Phase 1 and are not
// registered anywhere – no fake implementations exist. The UI states "Coming in Phase 2" instead.
// ---------------------------------------------------------------------------------------------

namespace GoatClient.Services.Future;

/// <summary>Result of a Microsoft / Minecraft sign-in (Phase 2). Tokens are never persisted in plain text.</summary>
public sealed record MinecraftAccount(string Username, Guid Uuid);

/// <summary>Microsoft OAuth (device code / browser flow) – Phase 2.</summary>
public interface IMicrosoftAuthService
{
    Task<bool> SignInAsync(CancellationToken cancellationToken);

    Task SignOutAsync(CancellationToken cancellationToken);
}

/// <summary>Xbox Live / XSTS / Minecraft services authentication – Phase 2.</summary>
public interface IMinecraftAuthService
{
    Task<MinecraftAccount?> AuthenticateAsync(CancellationToken cancellationToken);
}

public sealed record DownloadProgress(long BytesReceived, long? TotalBytes);

/// <summary>Verified downloads (hash check, retries) – later phase.</summary>
public interface IDownloadService
{
    Task DownloadAsync(Uri source, string destination, string? sha1, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>Installs versions, libraries, assets and the managed Java runtime – later phase.</summary>
public interface IMinecraftInstallationService
{
    Task InstallAsync(MinecraftVersionInfo version, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken);

    Task<bool> IsInstalledAsync(MinecraftVersionInfo version, CancellationToken cancellationToken);
}

/// <summary>Builds the command line and starts Minecraft – later phase.</summary>
public interface IMinecraftLauncherService
{
    Task LaunchAsync(LauncherProfile profile, CancellationToken cancellationToken);
}

/// <summary>Skin upload / preview via the Minecraft services API – Phase 2.</summary>
public interface ISkinService
{
    Task UploadSkinAsync(string pngPath, bool slimModel, CancellationToken cancellationToken);
}

/// <summary>Launcher self-update – later phase.</summary>
public interface IUpdateService
{
    Task<bool> CheckForUpdatesAsync(CancellationToken cancellationToken);
}
