using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;
using GoatClient.Core;
using GoatClient.Core.Commands;
using GoatClient.Models;
using GoatClient.Services.Auth;
using GoatClient.Services.Dialogs;
using GoatClient.Services.Downloads;
using GoatClient.Services.Java;
using GoatClient.Services.Launch;
using GoatClient.Services.Logging;
using GoatClient.Services.Minecraft;
using GoatClient.Services.Navigation;
using GoatClient.Services.Notifications;
using GoatClient.Services.Platform;
using GoatClient.Services.Profiles;
using GoatClient.Services.Settings;
using GoatClient.Services.Status;

namespace GoatClient.ViewModels;

/// <summary>
/// Shared install / repair / launch workflow for the selected profile.
/// Used by Home, Play and the sidebar so every page shows the same real state.
/// </summary>
public sealed class GameController : ObservableObject
{
    private const int MaxLogLines = 400;

    private readonly IProfileService _profiles;
    private readonly IMinecraftVersionService _versions;
    private readonly IMinecraftInstallationService _installation;
    private readonly IJavaService _java;
    private readonly IMinecraftLauncherService _launcher;
    private readonly IOfficialLauncherService _official;
    private readonly IAuthService _auth;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly IStatusService _status;
    private readonly INavigationService _navigation;
    private readonly IShellService _shell;
    private readonly ILogger _logger;
    private readonly Dispatcher _dispatcher;
    private readonly CancellationToken _appLifetime;

    private CancellationTokenSource? _operation;
    private bool _isBusy;
    private bool _isIndeterminate;
    private double _progress;
    private string _stageText = string.Empty;
    private string _detailText = string.Empty;
    private string _speedText = string.Empty;

    public GameController(
        IProfileService profiles,
        IMinecraftVersionService versions,
        IMinecraftInstallationService installation,
        IJavaService java,
        IMinecraftLauncherService launcher,
        IOfficialLauncherService official,
        IAuthService auth,
        ISettingsService settings,
        IDialogService dialogs,
        INotificationService notifications,
        IStatusService status,
        INavigationService navigation,
        IShellService shell,
        ILogger logger,
        Dispatcher dispatcher,
        CancellationToken appLifetime)
    {
        _profiles = profiles;
        _versions = versions;
        _installation = installation;
        _java = java;
        _launcher = launcher;
        _official = official;
        _auth = auth;
        _settings = settings;
        _dialogs = dialogs;
        _notifications = notifications;
        _status = status;
        _navigation = navigation;
        _shell = shell;
        _logger = logger;
        _dispatcher = dispatcher;
        _appLifetime = appLifetime;

        PrimaryCommand = new AsyncRelayCommand(PrimaryAsync, () => !IsBusy && !_launcher.IsRunning && _profiles.SelectedProfile is not null);
        InstallCommand = new AsyncRelayCommand(() => RunInstallAsync(repair: false), () => !IsBusy && !_launcher.IsRunning);
        RepairCommand = new AsyncRelayCommand(() => RunInstallAsync(repair: true), () => IsDirectMode && !IsBusy && !_launcher.IsRunning && _profiles.SelectedProfile is not null);
        CancelCommand = new RelayCommand(() => _operation?.Cancel(), () => IsBusy);
        OpenLaunchLogCommand = new RelayCommand(OpenLaunchLog);

        _profiles.ProfilesChanged += (_, _) => RefreshState();
        _versions.VersionsChanged += (_, _) => RefreshState();
        _java.RuntimesChanged += (_, _) => RefreshState();
        _settings.SettingsChanged += (_, _) => RefreshState();
        _launcher.PropertyChanged += OnLauncherChanged;
        _launcher.LogLine += (_, line) => _dispatcher.InvokeAsync(() => AppendLog(line));
        _launcher.ProcessExited += (_, code) => _dispatcher.InvokeAsync(() => OnProcessExited(code));
    }

    public ObservableCollection<string> LaunchLog { get; } = new();

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(PrimaryText));
                RelayCommand.Refresh();
            }
        }
    }

    public bool IsIndeterminate { get => _isIndeterminate; private set => SetProperty(ref _isIndeterminate, value); }

    /// <summary>0–100.</summary>
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }

    public string StageText { get => _stageText; private set => SetProperty(ref _stageText, value); }

    public string DetailText { get => _detailText; private set => SetProperty(ref _detailText, value); }

    public string SpeedText { get => _speedText; private set => SetProperty(ref _speedText, value); }

    public MinecraftProcessState ProcessState => _launcher.ProcessState;

    public string ProcessStateText => _launcher.ProcessState switch
    {
        MinecraftProcessState.Starting => "Starting",
        MinecraftProcessState.Running => "Running",
        MinecraftProcessState.Exited => "Exited",
        MinecraftProcessState.Crashed => $"Crashed (exit code {_launcher.LastExitCode})",
        _ => "Not Running",
    };

    /// <summary>True when GOAT CLIENT installs and starts Minecraft itself.</summary>
    public bool IsDirectMode => _settings.Current.LaunchMode == LaunchMode.Direct;

    public bool IsSelectedInstalled
        => _profiles.SelectedProfile is { } p && _installation.IsInstalled(p.MinecraftVersion);

    public string InstallStateText => !IsDirectMode
        ? "Via Minecraft Launcher"
        : IsSelectedInstalled ? "Installed" : "Not Installed";

    /// <summary>INSTALL / PLAY / RUNNING – derived from the real installation and process state.</summary>
    public string PrimaryText
    {
        get
        {
            if (_launcher.IsRunning)
            {
                return "RUNNING";
            }

            if (IsBusy)
            {
                return "WORKING…";
            }

            return !IsDirectMode || IsSelectedInstalled ? "PLAY" : "INSTALL";
        }
    }

    public ICommand PrimaryCommand { get; }

    public ICommand InstallCommand { get; }

    public ICommand RepairCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand OpenLaunchLogCommand { get; }

    public void RefreshState()
    {
        OnPropertyChanged(nameof(IsDirectMode));
        OnPropertyChanged(nameof(IsSelectedInstalled));
        OnPropertyChanged(nameof(InstallStateText));
        OnPropertyChanged(nameof(PrimaryText));
        RelayCommand.Refresh();
    }

    private async Task PrimaryAsync()
    {
        var profile = _profiles.SelectedProfile;
        if (profile is null)
        {
            return;
        }

        if (!IsDirectMode)
        {
            await RunOfficialLauncherAsync(profile);
            return;
        }

        if (!_installation.IsInstalled(profile.MinecraftVersion))
        {
            await RunInstallAsync(repair: false);
            return;
        }

        await RunLaunchAsync(profile);
    }

    private async Task RunInstallAsync(bool repair)
    {
        var profile = _profiles.SelectedProfile;
        if (profile is null)
        {
            return;
        }

        await RunOperationAsync(repair ? $"Repairing Minecraft {profile.MinecraftVersion}" : $"Installing Minecraft {profile.MinecraftVersion}", async ct =>
        {
            var version = await _installation.InstallAsync(profile.MinecraftVersion, repair, CreateProgress(), ct);
            var requirement = _java.ResolveRequirement(profile.JavaPreference, version.JavaRequirement);
            await EnsureJavaAsync(requirement, verify: repair, ct);

            _notifications.Show(NotificationKind.Success,
                repair ? "Repair completed" : "Minecraft installation completed.",
                $"Minecraft {version.Id} is ready to play.");
        });
    }

    /// <summary>
    /// Default mode: create/update the GOAT CLIENT profile in the official Minecraft Launcher and open it.
    /// Sign-in, downloads and Java are handled by the official launcher.
    /// </summary>
    private async Task RunOfficialLauncherAsync(LauncherProfile profile)
    {
        await RunOperationAsync("Opening Minecraft Launcher", async ct =>
        {
            if (!_official.IsInstalled)
            {
                throw new OfficialLauncherNotFoundException();
            }

            SetStage("Updating GOAT CLIENT profile…");
            var wasRunning = _official.IsRunning;
            var ids = _profiles.Profiles.Select(p => p.Id).ToList();
            var name = await _official.SyncProfileAsync(profile, ids, ct);

            SetStage("Opening Minecraft Launcher…");
            _official.OpenLauncher();

            _notifications.Show(
                NotificationKind.Info,
                "Minecraft Launcher opened",
                wasRunning
                    ? $"Select \"{name}\" and press PLAY. The launcher was already open – if the profile is missing, close and reopen it."
                    : $"Select \"{name}\" and press PLAY. Sign-in happens in the official launcher.");
        });
    }

    private async Task RunLaunchAsync(LauncherProfile profile)
    {
        await RunOperationAsync($"Launching Minecraft {profile.MinecraftVersion}", async ct =>
        {
            // 1. Account
            SetStage("Checking account…");
            MinecraftSession session;
            try
            {
                session = await _auth.GetSessionAsync(ct);
            }
            catch (AuthException ex) when (ex.RequiresSignIn)
            {
                throw new LaunchFailedException(ex.Message, LaunchFix.SignIn, ex);
            }
            catch (AuthException ex)
            {
                throw new LaunchFailedException(ex.Message, LaunchFix.None, ex);
            }

            // 2. Installation
            SetStage("Preparing…");
            VersionManifest version;
            try
            {
                version = await _installation.LoadInstalledAsync(profile.MinecraftVersion, ct);
            }
            catch (InstallationIncompleteException ex)
            {
                throw new LaunchFailedException(ex.Message, LaunchFix.RepairInstallation, ex);
            }

            // 3. Java
            var requirement = _java.ResolveRequirement(profile.JavaPreference, version.JavaRequirement);
            if (requirement.MajorVersion < version.JavaRequirement.MajorVersion)
            {
                throw new LaunchFailedException(
                    $"Minecraft {version.Id} requires Java {version.JavaRequirement.MajorVersion}, but the profile is set to Java {requirement.MajorVersion}. Set the profile's Java runtime to Automatic.",
                    LaunchFix.None);
            }

            var runtime = await EnsureJavaAsync(requirement, verify: false, ct);

            // 4. Process
            SetStage("Launching Minecraft…");
            LaunchLog.Clear();
            await _launcher.LaunchAsync(profile, version, runtime, session, ct);
            await _profiles.MarkLaunchedAsync(profile.Id, ct);
            _notifications.Show(NotificationKind.Info, "Minecraft is starting...", $"{profile.Name} · Minecraft {version.Id}");
        });
    }

    private async Task<JavaRuntime> EnsureJavaAsync(JavaRequirement requirement, bool verify, CancellationToken ct)
    {
        var existing = _java.FindManagedRuntime(requirement.MajorVersion);
        if (existing is not null && !verify)
        {
            return existing;
        }

        var runtime = await _java.EnsureRuntimeAsync(requirement, verify, CreateProgress(), ct);
        if (existing is null)
        {
            _notifications.Show(NotificationKind.Success, $"Java {requirement.MajorVersion} installed successfully.", runtime.HomeDirectory);
        }

        return runtime;
    }

    private async Task RunOperationAsync(string title, Func<CancellationToken, Task> operation)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime);
        _operation = cts;
        Exception? failure = null;
        IsBusy = true;
        SetStage(title + "…");
        _status.Set(LauncherStatus.Loading, title + "…");
        try
        {
            await operation(cts.Token);
            _status.Set(LauncherStatus.Ready, "Ready");
        }
        catch (OperationCanceledException)
        {
            _status.Set(LauncherStatus.Ready, "Cancelled");
            if (!_appLifetime.IsCancellationRequested)
            {
                _notifications.Show(NotificationKind.Warning, "Cancelled", $"{title} was cancelled. Already verified files are kept.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"{title} failed.", ex);
            _status.Set(LauncherStatus.Error, "Action failed");
            failure = ex;
        }
        finally
        {
            IsBusy = false;
            IsIndeterminate = false;
            Progress = 0;
            StageText = string.Empty;
            DetailText = string.Empty;
            SpeedText = string.Empty;
            if (ReferenceEquals(_operation, cts))
            {
                _operation = null;
            }

            cts.Dispose();
            RefreshState();
        }

        // Shown after cleanup, so a follow-up action (e.g. Repair) can start its own operation.
        if (failure is not null)
        {
            await ShowFailureAsync(title, failure);
        }
    }

    private async Task ShowFailureAsync(string title, Exception ex)
    {
        var (reason, fix) = ex switch
        {
            LaunchFailedException launch => (launch.Message, launch.Fix),
            JavaRuntimeUnavailableException java => (java.Message, LaunchFix.InstallJava),
            DownloadFailedException download => ($"{download.Message}\nCheck your internet connection and try again.", LaunchFix.None),
            OfficialLauncherNotFoundException notFound => (notFound.Message, LaunchFix.None),
            System.Net.Http.HttpRequestException => ("A server could not be reached. Check your internet connection and try again.", LaunchFix.None),
            AuthException auth => (auth.Message, auth.RequiresSignIn ? LaunchFix.SignIn : LaunchFix.None),
            _ => (ex.Message, LaunchFix.None),
        };

        var header = title.StartsWith("Launching", StringComparison.Ordinal) || title.StartsWith("Opening", StringComparison.Ordinal)
            ? "Minecraft could not be started."
            : $"{title} failed.";
        var message = $"Reason:\n{reason}";

        switch (fix)
        {
            case LaunchFix.RepairInstallation:
                if (await _dialogs.ConfirmAsync(header, message, "Repair Installation", "Close"))
                {
                    await RunInstallAsync(repair: true);
                }

                break;
            case LaunchFix.InstallJava:
                if (await _dialogs.ConfirmAsync(header, message, "Install Java Runtime", "Close"))
                {
                    await RunInstallAsync(repair: true);
                }

                break;
            case LaunchFix.SignIn:
                if (await _dialogs.ConfirmAsync(header, message + "\n\nPlease sign in again.", "Go to Account", "Close"))
                {
                    _navigation.Navigate(AppPage.Account);
                }

                break;
            default:
                await _dialogs.ShowInfoAsync(header, message + (_launcher.CurrentLogFile is null ? string.Empty : "\n\nThe launch log is available on the Play page."), "Close");
                break;
        }
    }

    private IProgress<TransferProgress> CreateProgress() => new Progress<TransferProgress>(p =>
    {
        StageText = p.Stage;
        IsIndeterminate = p.IsIndeterminate;
        Progress = p.Fraction * 100;
        DetailText = p.BytesTotal > 1
            ? $"{FormatBytes(p.BytesDone)} / {FormatBytes(p.BytesTotal)} · {p.FilesDone}/{p.FilesTotal} files"
            : p.FilesTotal > 0 ? $"{p.FilesDone}/{p.FilesTotal} files" : string.Empty;
        SpeedText = p.BytesPerSecond > 0 ? $"{FormatBytes((long)p.BytesPerSecond)}/s" : string.Empty;
    });

    private void SetStage(string text)
    {
        StageText = text;
        IsIndeterminate = true;
        DetailText = string.Empty;
        SpeedText = string.Empty;
    }

    private void AppendLog(string line)
    {
        LaunchLog.Add(line);
        while (LaunchLog.Count > MaxLogLines)
        {
            LaunchLog.RemoveAt(0);
        }
    }

    private void OnLauncherChanged(object? sender, PropertyChangedEventArgs e)
    {
        _dispatcher.InvokeAsync(() =>
        {
            OnPropertyChanged(nameof(ProcessState));
            OnPropertyChanged(nameof(ProcessStateText));
            OnPropertyChanged(nameof(PrimaryText));
            RelayCommand.Refresh();
        });
    }

    private void OnProcessExited(int exitCode)
    {
        if (exitCode == 0)
        {
            _notifications.Show(NotificationKind.Info, "Minecraft exited.", "The game was closed normally.");
        }
        else
        {
            _notifications.Show(NotificationKind.Error, "Minecraft crashed.", $"Exit code {exitCode}. Open the launch log on the Play page for details.");
        }
    }

    private void OpenLaunchLog()
    {
        var folder = _launcher.CurrentLogFile is { } file ? System.IO.Path.GetDirectoryName(file)! : Core.Constants.AppPaths.Logs;
        if (!_shell.OpenFolder(folder))
        {
            _notifications.Show(NotificationKind.Warning, "Folder not found", folder);
        }
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.00} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B",
    };
}
