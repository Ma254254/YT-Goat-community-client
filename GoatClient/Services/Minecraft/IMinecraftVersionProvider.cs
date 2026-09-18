using GoatClient.Models;

namespace GoatClient.Services.Minecraft;

/// <summary>
/// Source of Minecraft versions: the official Mojang manifest and locally installed versions.
/// </summary>
public interface IMinecraftVersionProvider
{
    string Name { get; }

    /// <summary>True if the data is bundled development data rather than live data.</summary>
    bool IsDevelopmentData { get; }

    Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(CancellationToken cancellationToken);
}
