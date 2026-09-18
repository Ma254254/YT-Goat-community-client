using GoatClient.Models;

namespace GoatClient.Services.Minecraft;

public interface IMinecraftVersionService
{
    IReadOnlyList<MinecraftVersionInfo> Versions { get; }

    /// <summary>True while the versions come from bundled development data.</summary>
    bool UsesDevelopmentData { get; }

    string SourceDescription { get; }

    event EventHandler? VersionsChanged;

    Task LoadAsync(CancellationToken cancellationToken = default);

    MinecraftVersionInfo? Find(string? id);

    IReadOnlyList<MinecraftVersionInfo> Filter(string? query, bool includeSnapshots = true);
}
