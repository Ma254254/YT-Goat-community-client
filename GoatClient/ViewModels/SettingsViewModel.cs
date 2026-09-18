using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using GoatClient.Core.Commands;
using GoatClient.Models;
using GoatClient.Services.Dialogs;
using GoatClient.Services.Java;
using GoatClient.Services.Logging;
using GoatClient.Services.Minecraft;
using GoatClient.Services.Notifications;
using GoatClient.Services.Platform;
using GoatClient.Services.Profiles;
using GoatClient.Services.Settings;

namespace GoatClient.ViewModels;

public sealed record ProfileChoice(Guid Id, string Name);

/// <summary>
/// Settings page. Every change is saved automatically (short debounce);
/// "Save" writes immediately.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private static readonly TimeSpan AutoSaveDelay = TimeSpan.FromMilliseconds(700);

    private readonly ISettingsService _settings;
    private readonly IProfileService _profiles;
    private readonly IMinecraftVersionService _versions;
    private readonly IJavaService _java;
    private readonly ISystemInfoService _systemInfo;
    private readonly IStartupRegistrationService _startup;
    private readonly IShellService _shell;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly ILogger _logger;

    private CancellationTokenSource? _autoSaveCts;
    private bool _loading;
    private string _selectedCategory = "LAUNCHER";

    private bool _launchWithWindows;
    private bool _notificationsEnabled;
    private bool _confirmBeforeClosing;
    private string _minecraftDirectory = string.Empty;
    private string _defaultVersion = string.Empty;
    private Guid _defaultProfileId;
    private int _defaultRamMb;
    private string _defaultJvmArguments = string.Empty;
    private bool _enablePageTransitions;
    private bool _showHomeLogo;
    private string _microsoftClientId = string.Empty;
    private bool _showSnapshots;
    private bool _installMissingRuntime;
    private string _downloadDirectory = string.Empty;
    private int _maxParallelDownloads;
    private int _downloadRetryCount;
    private bool _isInstallingJava;
    private string _javaInstallStatus = string.Empty;

    private string _saveState = "All changes saved";
    private bool _hasError;
    private string _managedRuntimeText = string.Empty;
    private string _systemJavaText = string.Empty;

    public SettingsViewModel(
        ISettingsService settings,
        IProfileService profiles,
        IMinecraftVersionService versions,
        IJavaService java,
        ISystemInfoService systemInfo,
        IStartupRegistrationService startup,
        IShellService shell,
        IDialogService dialogs,
        INotificationService notifications,
        ILogger logger)
    {
        _settings = settings;
        _profiles = profiles;
        _versions = versions;
        _java = java;
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
        OpenRuntimeFolderCommand = new RelayCommand(() => OpenFolder(_java.RuntimeRoot));
        RescanJavaCommand = new AsyncRelayCommand(RescanJavaAsync);
        InstallJavaCommand = new AsyncRelayCommand(p => InstallJavaAsync(p), _ => !_isInstallingJava);
        BrowseDownloadDirectoryCommand = new RelayCommand(BrowseDownloadDirectory);

        _java.RuntimesChanged += (_, _) => UpdateJavaTexts();
        _profiles.ProfilesChanged += (_, _) => RefreshProfileChoices();
    }

    public IReadOnlyList<string> Categories { get; } = ["LAUNCHER", "MINECRAFT", "JAVA", "DOWNLOADS", "APPEARANCE"];

    public string SelectedCategory { get => _selectedCategory; set => SetProperty(ref _selectedCategory, value); }

    public IReadOnlyList<RamOption> RamOptions { get; }

    public IReadOnlyList<MinecraftVersionInfo> Versions => _versions.Versions;

    public ObservableCollection<ProfileChoice> ProfileChoices { get; } = new();

    public string MemorySummary => _systemInfo.MemorySummary;

    // GENERAL
    public bool LaunchWithWindows { get => _launchWithWindows; set => SetAndSave(ref _launchWithWindows, value); }

    public bool NotificationsEnabled { get => _notificationsEnabled; set => SetAndSave(ref _notificationsEnabled, value); }

    public bool ConfirmBeforeClosing { get => _confirmBeforeClosing; set => SetAndSave(ref _confirmBeforeClosing, value); }

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

    public string MicrosoftClientId { get => _microsoftClientId; set => SetAndSave(ref _microsoftClientId, value); }

    public bool ShowSnapshots { get => _showSnapshots; set => SetAndSave(ref _showSnapshots, value); }

    // JAVA
    public bool InstallMissingRuntimeAutomatically { get => _installMissingRuntime; set => SetAndSave(ref _installMissingRuntime, value); }

    public string JavaInstallStatus { get => _javaInstallStatus; private set => SetProperty(ref _javaInstallStatus, value); }

    // DOWNLOADS
    public string DownloadDirectory { get => _downloadDirectory; set => SetAndSave(ref _downloadDirectory, value); }

    public IReadOnlyList<int> ParallelDownloadOptions { get; } = [1, 2, 4, 8, 12, 16];

    public IReadOnlyList<int> RetryOptions { get; } = [0, 1, 2, 3, 5, 10];

    public int MaxParallelDownloads { get => _maxParallelDownloads; set => SetAndSave(ref _maxParallelDownloads, value); }

    public int DownloadRetryCount { get => _downloadRetryCount; set => SetAndSave(ref _downloadRetryCount, value); }

    // APPEARANCE
    public bool EnablePageTransitions { get => _enablePageTransitions; set => SetAndSave(ref _enablePageTransitions, value); }

    public bool ShowHomeLogo { get => _showHomeLogo; set => SetAndSave(ref _showHomeLogo, value); }

    // JAVA (read-only information)
    public string RuntimeDirectory => _java.RuntimeRoot;

    public string ManagedRuntimeText { get => _managedRuntimeText; private set => SetProperty(ref _managedRuntimeText, value); }

    public string SystemJavaText { get => _systemJavaText; private set => SetProperty(ref _systemJavaText, value); }

    public string ProvisioningText =>
        "GOAT CLIENT downloads the Java runtime required by each Minecraft version from Mojang's official runtime distribution and verifies every file (SHA-1).";

    public string SaveState { get => _saveState; private set => SetProperty(ref _saveState, value); }

    public bool HasError { get => _hasError; private set => SetProperty(ref _hasError, value); }

    public ICommand SelectCategoryCommand { get; }

    public ICommand SaveSettingsCommand { get; }

    public ICommand BrowseMinecraftDirectoryCommand { get; }

    public ICommand OpenDataFolderCommand { get; }

    public ICommand OpenRuntimeFolderCommand { get; }

    public ICommand RescanJavaCommand { get; }

    public ICommand InstallJavaCommand { get; }

    public ICommand BrowseDownloadDirectoryCommand { get; }

    public override void OnNavigatedTo()
    {
        LoadFromSettings();
        UpdateJavaTexts();
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
            LaunchWithWindows = _startup.IsEnabled(); // Registry is the source of truth.
            NotificationsEnabled = s.Notifications;
            ConfirmBeforeClosing = s.ConfirmBeforeClosing;
            MinecraftDirectory = s.MinecraftDirectory;
            DefaultVersion = s.DefaultVersion;
            DefaultRamMb = s.DefaultRamMb;
            DefaultJvmArguments = s.DefaultJvmArguments;
            EnablePageTransitions = s.EnablePageTransitions;
            ShowHomeLogo = s.ShowHomeLogo;
            MicrosoftClientId = s.MicrosoftClientId;
            ShowSnapshots = s.ShowSnapshots;
            InstallMissingRuntimeAutomatically = s.InstallMissingRuntimeAutomatically;
            DownloadDirectory = s.DownloadDirectory;
            MaxParallelDownloads = s.MaxParallelDownloads;
            DownloadRetryCount = s.DownloadRetryCount;
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
            return "Minecraft directory: please enter a full path (for example C:\\Games\\Minecraft).";
        }

        if (directory.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return "Minecraft directory contains invalid characters.";
        }

        var downloads = DownloadDirectory?.Trim() ?? string.Empty;
        if (downloads.Length == 0 || !Path.IsPathFullyQualified(downloads))
        {
            return "Download directory: please enter a full path.";
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
            Directory.CreateDirectory(DownloadDirectory.Trim());

            ApplyStartupRegistration();

            var s = _settings.Current.Clone();
            s.LaunchWithWindows = LaunchWithWindows;
            s.Notifications = NotificationsEnabled;
            s.ConfirmBeforeClosing = ConfirmBeforeClosing;
            s.MinecraftDirectory = directory;
            s.DefaultVersion = DefaultVersion;
            s.DefaultProfileId = DefaultProfileId == Guid.Empty ? null : DefaultProfileId;
            s.DefaultRamMb = DefaultRamMb;
            s.DefaultJvmArguments = DefaultJvmArguments?.Trim() ?? string.Empty;
            s.EnablePageTransitions = EnablePageTransitions;
            s.ShowHomeLogo = ShowHomeLogo;
            s.MicrosoftClientId = MicrosoftClientId?.Trim() ?? string.Empty;
            s.ShowSnapshots = ShowSnapshots;
            s.InstallMissingRuntimeAutomatically = InstallMissingRuntimeAutomatically;
            s.DownloadDirectory = DownloadDirectory.Trim();
            s.MaxParallelDownloads = MaxParallelDownloads;
            s.DownloadRetryCount = DownloadRetryCount;

            await _settings.SaveAsync(s);

            SaveState = $"All changes saved · {DateTime.Now:HH:mm:ss}";
            HasError = false;
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

    private void UpdateJavaTexts()
    {
        var info = _java.Info;
        ManagedRuntimeText = info.HasManagedRuntime
            ? string.Join(Environment.NewLine, info.ManagedRuntimes.Select(r => $"{r.DisplayName} – {r.HomeDirectory}"))
            : "No managed runtime installed yet – it is installed automatically with Minecraft.";

        SystemJavaText = info.SystemRuntime is null
            ? "No system Java detected. That is fine – GOAT CLIENT does not need one."
            : $"{info.SystemRuntime.DisplayName} (informational only – GOAT CLIENT does not depend on it)";
    }

    private async Task InstallJavaAsync(object? parameter)
    {
        if (!int.TryParse(parameter?.ToString(), out var major))
        {
            return;
        }

        _isInstallingJava = true;
        RelayCommand.Refresh();
        var progress = new Progress<TransferProgress>(p => JavaInstallStatus = p.BytesTotal > 1
            ? $"{p.Stage} – {GameController.FormatBytes(p.BytesDone)} / {GameController.FormatBytes(p.BytesTotal)}"
            : p.Stage);
        try
        {
            var runtime = await _java.EnsureRuntimeAsync(new JavaRequirement(major, null), verify: true, progress, CancellationToken.None);
            JavaInstallStatus = $"Java {major} verified: {runtime.DisplayName}";
            _notifications.Show(NotificationKind.Success, $"Java {major} is ready.", runtime.HomeDirectory);
        }
        catch (Exception ex)
        {
            _logger.Error($"Java {major} installation failed.", ex);
            JavaInstallStatus = ex.Message;
            _notifications.Show(NotificationKind.Error, $"Java {major} could not be installed", ex.Message);
        }
        finally
        {
            _isInstallingJava = false;
            RelayCommand.Refresh();
            UpdateJavaTexts();
        }
    }

    private void BrowseDownloadDirectory()
    {
        var folder = _dialogs.PickFolder(DownloadDirectory, "Choose the download directory");
        if (folder is not null)
        {
            DownloadDirectory = folder;
        }
    }

    private async Task RescanJavaAsync()
    {
        await _java.ScanAsync();
        _notifications.Show(NotificationKind.Info, "Java runtimes checked", ManagedRuntimeText);
    }

    private void BrowseMinecraftDirectory()
    {
        var folder = _dialogs.PickFolder(MinecraftDirectory, "Choose the Minecraft directory");
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
