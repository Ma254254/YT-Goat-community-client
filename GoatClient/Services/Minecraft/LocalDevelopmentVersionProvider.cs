using GoatClient.Models;
using GoatClient.Services.Java;

namespace GoatClient.Services.Minecraft;

/// <summary>
/// Local development data for Phase 1. No network access, nothing is installed.
/// Every entry is explicitly marked as "Not Installed".
/// </summary>
public sealed class LocalDevelopmentVersionProvider : IMinecraftVersionProvider
{
    private static readonly string[] VersionIds =
    [
        "1.21.11",
        "1.21.10",
        "1.21.8",
        "1.21.4",
        "1.21.1",
        "1.20.6",
        "1.20.4",
    ];

    public string Name => "Local development data";

    public bool IsDevelopmentData => true;

    public Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<MinecraftVersionInfo> versions = VersionIds
            .Select(id => new MinecraftVersionInfo
            {
                Id = id,
                Type = MinecraftVersionType.Release,
                RequiredJavaMajor = JavaRequirements.ForMinecraftVersion(id),
                InstallState = VersionInstallState.NotInstalled,
                Source = Name,
            })
            .ToList();

        return Task.FromResult(versions);
    }
}
