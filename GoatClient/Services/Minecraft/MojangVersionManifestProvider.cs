using System.IO;
using System.Text.Json.Nodes;
using GoatClient.Core.Constants;
using GoatClient.Models;
using GoatClient.Services.Downloads;
using GoatClient.Services.Logging;

namespace GoatClient.Services.Minecraft;

/// <summary>
/// Official Mojang version manifest (piston-meta.mojang.com).
/// The last successful response is cached so the launcher still works offline.
/// </summary>
public sealed class MojangVersionManifestProvider : IMinecraftVersionProvider
{
    public const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    private readonly IDownloadService _downloads;
    private readonly ILogger _logger;

    public MojangVersionManifestProvider(IDownloadService downloads, ILogger logger)
    {
        _downloads = downloads;
        _logger = logger;
    }

    public string Name => "Official Mojang version manifest";

    public bool IsDevelopmentData => false;

    public bool LastLoadUsedCache { get; private set; }

    public async Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(CancellationToken cancellationToken)
    {
        string json;
        try
        {
            json = await _downloads.GetStringAsync(ManifestUrl, cancellationToken);
            Directory.CreateDirectory(AppPaths.Versions);
            await File.WriteAllTextAsync(AppPaths.VersionManifestCache, json, cancellationToken);
            LastLoadUsedCache = false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && File.Exists(AppPaths.VersionManifestCache))
        {
            _logger.Warning("Version manifest could not be downloaded – using the cached copy.", ex);
            json = await File.ReadAllTextAsync(AppPaths.VersionManifestCache, cancellationToken);
            LastLoadUsedCache = true;
        }

        var root = JsonNode.Parse(json) ?? throw new InvalidDataException("Empty version manifest.");
        var versions = new List<MinecraftVersionInfo>();
        foreach (var entry in root["versions"]?.AsArray() ?? new JsonArray())
        {
            var id = entry?["id"]?.GetValue<string>();
            var url = entry?["url"]?.GetValue<string>();
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(url))
            {
                continue;
            }

            versions.Add(new MinecraftVersionInfo
            {
                Id = id,
                Type = ParseType(entry?["type"]?.GetValue<string>()),
                Url = url,
                Sha1 = entry?["sha1"]?.GetValue<string>(),
                ReleaseTime = DateTimeOffset.TryParse(entry?["releaseTime"]?.GetValue<string>(), out var time) ? time : null,
                Source = LastLoadUsedCache ? Name + " (cached)" : Name,
            });
        }

        return versions;
    }

    private static MinecraftVersionType ParseType(string? type) => type switch
    {
        "release" => MinecraftVersionType.Release,
        "snapshot" => MinecraftVersionType.Snapshot,
        "old_beta" => MinecraftVersionType.OldBeta,
        _ => MinecraftVersionType.OldAlpha,
    };
}
