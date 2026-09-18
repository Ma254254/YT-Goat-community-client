using GoatClient.Models;
using GoatClient.Services.Auth;
using GoatClient.Services.Java;
using GoatClient.Services.Logging;
using GoatClient.Services.Minecraft;
using GoatClient.Services.Profiles;
using GoatClient.Services.Settings;
using GoatClient.Services.Status;

namespace GoatClient.Services.Startup;

/// <summary>
/// Startup sequence (directories and logging are prepared by App before):
/// settings → profiles → Java runtimes → Minecraft versions → Microsoft sign-in.
/// Every step is isolated: a non-critical failure is logged and reported, the launcher stays usable.
/// </summary>
public sealed class LauncherBootstrapper
{
    private readonly ISettingsService _settings;
    private readonly IProfileService _profiles;
    private readonly IJavaService _java;
    private readonly IMinecraftVersionService _versions;
    private readonly IStatusService _status;
    private readonly IAuthService _auth;
    private readonly ILogger _logger;

    public LauncherBootstrapper(
        ISettingsService settings,
        IProfileService profiles,
        IJavaService java,
        IMinecraftVersionService versions,
        IStatusService status,
        IAuthService auth,
        ILogger logger)
    {
        _auth = auth;
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
        await RunStepAsync("The official Minecraft version list could not be loaded.", () => _versions.LoadAsync(cancellationToken), warnings);

        _status.Set(LauncherStatus.Loading, "Restoring Microsoft sign-in…");
        await RunStepAsync("Your Microsoft sign-in could not be restored.", async () =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                await _auth.InitializeAsync(timeout.Token);
            }
            catch (AuthException ex) when (ex.RequiresSignIn)
            {
                _logger.Info($"Sign-in required: {ex.Message}");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.Warning("Sign-in refresh timed out – it will be retried before launching.");
            }
        }, warnings);

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
