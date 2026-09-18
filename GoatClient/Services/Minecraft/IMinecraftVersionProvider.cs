using GoatClient.Models;

namespace GoatClient.Services.Minecraft;

/// <summary>
/// Source of Minecraft versions. Phase 1 ships only <see cref="LocalDevelopmentVersionProvider"/>.
/// A later phase adds a provider for Mojang's official version manifest.
/// </summary>
public interface IMinecraftVersionProvider
{
    string Name { get; }

    /// <summary>True if the data is bundled development data rather than live data.</summary>
    bool IsDevelopmentData { get; }

    Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(CancellationToken cancellationToken);
}
