using GoatClient.Models;

namespace GoatClient.Services.Minecraft;

public interface IMinecraftInstallationService
{
    bool IsInstalled(string versionId);

    /// <summary>
    /// Installs a version: version JSON, client, libraries, natives, asset index, assets, logging config.
    /// With <paramref name="repair"/> every existing file is hash-verified and re-downloaded if corrupt.
    /// </summary>
    Task<VersionManifest> InstallAsync(string versionId, bool repair, IProgress<TransferProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Loads the installed version JSON. Throws <see cref="InstallationIncompleteException"/> if missing.</summary>
    Task<VersionManifest> LoadInstalledAsync(string versionId, CancellationToken cancellationToken);

    /// <summary>Asset index of an installed version (needed for legacy asset layouts at launch).</summary>
    Task<System.Text.Json.Nodes.JsonNode?> LoadAssetIndexAsync(VersionManifest version, CancellationToken cancellationToken);
}

public sealed class InstallationIncompleteException : Exception
{
    public InstallationIncompleteException(string message)
        : base(message)
    {
    }
}
