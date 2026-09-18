using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using GoatClient.Core.Constants;
using GoatClient.Models;
using GoatClient.Services.Downloads;
using GoatClient.Services.Logging;

namespace GoatClient.Services.Minecraft;

/// <summary>
/// Installs Minecraft from official Mojang sources only
/// (piston-meta / piston-data / libraries.minecraft.net / resources.download.minecraft.net).
/// </summary>
public sealed class MinecraftInstallationService : IMinecraftInstallationService
{
    private const string ResourcesBaseUrl = "https://resources.download.minecraft.net";

    private readonly IMinecraftVersionService _versions;
    private readonly IDownloadService _downloads;
    private readonly ILogger _logger;

    public MinecraftInstallationService(IMinecraftVersionService versions, IDownloadService downloads, ILogger logger)
    {
        _versions = versions;
        _downloads = downloads;
        _logger = logger;
    }

    public bool IsInstalled(string versionId) => LocalVersionStore.IsInstalled(versionId);

    public async Task<VersionManifest> InstallAsync(string versionId, bool repair, IProgress<TransferProgress>? progress, CancellationToken cancellationToken)
    {
        _logger.Info($"{(repair ? "Repairing" : "Installing")} Minecraft {versionId}…");
        progress?.Report(TransferProgress.Indeterminate("Downloading version information…"));

        // 1. Version JSON (hash-verified against the manifest).
        var version = await EnsureVersionJsonAsync(versionId, repair, cancellationToken);
        LocalVersionStore.ClearInstalled(version.Id);

        // 2. Asset index.
        JsonNode? assetIndex = null;
        if (version.AssetIndex is { } indexRef)
        {
            var indexPath = GetAssetIndexPath(indexRef.Id);
            await _downloads.DownloadManyAsync(new[] { new DownloadRequest(indexRef.Url, indexPath, indexRef.Sha1, indexRef.Size) }, "Downloading asset index", repair, null, cancellationToken);
            assetIndex = JsonNode.Parse(await File.ReadAllTextAsync(indexPath, cancellationToken));
        }

        // 3. Client, libraries, natives and logging configuration.
        var gameFiles = new List<DownloadRequest>();
        if (version.Client is { } client)
        {
            gameFiles.Add(new DownloadRequest(client.Url, LocalVersionStore.GetJarPath(version.Id), client.Sha1, client.Size));
        }
        else
        {
            throw new InvalidOperationException($"Minecraft {version.Id} has no client download.");
        }

        foreach (var library in version.Libraries)
        {
            foreach (var download in new[] { library.Artifact, library.Native })
            {
                if (download?.Path is { } relative)
                {
                    gameFiles.Add(new DownloadRequest(download.Url, GetLibraryPath(relative), download.Sha1, download.Size));
                }
            }
        }

        if (version.Logging is { } logging)
        {
            gameFiles.Add(new DownloadRequest(logging.Url, GetLoggingConfigPath(logging.FileId), logging.Sha1, logging.Size));
        }

        await _downloads.DownloadManyAsync(gameFiles, "Downloading client & libraries", repair, progress, cancellationToken);

        // 4. Assets.
        if (assetIndex?["objects"] is JsonObject objects)
        {
            var assets = new List<DownloadRequest>();
            foreach (var (_, entry) in objects)
            {
                var hash = entry?["hash"]?.GetValue<string>();
                if (hash is null || hash.Length < 2)
                {
                    continue;
                }

                var sub = hash[..2];
                assets.Add(new DownloadRequest($"{ResourcesBaseUrl}/{sub}/{hash}", Path.Combine(AppPaths.Assets, "objects", sub, hash), hash, entry?["size"]?.GetValue<long>()));
            }

            await _downloads.DownloadManyAsync(assets, "Downloading assets", repair, progress, cancellationToken);

            if (assetIndex["virtual"]?.GetValue<bool>() == true)
            {
                progress?.Report(TransferProgress.Indeterminate("Preparing legacy assets…"));
                await Task.Run(() => CopyAssetsTo(objects, GetVirtualAssetsDirectory(version.AssetIndex!.Id)), cancellationToken);
            }
        }

        LocalVersionStore.MarkInstalled(version.Id);
        _versions.RefreshLocalState(version.Id);
        _logger.Info($"Minecraft {version.Id} {(repair ? "repaired" : "installed")}.");
        return version;
    }

    public async Task<VersionManifest> LoadInstalledAsync(string versionId, CancellationToken cancellationToken)
    {
        var path = LocalVersionStore.GetJsonPath(versionId);
        if (!LocalVersionStore.IsInstalled(versionId) || !File.Exists(path))
        {
            throw new InstallationIncompleteException($"Minecraft {versionId} is not installed completely.");
        }

        return VersionManifest.Parse(await File.ReadAllTextAsync(path, cancellationToken));
    }

    public async Task<JsonNode?> LoadAssetIndexAsync(VersionManifest version, CancellationToken cancellationToken)
    {
        if (version.AssetIndex is null)
        {
            return null;
        }

        var path = GetAssetIndexPath(version.AssetIndex.Id);
        return File.Exists(path) ? JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken)) : null;
    }

    public static string GetLibraryPath(string relative)
        => Path.Combine(AppPaths.Libraries, relative.Replace('/', Path.DirectorySeparatorChar));

    public static string GetAssetIndexPath(string indexId) => Path.Combine(AppPaths.Assets, "indexes", indexId + ".json");

    public static string GetVirtualAssetsDirectory(string indexId) => Path.Combine(AppPaths.Assets, "virtual", indexId);

    public static string GetLoggingConfigPath(string fileId) => Path.Combine(AppPaths.Assets, "log_configs", fileId);

    /// <summary>Copies hashed objects to their named paths (legacy "virtual" / "map_to_resources" layouts).</summary>
    public static void CopyAssetsTo(JsonObject objects, string targetRoot)
    {
        foreach (var (name, entry) in objects)
        {
            var hash = entry?["hash"]?.GetValue<string>();
            if (hash is null || hash.Length < 2)
            {
                continue;
            }

            var source = Path.Combine(AppPaths.Assets, "objects", hash[..2], hash);
            var target = Path.GetFullPath(Path.Combine(targetRoot, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(Path.GetFullPath(targetRoot), StringComparison.OrdinalIgnoreCase) || !File.Exists(source))
            {
                continue;
            }

            if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(source).Length)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: true);
            }
        }
    }

    private async Task<VersionManifest> EnsureVersionJsonAsync(string versionId, bool repair, CancellationToken cancellationToken)
    {
        var path = LocalVersionStore.GetJsonPath(versionId);
        var info = _versions.Find(versionId);

        if (info?.Url is { } url)
        {
            await _downloads.DownloadManyAsync(new[] { new DownloadRequest(url, path, info.Sha1, null) }, "Downloading version information", verifyExisting: true, null, cancellationToken);
        }
        else if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Minecraft {versionId} is not in the official version list and not installed. Check your internet connection.");
        }
        else if (repair)
        {
            _logger.Warning($"Minecraft {versionId}: version list unavailable – the local version JSON is used without re-download.");
        }

        return VersionManifest.Parse(await File.ReadAllTextAsync(path, cancellationToken));
    }
}
