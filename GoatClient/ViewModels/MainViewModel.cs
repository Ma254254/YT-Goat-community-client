using System.ComponentModel;
using System.Windows.Input;
using GoatClient.Core;
using GoatClient.Core.Commands;
using GoatClient.Core.Constants;
using GoatClient.Models;
using GoatClient.Services.Dialogs;
using GoatClient.Services.Logging;
using GoatClient.Services.Navigation;
using GoatClient.Services.Notifications;
using GoatClient.Services.Profiles;
using GoatClient.Services.Settings;
using GoatClient.Services.Startup;
using GoatClient.Services.Status;

namespace GoatClient.ViewModels;

/// <summary>Shell view model: navigation, sidebar, title bar, dialogs, notifications, startup and shutdown.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly ISettingsService _settings;
    private readonly IProfileService _profiles;
    private readonly LauncherBootstrapper _bootstrapper;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _lifetime;
    private bool _isInitializing = true;

    public MainViewModel(
        INavigationService navigation,
        IStatusService status,
        IDialogService dialogs,
        INotificationService notifications,
        ISettingsService settings,
        IProfileService profiles,
        LauncherBootstrapper bootstrapper,
        ILogger logger,
        CancellationTokenSource lifetime)
    {
        _navigation = navigation;
        _settings = settings;
        _profiles = profiles;
        _bootstrapper = bootstrapper;
        _logger = logger;
        _lifetime = lifetime;
        Status = status;
        Dialogs = dialogs;
        Notifications = notifications;

        _navigation.PropertyChanged += OnNavigationChanged;
        _settings.SettingsChanged += (_, _) => OnPropertyChanged(nameof(EnablePageTransitions));

        NavigateHomeCommand = CreateNavigateCommand(AppPage.Home);
        NavigatePlayCommand = CreateNavigateCommand(AppPage.Play);
        NavigateProfilesCommand = CreateNavigateCommand(AppPage.Profiles);
        NavigateSkinsCommand = CreateNavigateCommand(AppPage.Skins);
        NavigateSettingsCommand = CreateNavigateCommand(AppPage.Settings);
        NavigateAccountCommand = CreateNavigateCommand(AppPage.Account);
        LoginCommand = new AsyncRelayCommand(ShowLoginNotAvailableAsync);
        DismissNotificationCommand = new RelayCommand(p =>
        {
            if (p is NotificationItem item)
            {
                Notifications.Dismiss(item);
            }
        });
    }

    public IStatusService Status { get; }

    public IDialogService Dialogs { get; }

    public INotificationService Notifications { get; }

    public string ProductName => AppInfo.ProductName;

    public string VersionText => $"v{AppInfo.Version} · {AppInfo.Phase}";

    public ViewModelBase? CurrentViewModel => _navigation.CurrentViewModel;

    public AppPage? CurrentPage => _navigation.CurrentPage;

    public bool IsInitializing
    {
        get => _isInitializing;
        private set
        {
            if (SetProperty(ref _isInitializing, value))
            {
                RelayCommand.Refresh();
            }
        }
    }

    public bool EnablePageTransitions => _settings.Current.EnablePageTransitions;

    public bool ConfirmBeforeClosing => _settings.Current.ConfirmBeforeClosing;

    /// <summary>Phase 1 has no account system – this text never pretends otherwise.</summary>
    public string AccountStatusText => "Not connected";

    public ICommand NavigateHomeCommand { get; }

    public ICommand NavigatePlayCommand { get; }

    public ICommand NavigateProfilesCommand { get; }

    public ICommand NavigateSkinsCommand { get; }

    public ICommand NavigateSettingsCommand { get; }

    public ICommand NavigateAccountCommand { get; }

    public ICommand LoginCommand { get; }

    public ICommand DismissNotificationCommand { get; }

    public async Task InitializeAsync()
    {
        try
        {
            var warnings = await _bootstrapper.RunAsync(_lifetime.Token);
            _navigation.Navigate(AppPage.Home);
            IsInitializing = false;

            if (warnings.Count > 0)
            {
                await Dialogs.ShowInfoAsync(
                    "Started with warnings",
                    string.Join(Environment.NewLine + Environment.NewLine, warnings)
                    + Environment.NewLine + Environment.NewLine + $"Details are in the log folder:{Environment.NewLine}{AppPaths.Logs}");
            }
        }
        catch (OperationCanceledException)
        {
            // Window was closed during startup.
        }
        catch (Exception ex)
        {
            _logger.Critical("Startup failed.", ex);
            Status.Set(LauncherStatus.Error, "Startup failed");
            IsInitializing = false;
            await Dialogs.ShowInfoAsync("Startup failed", $"{ex.Message}{Environment.NewLine}{Environment.NewLine}Log folder: {AppPaths.Logs}", "Close");
        }
    }

    /// <summary>Cancels running operations and persists all data before the window closes.</summary>
    public async Task ShutdownAsync()
    {
        _logger.Info("Shutting down…");
        _lifetime.Cancel();

        try
        {
            await _settings.SaveCurrentAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("Settings could not be saved during shutdown.", ex);
        }

        if (!IsInitializing)
        {
            try
            {
                await _profiles.SaveAsync();
            }
            catch (Exception ex)
            {
                _logger.Error("Profiles could not be saved during shutdown.", ex);
            }
        }
    }

    private ICommand CreateNavigateCommand(AppPage page)
        => new RelayCommand(() => _navigation.Navigate(page), () => !IsInitializing);

    private Task ShowLoginNotAvailableAsync()
        => Dialogs.ShowInfoAsync(
            "Microsoft Account Login",
            "Coming in Phase 2." + Environment.NewLine + Environment.NewLine
            + "GOAT CLIENT does not sign you in yet and stores no account data. "
            + "Secure Microsoft authentication will be added in the next development phase.");

    private void OnNavigationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(INavigationService.CurrentViewModel))
        {
            OnPropertyChanged(nameof(CurrentViewModel));
        }
        else if (e.PropertyName == nameof(INavigationService.CurrentPage))
        {
            OnPropertyChanged(nameof(CurrentPage));
        }
    }
}
