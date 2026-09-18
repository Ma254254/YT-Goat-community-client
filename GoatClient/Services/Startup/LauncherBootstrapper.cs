using GoatClient.Models;
using GoatClient.Services.Java;
using GoatClient.Services.Logging;
using GoatClient.Services.Minecraft;
using GoatClient.Services.Profiles;
using GoatClient.Services.Settings;
using GoatClient.Services.Status;

namespace GoatClient.Services.Startup;

/// <summary>
/// Startup sequence (steps 5–8; directories and logging are prepared by App before):
/// settings → profiles → Java system → Minecraft versions.
/// Every step is isolated: a non-critical failure is logged and reported, the launcher stays usable.
/// </summary>
public sealed class LauncherBootstrapper
{
    private readonly ISettingsService _settings;
    private readonly IProfileService _profiles;
    private readonly IJavaService _java;
    private readonly IMinecraftVersionService _versions;
    private readonly IStatusService _status;
    private readonly ILogger _logger;

    public LauncherBootstrapper(
        ISettingsService settings,
        IProfileService profiles,
        IJavaService java,
        IMinecraftVersionService versions,
        IStatusService status,
        ILogger logger)
    {
        _settings = settings;
        _profiles = profiles;
        _java = java;
        _versions = versions;
        _status = status;
        _logger = logger;
    }

    /// <summary>Returns user-facing warnings for steps that failed.</summary>
    public async Task<IReadOnlyList<string>> RunAsync(CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        _status.Set(LauncherStatus.Loading, "Loading settings…");
        await RunStepAsync("Settings could not be loaded. Defaults are used.", () => _settings.LoadAsync(cancellationToken), warnings);

        _status.Set(LauncherStatus.Loading, "Loading profiles…");
        await RunStepAsync("Profiles could not be loaded.", () => _profiles.LoadAsync(cancellationToken), warnings);

        _status.Set(LauncherStatus.DetectingJava, "Checking Java runtimes…");
        await RunStepAsync("The Java runtime folder could not be checked.", () => _java.ScanAsync(cancellationToken), warnings);

        _status.Set(LauncherStatus.Loading, "Loading Minecraft versions…");
        await RunStepAsync("Minecraft versions could not be loaded.", () => _versions.LoadAsync(cancellationToken), warnings);

        if (warnings.Count == 0)
        {
            _status.Set(LauncherStatus.Ready, "Ready");
            _logger.Info("Startup completed.");
        }
        else
        {
            _status.Set(LauncherStatus.Ready, "Ready (with warnings)");
            _logger.Warning($"Startup completed with {warnings.Count} warning(s).");
        }

        return warnings;
    }

    private async Task RunStepAsync(string userMessage, Func<Task> step, List<string> warnings)
    {
        try
        {
            await step();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(userMessage, ex);
            warnings.Add($"{userMessage} ({ex.Message})");
        }
    }
}
