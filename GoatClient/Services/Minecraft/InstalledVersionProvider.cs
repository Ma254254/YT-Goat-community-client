using System.IO;
using System.Text.Json.Nodes;
using GoatClient.Models;

namespace GoatClient.Services.Minecraft;

/// <summary>Versions installed on this PC. Keeps installed versions usable without internet.</summary>
public sealed class InstalledVersionProvider : IMinecraftVersionProvider
{
    public string Name => "Installed on this PC";

    public bool IsDevelopmentData => false;

    public Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(CancellationToken cancellationToken)
    {
        var result = new List<MinecraftVersionInfo>();
        foreach (var id in LocalVersionStore.EnumerateInstalledIds())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var type = MinecraftVersionType.Release;
            DateTimeOffset? released = null;
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(LocalVersionStore.GetJsonPath(id)));
                type = node?["type"]?.GetValue<string>() == "snapshot" ? MinecraftVersionType.Snapshot : MinecraftVersionType.Release;
                released = DateTimeOffset.TryParse(node?["releaseTime"]?.GetValue<string>(), out var t) ? t : null;
            }
            catch (Exception)
            {
                // Unreadable JSON: still listed, "Repair" will re-download it.
            }

            result.Add(new MinecraftVersionInfo { Id = id, Type = type, ReleaseTime = released, Source = Name });
        }

        return Task.FromResult<IReadOnlyList<MinecraftVersionInfo>>(result);
    }
}
