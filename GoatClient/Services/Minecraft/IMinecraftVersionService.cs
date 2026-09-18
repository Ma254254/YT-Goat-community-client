using GoatClient.Models;

namespace GoatClient.Services.Minecraft;

public interface IMinecraftVersionService
{
    IReadOnlyList<MinecraftVersionInfo> Versions { get; }

    /// <summary>False only if neither the manifest nor its cache could be loaded.</summary>
    bool IsManifestAvailable { get; }

    string SourceDescription { get; }

    event EventHandler? VersionsChanged;

    Task LoadAsync(CancellationToken cancellationToken = default);

    MinecraftVersionInfo? Find(string? id);

    IReadOnlyList<MinecraftVersionInfo> Filter(string? query, bool includeSnapshots = false);

    /// <summary>Re-reads install state and Java requirement of a version from disk.</summary>
    void RefreshLocalState(string versionId);
}
