using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using GoatClient.Core.Commands;
using GoatClient.Models;
using GoatClient.Services.Auth;
using GoatClient.Services.Dialogs;
using GoatClient.Services.Launch;
using GoatClient.Services.Logging;
using GoatClient.Services.Minecraft;
using GoatClient.Services.Notifications;
using GoatClient.Services.Platform;
using GoatClient.Services.Profiles;
using GoatClient.Services.Settings;
using GoatClient.Services.Theming;
using System.Windows.Media;

namespace GoatClient.ViewModels;

public sealed record ProfileChoice(Guid Id, string Name);

public sealed record LaunchModeOption(LaunchMode Value, string Label, string Description);

/// <summary>Selectable theme with preview swatches.</summary>
public sealed record ThemeOption(string Id, string Name, Brush Background, Brush Card, Brush Secondary);

/// <summary>Selectable accent color with preview swatches.</summary>
public sealed record AccentOption(string Id, string Name, Brush Accent, Brush Accent2);

/// <summary>
/// Settings page (LAUNCHER, MINECRAFT, APPEARANCE). Every change is saved automatically
/// (short debounce); "Save" writes immediately.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private static readonly TimeSpan AutoSaveDelay = TimeSpan.FromMilliseconds(700);

    private readonly ISettingsService _settings;
    private readonly IProfileService _profiles;
    private readonly IMinecraftVersionService _versions;
    private readonly IOfficialLauncherService _official;
    private readonly ISystemInfoService _systemInfo;
    private readonly IStartupRegistrationService _startup;
    private readonly IShellService _shell;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly ILogger _logger;
    private readonly IAuthService _auth;

    private CancellationTokenSource? _autoSaveCts;
    private bool _loading;
    private string _selectedCategory = "LAUNCHER";

    private LaunchMode _launchMode;
    private bool _launchWithWindows;
    private bool _notificationsEnabled;
    private bool _confirmBeforeClosing;
    private string _microsoftClientId = string.Empty;
    private string _minecraftDirectory = string.Empty;
    private string _defaultVersion = string.Empty;
    private Guid _defaultProfileId;
    private int _defaultRamMb;
    private string _defaultJvmArguments = string.Empty;
    private bool _showSnapshots;
    private bool _enablePageTransitions;
    private string _theme = ThemeService.DefaultThemeId;
    private string _accent = ThemeService.DefaultAccentId;
    private string _appliedTheme = ThemeService.DefaultThemeId;
    private string _appliedAccent = ThemeService.DefaultAccentId;

    private string _saveState = "All changes saved";
    private bool _hasError;

    public SettingsViewModel(
        ISettingsService settings,
        IProfileService profiles,
        IMinecraftVersionService versions,
        IOfficialLauncherService official,
        ISystemInfoService systemInfo,
        IStartupRegistrationService startup,
        IShellService shell,
        IDialogService dialogs,
        INotificationService notifications,
        ILogger logger,
        IAuthService auth)
    {
        _auth = auth;
        _settings = settings;
        _profiles = profiles;
        _versions = versions;
        _official = official;
        _systemInfo = systemInfo;
        _startup = startup;
        _shell = shell;
        _dialogs = dialogs;
        _notifications = notifications;
        _logger = logger;

        RamOptions = systemInfo.GetRamOptions();
        SelectCategoryCommand = new RelayCommand(p => SelectedCategory = p as string ?? "LAUNCHER");
        SaveSettingsCommand = new AsyncRelayCommand(() => SaveNowAsync(showToast: true));
        BrowseMinecraftDirectoryCommand = new RelayCommand(BrowseMinecraftDirectory);
        OpenDataFolderCommand = new RelayCommand(() => OpenFolder(_settings.Current.MinecraftDirectory));
        OpenOfficialFolderCommand = new RelayCommand(() => OpenFolder(_official.MinecraftDirectory));
        RestartCommand = new RelayCommand(() => _shell.RestartApplication());

        // The theme currently on screen (applied at startup).
        (_appliedTheme, _appliedAccent) = (ThemeService.FindTheme(settings.Current.Theme).Id, ThemeService.FindAccent(settings.Current.Accent).Id);

        _profiles.ProfilesChanged += (_, _) => RefreshProfileChoices();
    }

    public IReadOnlyList<string> Categories { get; } = ["LAUNCHER", "MINECRAFT", "APPEARANCE"];

    public string SelectedCategory { get => _selectedCategory; set => SetProperty(ref _selectedCategory, value); }

    public IReadOnlyList<LaunchModeOption> LaunchModes { get; } =
    [
        new(LaunchMode.OfficialLauncher, "Official Minecraft Launcher (recommended)", "PLAY creates a GOAT CLIENT profile in the official launcher and opens it. You sign in there – no extra setup."),
        new(LaunchMode.Direct, "Direct launch (advanced)", "GOAT CLIENT installs and starts Minecraft itself. Requires your own Azure app ID approved for Minecraft."),
    ];

    public IReadOnlyList<RamOption> RamOptions { get; }

    public IReadOnlyList<MinecraftVersionInfo> Versions => _versions.Filter(null, _settings.Current.ShowSnapshots);

    public ObservableCollection<ProfileChoice> ProfileChoices { get; } = new();

    public string MemorySummary => _systemInfo.MemorySummary;

    // LAUNCHER
    public LaunchMode LaunchMode
    {
        get => _launchMode;
        set
        {
            SetAndSave(ref _launchMode, value);
            OnPropertyChanged(nameof(IsDirectMode));
        }
    }

    public bool IsDirectMode => _launchMode == LaunchMode.Direct;

    public string OfficialLauncherStatus => _official.StatusText;

    public bool LaunchWithWindows { get => _launchWithWindows; set => SetAndSave(ref _launchWithWindows, value); }

    public bool NotificationsEnabled { get => _notificationsEnabled; set => SetAndSave(ref _notificationsEnabled, value); }

    public bool ConfirmBeforeClosing { get => _confirmBeforeClosing; set => SetAndSave(ref _confirmBeforeClosing, value); }

    public string MicrosoftClientId { get => _microsoftClientId; set => SetAndSave(ref _microsoftClientId, value); }

    /// <summary>"built-in (microsoft-auth.json)", "Settings override" or "not configured".</summary>
    public string ClientIdSource => _auth.ClientIdSource;

    // MINECRAFT
    public string MinecraftDirectory { get => _minecraftDirectory; set => SetAndSave(ref _minecraftDirectory, value); }

    public string DefaultVersion
    {
        get => _defaultVersion;
        set
        {
            if (!string.IsNullOrEmpty(value))
            {
                SetAndSave(ref _defaultVersion, value);
            }
        }
    }

    public Guid DefaultProfileId { get => _defaultProfileId; set => SetAndSave(ref _defaultProfileId, value); }

    public int DefaultRamMb
    {
        get => _defaultRamMb;
        set
        {
            if (value > 0)
            {
                SetAndSave(ref _defaultRamMb, value);
            }
        }
    }

    public string DefaultJvmArguments { get => _defaultJvmArguments; set => SetAndSave(ref _defaultJvmArguments, value); }

    public bool ShowSnapshots { get => _showSnapshots; set => SetAndSave(ref _showSnapshots, value); }

    // APPEARANCE
    public IReadOnlyList<ThemeOption> ThemeOptions { get; } = ThemeService.Themes
        .Select(t => new ThemeOption(t.Id, t.Name, Frozen(t.Background), Frozen(t.Card), Frozen(t.Secondary)))
        .ToList();

    public IReadOnlyList<AccentOption> AccentOptions { get; } = ThemeService.Accents
        .Select(a => new AccentOption(a.Id, a.Name, Frozen(a.Accent), Frozen(a.Accent2)))
        .ToList();

    public string Theme
    {
        get => _theme;
        set
        {
            if (value is not null)
            {
                SetAndSave(ref _theme, value);
                OnPropertyChanged(nameof(RestartRequired));
            }
        }
    }

    public string Accent
    {
        get => _accent;
        set
        {
            if (value is not null)
            {
                SetAndSave(ref _accent, value);
                OnPropertyChanged(nameof(RestartRequired));
            }
        }
    }

    /// <summary>True when the saved theme differs from the one on screen.</summary>
    public bool RestartRequired => _theme != _appliedTheme || _accent != _appliedAccent;

    public ICommand RestartCommand { get; }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public bool EnablePageTransitions { get => _enablePageTransitions; set => SetAndSave(ref _enablePageTransitions, value); }

    public string SaveState { get => _saveState; private set => SetProperty(ref _saveState, value); }

    public bool HasError { get => _hasError; private set => SetProperty(ref _hasError, value); }

    public ICommand SelectCategoryCommand { get; }

    public ICommand SaveSettingsCommand { get; }

    public ICommand BrowseMinecraftDirectoryCommand { get; }

    public ICommand OpenDataFolderCommand { get; }

    public ICommand OpenOfficialFolderCommand { get; }

    public override void OnNavigatedTo()
    {
        LoadFromSettings();
        OnPropertyChanged(nameof(OfficialLauncherStatus));
        OnPropertyChanged(nameof(ClientIdSource));
    }

    public override void OnNavigatedFrom()
    {
        // Flush a pending auto-save immediately.
        if (_autoSaveCts is not null)
        {
            _autoSaveCts.Cancel();
            _autoSaveCts = null;
            _ = SaveNowAsync(showToast: false);
        }
    }

    private void LoadFromSettings()
    {
        _loading = true;
        try
        {
            var s = _settings.Current;
            LaunchMode = s.LaunchMode;
            LaunchWithWindows = _startup.IsEnabled(); // Registry is the source of truth.
            NotificationsEnabled = s.Notifications;
            ConfirmBeforeClosing = s.ConfirmBeforeClosing;
            MicrosoftClientId = s.MicrosoftClientId;
            MinecraftDirectory = s.MinecraftDirectory;
            DefaultVersion = s.DefaultVersion;
            DefaultRamMb = s.DefaultRamMb;
            DefaultJvmArguments = s.DefaultJvmArguments;
            ShowSnapshots = s.ShowSnapshots;
            EnablePageTransitions = s.EnablePageTransitions;
            Theme = ThemeService.FindTheme(s.Theme).Id;
            Accent = ThemeService.FindAccent(s.Accent).Id;
            RefreshProfileChoices();
            OnPropertyChanged(nameof(Versions));
        }
        finally
        {
            _loading = false;
        }
    }

    private void RefreshProfileChoices()
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            ProfileChoices.Clear();
            ProfileChoices.Add(new ProfileChoice(Guid.Empty, "Last selected profile"));
            foreach (var profile in _profiles.Profiles)
            {
                ProfileChoices.Add(new ProfileChoice(profile.Id, profile.Name));
            }

            var id = _settings.Current.DefaultProfileId ?? Guid.Empty;
            _defaultProfileId = ProfileChoices.Any(c => c.Id == id) ? id : Guid.Empty;
            OnPropertyChanged(nameof(DefaultProfileId));
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    private void SetAndSave<T>(ref T field, T value, [global::System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!SetProperty(ref field, value, name) || _loading)
        {
            return;
        }

        SaveState = "Saving…";
        HasError = false;
        _autoSaveCts?.Cancel();
        _autoSaveCts = new CancellationTokenSource();
        _ = AutoSaveAsync(_autoSaveCts.Token);
    }

    private async Task AutoSaveAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(AutoSaveDelay, token);
            _autoSaveCts = null;
            await SaveNowAsync(showToast: false);
        }
        catch (OperationCanceledException)
        {
            // A newer change restarted the timer.
        }
    }

    private string? Validate()
    {
        var directory = MinecraftDirectory?.Trim() ?? string.Empty;
        if (directory.Length == 0 || !Path.IsPathFullyQualified(directory))
        {
            return "Game directory: please enter a full path (for example C:\\Games\\GoatClient).";
        }

        if (directory.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return "Game directory contains invalid characters.";
        }

        var clientId = MicrosoftClientId?.Trim() ?? string.Empty;
        if (clientId.Length > 0 && !Guid.TryParse(clientId, out _))
        {
            return "Microsoft application (client) ID: must be a GUID (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx).";
        }

        var args = DefaultJvmArguments ?? string.Empty;
        if (args.Contains("-Xmx", StringComparison.OrdinalIgnoreCase) || args.Contains("-Xms", StringComparison.OrdinalIgnoreCase))
        {
            return "JVM arguments: please use the RAM setting instead of -Xmx / -Xms.";
        }

        return null;
    }

    private async Task SaveNowAsync(bool showToast)
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts = null;

        var error = Validate();
        if (error is not null)
        {
            SaveState = error;
            HasError = true;
            return;
        }

        try
        {
            var directory = MinecraftDirectory.Trim();
            Directory.CreateDirectory(directory);

            ApplyStartupRegistration();

            var s = _settings.Current.Clone();
            s.LaunchMode = LaunchMode;
            s.LaunchWithWindows = LaunchWithWindows;
            s.Notifications = NotificationsEnabled;
            s.ConfirmBeforeClosing = ConfirmBeforeClosing;
            s.MicrosoftClientId = MicrosoftClientId?.Trim() ?? string.Empty;
            s.MinecraftDirectory = directory;
            s.DefaultVersion = DefaultVersion;
            s.DefaultProfileId = DefaultProfileId == Guid.Empty ? null : DefaultProfileId;
            s.DefaultRamMb = DefaultRamMb;
            s.DefaultJvmArguments = DefaultJvmArguments?.Trim() ?? string.Empty;
            s.ShowSnapshots = ShowSnapshots;
            s.EnablePageTransitions = EnablePageTransitions;
            s.Theme = Theme;
            s.Accent = Accent;

            await _settings.SaveAsync(s);

            SaveState = $"All changes saved · {DateTime.Now:HH:mm:ss}";
            HasError = false;
            OnPropertyChanged(nameof(Versions));
            OnPropertyChanged(nameof(ClientIdSource));
            if (showToast)
            {
                _notifications.Show(NotificationKind.Success, "Settings saved", "Your settings were written to settings.json.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Saving settings failed.", ex);
            SaveState = $"Not saved: {ex.Message}";
            HasError = true;
            _notifications.Show(NotificationKind.Error, "Settings not saved", ex.Message);
        }
    }

    private void ApplyStartupRegistration()
    {
        if (_startup.IsEnabled() == LaunchWithWindows)
        {
            return;
        }

        try
        {
            _startup.SetEnabled(LaunchWithWindows);
        }
        catch (Exception ex)
        {
            _logger.Error("Updating the Windows startup entry failed.", ex);
            _loading = true;
            LaunchWithWindows = _startup.IsEnabled();
            _loading = false;
            _notifications.Show(NotificationKind.Error, "Windows startup", $"The startup entry could not be changed: {ex.Message}");
        }
    }

    private void BrowseMinecraftDirectory()
    {
        var folder = _dialogs.PickFolder(MinecraftDirectory, "Choose the game directory root");
        if (folder is not null)
        {
            MinecraftDirectory = folder;
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            if (!_shell.OpenFolder(path))
            {
                _notifications.Show(NotificationKind.Warning, "Folder not found", path);
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not open '{path}'.", ex);
            _notifications.Show(NotificationKind.Error, "Could not open folder", ex.Message);
        }
    }
}
