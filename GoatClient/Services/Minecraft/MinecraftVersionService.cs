using GoatClient.Models;
using GoatClient.Services.Logging;

namespace GoatClient.Services.Minecraft;

/// <summary>
/// Merges the official manifest with locally installed versions (manifest entries win),
/// sorted newest first by release time.
/// </summary>
public sealed class MinecraftVersionService : IMinecraftVersionService
{
    private readonly IReadOnlyList<IMinecraftVersionProvider> _providers;
    private readonly ILogger _logger;
    private IReadOnlyList<MinecraftVersionInfo> _versions = Array.Empty<MinecraftVersionInfo>();

    public MinecraftVersionService(IEnumerable<IMinecraftVersionProvider> providers, ILogger logger)
    {
        _providers = providers.ToList();
        _logger = logger;
    }

    public IReadOnlyList<MinecraftVersionInfo> Versions => _versions;

    public bool IsManifestAvailable { get; private set; }

    public string SourceDescription { get; private set; } = string.Empty;

    public event EventHandler? VersionsChanged;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var all = new List<MinecraftVersionInfo>();
        var sources = new List<string>();
        var errors = new List<Exception>();
        IsManifestAvailable = false;

        foreach (var provider in _providers)
        {
            try
            {
                var versions = await provider.GetVersionsAsync(cancellationToken);
                all.AddRange(versions);
                if (versions.Count > 0)
                {
                    sources.Add(versions[0].Source);
                }

                if (provider is MojangVersionManifestProvider)
                {
                    IsManifestAvailable = true;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error($"Version provider '{provider.Name}' failed.", ex);
                errors.Add(ex);
            }
        }

        _versions = all
            .GroupBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(v => v.Url is null).First())
            .OrderByDescending(v => v.ReleaseTime ?? DateTimeOffset.MinValue)
            .ToList();

        foreach (var version in _versions)
        {
            ApplyLocalState(version);
        }

        SourceDescription = string.Join(" · ", sources.Distinct());
        _logger.Info($"{_versions.Count} Minecraft versions available ({SourceDescription}).");
        VersionsChanged?.Invoke(this, EventArgs.Empty);

        if (!IsManifestAvailable && errors.Count > 0)
        {
            throw new InvalidOperationException(
                "The official Minecraft version list could not be loaded. Check your internet connection. Installed versions remain available.",
                errors[0]);
        }
    }

    public MinecraftVersionInfo? Find(string? id)
        => string.IsNullOrWhiteSpace(id)
            ? null
            : _versions.FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<MinecraftVersionInfo> Filter(string? query, bool includeSnapshots = false)
    {
        var term = query?.Trim();
        return _versions
            .Where(v => v.Type == MinecraftVersionType.Release || (includeSnapshots && v.Type == MinecraftVersionType.Snapshot) || v.IsInstalled)
            .Where(v => string.IsNullOrEmpty(term) || v.Id.Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public void RefreshLocalState(string versionId)
    {
        if (Find(versionId) is { } version)
        {
            ApplyLocalState(version);
            VersionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static void ApplyLocalState(MinecraftVersionInfo version)
    {
        version.InstallState = LocalVersionStore.IsInstalled(version.Id) ? VersionInstallState.Installed : VersionInstallState.NotInstalled;
        version.RequiredJavaMajor = LocalVersionStore.TryReadJavaMajor(version.Id);
    }
}
