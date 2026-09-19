using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using GoatClient.Core.Commands;
using GoatClient.Core.Constants;
using GoatClient.Core.Helpers;
using GoatClient.Models;
using GoatClient.Services.Auth;
using GoatClient.Services.Dialogs;
using GoatClient.Services.Downloads;
using GoatClient.Services.Integrity;
using GoatClient.Services.Launch;
using GoatClient.Services.Java;
using GoatClient.Services.Logging;
using GoatClient.Services.Minecraft;
using GoatClient.Services.Navigation;
using GoatClient.Services.Notifications;
using GoatClient.Services.Platform;
using GoatClient.Services.Profiles;
using GoatClient.Services.Settings;
using GoatClient.Services.Startup;
using GoatClient.Services.Status;
using GoatClient.ViewModels;
using GoatClient.Views;

namespace GoatClient;

/// <summary>Application entry point and composition root (manual dependency injection).</summary>
public partial class App : Application
{
    private readonly CancellationTokenSource _lifetime = new();
    private FileLogger? _logger;
    private INotificationService? _notifications;
    private Mutex? _singleInstance;
    private HttpClient? _http;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Initialize application – single instance
        _singleInstance = new Mutex(true, @"Local\GoatClient.Launcher", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("GOAT CLIENT is already running.", AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown(0);
            return;
        }

        // 2./3. Check AppData and create required directories
        try
        {
            AppPaths.EnsureCreated();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"GOAT CLIENT could not create its data folder:{Environment.NewLine}{AppPaths.Root}{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // 4. Logging
        _logger = new FileLogger(AppPaths.Logs);
        _logger.Info($"{AppInfo.ProductName} {AppInfo.Version} starting ({Environment.OSVersion}, .NET {Environment.Version}).");
        RegisterGlobalExceptionHandlers();

        // Compose services and view models, show the window, then run steps 5–10 asynchronously.
        var mainViewModel = Compose(_logger);
        var window = new MainWindow { DataContext = mainViewModel };
        MainWindow = window;
        window.Show();

        await mainViewModel.InitializeAsync();
    }

    private MainViewModel Compose(ILogger logger)
    {
        var http = CreateHttpClient();
        _http = http;
        var store = new JsonFileStore(logger);
        var status = new StatusService();
        var systemInfo = new SystemInfoService(logger);
        var settings = new SettingsService(store, systemInfo, status, logger);
        var notifications = new NotificationService(() => settings.Current.Notifications, Dispatcher);
        _notifications = notifications;
        var dialogs = new DialogService();
        var startup = new StartupRegistrationService(logger);
        var shell = new ShellService(logger);
        var integrity = new IntegrityService();
        var downloads = new DownloadService(http, integrity, settings, logger);
        var versions = new MinecraftVersionService(
            new IMinecraftVersionProvider[] { new MojangVersionManifestProvider(downloads, logger), new InstalledVersionProvider() },
            logger);
        var java = new JavaService(AppPaths.Runtime, downloads, logger);
        var installation = new MinecraftInstallationService(versions, downloads, logger);
        var launcher = new MinecraftLauncherService(installation, logger);
        var official = new OfficialLauncherService(logger);
        var auth = new MicrosoftAuthService(http, new WindowsCredentialStore(), settings, store, logger);
        var skins = new SkinTextureLoader(http);
        var profiles = new ProfileService(store, settings, systemInfo, status, logger);
        var navigation = new NavigationService(logger);
        var bootstrapper = new LauncherBootstrapper(settings, profiles, java, versions, status, auth, logger);
        var game = new GameController(profiles, versions, installation, java, launcher, official, auth, settings, dialogs, notifications, status, navigation, shell, logger, Dispatcher, _lifetime.Token);

        AsyncRelayCommand.GlobalErrorHandler = ex =>
        {
            logger.Error("Unhandled command error.", ex);
            status.Set(LauncherStatus.Error, ex.Message);
            notifications.Show(NotificationKind.Error, "Something went wrong", ex.Message);
        };

        var main = new MainViewModel(navigation, status, dialogs, notifications, settings, profiles, bootstrapper, logger, _lifetime, auth, skins, shell, game);

        navigation.Register(AppPage.Home, () => new HomeViewModel(profiles, settings, versions, java, auth, status, game, navigation));
        navigation.Register(AppPage.Play, () => new PlayViewModel(profiles, versions, java, systemInfo, settings, notifications, shell, status, navigation, game, logger));
        navigation.Register(AppPage.Profiles, () => new ProfilesViewModel(profiles, settings, versions, java, systemInfo, dialogs, notifications, logger));
        navigation.Register(AppPage.Skins, () => new SkinsViewModel(auth, skins, navigation, logger));
        navigation.Register(AppPage.Settings, () => new SettingsViewModel(settings, profiles, versions, official, systemInfo, startup, shell, dialogs, notifications, logger));
        navigation.Register(AppPage.Account, () => new AccountViewModel(auth, shell, skins, dialogs, notifications, navigation, logger));

        return main;
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 32,
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"GoatClient/{AppInfo.Version}");
        return client;
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            _logger?.Critical("Fatal unhandled exception.", args.ExceptionObject as Exception);
            _logger?.Dispose();
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _logger?.Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Error("Unhandled UI exception.", e.Exception);
        e.Handled = true; // keep the launcher usable

        if (_notifications is not null)
        {
            _notifications.Show(NotificationKind.Error, "Unexpected error", e.Exception.Message);
        }
        else
        {
            MessageBox.Show(e.Exception.Message, AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _lifetime.Cancel();
        _logger?.Info($"{AppInfo.ProductName} exited (code {e.ApplicationExitCode}).");
        _http?.Dispose();
        _logger?.Dispose();
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
