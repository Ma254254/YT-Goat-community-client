using GoatClient.Models;
using GoatClient.Services.Logging;

namespace GoatClient.Services.Minecraft;

/// <summary>Aggregates all version providers, de-duplicates and sorts versions (newest first).</summary>
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

    public bool UsesDevelopmentData => _providers.Any(p => p.IsDevelopmentData);

    public string SourceDescription => string.Join(", ", _providers.Select(p => p.Name));

    public event EventHandler? VersionsChanged;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var all = new List<MinecraftVersionInfo>();
        foreach (var provider in _providers)
        {
            try
            {
                all.AddRange(await provider.GetVersionsAsync(cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error($"Version provider '{provider.Name}' failed.", ex);
            }
        }

        _versions = all
            .GroupBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(v => ParseVersion(v.Id))
            .ToList();

        _logger.Info($"{_versions.Count} Minecraft versions loaded from: {SourceDescription}.");
        VersionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public MinecraftVersionInfo? Find(string? id)
        => string.IsNullOrWhiteSpace(id)
            ? null
            : _versions.FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<MinecraftVersionInfo> Filter(string? query, bool includeSnapshots = true)
    {
        var term = query?.Trim();
        return _versions
            .Where(v => includeSnapshots || v.Type == MinecraftVersionType.Release)
            .Where(v => string.IsNullOrEmpty(term) || v.Id.Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static Version ParseVersion(string id)
        => Version.TryParse(id, out var version) ? version : new Version(0, 0);
}
