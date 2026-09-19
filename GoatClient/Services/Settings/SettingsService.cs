using System.IO;
using GoatClient.Core.Constants;
using GoatClient.Core.Helpers;
using GoatClient.Models;
using GoatClient.Services.Logging;
using GoatClient.Services.Status;
using GoatClient.Services.Platform;

namespace GoatClient.Services.Settings;

/// <summary>Persists launcher settings in %APPDATA%\GoatClient\settings.json.</summary>
public sealed class SettingsService : ISettingsService
{
    private readonly JsonFileStore _store;
    private readonly ISystemInfoService _systemInfo;
    private readonly IStatusService _status;
    private readonly ILogger _logger;

    public SettingsService(JsonFileStore store, ISystemInfoService systemInfo, IStatusService status, ILogger logger)
    {
        _store = store;
        _systemInfo = systemInfo;
        _status = status;
        _logger = logger;
    }

    public LauncherSettings Current { get; private set; } = new();

    public event EventHandler? SettingsChanged;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _store.LoadAsync<LauncherSettings>(AppPaths.SettingsFile, cancellationToken);
        var isNew = loaded is null;
        var settings = loaded ?? new LauncherSettings { DefaultRamMb = _systemInfo.RecommendedRamMb };

        Normalize(settings);
        Current = settings;

        if (isNew)
        {
            _logger.Info("No settings found – created defaults.");
            await _store.SaveAsync(AppPaths.SettingsFile, Current, cancellationToken);
        }
        else
        {
            _logger.Info("Settings loaded.");
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var copy = settings.Clone();
        Normalize(copy);

        using (_status.Begin(LauncherStatus.Saving, "Saving settings…"))
        {
            await _store.SaveAsync(AppPaths.SettingsFile, copy, cancellationToken);
        }

        Current = copy;
        _logger.Info("Settings saved.");
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task SaveCurrentAsync(CancellationToken cancellationToken = default)
        => _store.SaveAsync(AppPaths.SettingsFile, Current, cancellationToken);

    private void Normalize(LauncherSettings settings)
    {
        // Phase 1 used the data root as game directory – Phase 2 uses separate instances.
        if (string.IsNullOrWhiteSpace(settings.MinecraftDirectory)
            || !Path.IsPathFullyQualified(settings.MinecraftDirectory)
            || PathsEqual(settings.MinecraftDirectory, AppPaths.Root))
        {
            settings.MinecraftDirectory = AppPaths.DefaultGameDirectory;
        }

        settings.MicrosoftClientId = settings.MicrosoftClientId?.Trim() ?? string.Empty;
        settings.SchemaVersion = 3;

        if (string.IsNullOrWhiteSpace(settings.DefaultVersion))
        {
            settings.DefaultVersion = LauncherSettings.FallbackVersion;
        }

        settings.DefaultRamMb = _systemInfo.ClampRam(settings.DefaultRamMb);
        settings.DefaultJvmArguments ??= string.Empty;
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
