using System.ComponentModel;
using System.Windows.Input;
using GoatClient.Core;
using GoatClient.Core.Commands;
using GoatClient.Core.Constants;
using GoatClient.Models;
using System.Windows.Media.Imaging;
using GoatClient.Services.Auth;
using GoatClient.Services.Dialogs;
using GoatClient.Services.Platform;
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
    private readonly IAuthService _auth;
    private readonly SkinTextureLoader _skins;
    private readonly IShellService _shell;
    private bool _isInitializing = true;
    private BitmapSource? _accountFace;
    private BitmapSource? _accountHat;

    public MainViewModel(
        INavigationService navigation,
        IStatusService status,
        IDialogService dialogs,
        INotificationService notifications,
        ISettingsService settings,
        IProfileService profiles,
        LauncherBootstrapper bootstrapper,
        ILogger logger,
        CancellationTokenSource lifetime,
        IAuthService auth,
        SkinTextureLoader skins,
        IShellService shell,
        GameController game)
    {
        _auth = auth;
        _skins = skins;
        _shell = shell;
        Game = game;
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
        LoginCommand = new RelayCommand(() => _navigation.Navigate(AppPage.Account), () => !IsInitializing);
        OpenDiscordCommand = new RelayCommand(OpenDiscord);
        _auth.PropertyChanged += (_, _) => OnAccountChanged();
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

    public GameController Game { get; }

    /// <summary>Real Minecraft name when signed in – never a placeholder account.</summary>
    public string AccountStatusText => _auth.Account?.Username ?? "Not connected";

    public string AccountSubText => _auth.State switch
    {
        AuthState.SignedIn => "● Connected",
        AuthState.SessionExpired => "Sign in again",
        _ => "Microsoft account",
    };

    public bool IsAccountConnected => _auth.State == AuthState.SignedIn;

    public string AccountButtonText => _auth.State == AuthState.SignedIn ? "ACCOUNT" : "LOGIN";

    public BitmapSource? AccountFace { get => _accountFace; private set => SetProperty(ref _accountFace, value); }

    public BitmapSource? AccountHat { get => _accountHat; private set => SetProperty(ref _accountHat, value); }

    public string DiscordInvite => AppLinks.DiscordInvite;

    public ICommand OpenDiscordCommand { get; }

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

    private async void OnAccountChanged()
    {
        OnPropertyChanged(nameof(AccountStatusText));
        OnPropertyChanged(nameof(AccountSubText));
        OnPropertyChanged(nameof(IsAccountConnected));
        OnPropertyChanged(nameof(AccountButtonText));
        try
        {
            var images = await _skins.LoadAsync(_auth.Account?.SkinUrl, CancellationToken.None);
            AccountFace = images?.Face;
            AccountHat = images?.Hat;
        }
        catch (Exception ex)
        {
            _logger.Warning("Skin texture could not be loaded.", ex);
        }
    }

    private void OpenDiscord()
    {
        try
        {
            _shell.OpenUrl(AppLinks.DiscordInvite);
        }
        catch (Exception ex)
        {
            _logger.Error("Could not open the Discord invite.", ex);
            Notifications.Show(NotificationKind.Error, "Could not open browser", AppLinks.DiscordInvite);
        }
    }

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
