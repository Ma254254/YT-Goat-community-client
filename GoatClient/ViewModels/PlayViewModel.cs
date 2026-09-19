using System.Collections.ObjectModel;
using System.Windows.Input;
using GoatClient.Core.Commands;
using GoatClient.Models;
using GoatClient.Services.Java;
using GoatClient.Services.Logging;
using GoatClient.Services.Minecraft;
using GoatClient.Services.Navigation;
using GoatClient.Services.Notifications;
using GoatClient.Services.Platform;
using GoatClient.Services.Profiles;
using GoatClient.Services.Settings;
using GoatClient.Services.Status;

namespace GoatClient.ViewModels;

/// <summary>Play page: choose profile, version, RAM and Java; install, repair and launch.</summary>
public sealed class PlayViewModel : ViewModelBase
{
    private readonly IProfileService _profiles;
    private readonly IMinecraftVersionService _versions;
    private readonly IJavaService _java;
    private readonly ISystemInfoService _systemInfo;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly IShellService _shell;
    private readonly IStatusService _status;
    private readonly ILogger _logger;

    private bool _refreshing;
    private LauncherProfile? _selectedProfile;
    private MinecraftVersionInfo? _selectedVersion;
    private int _selectedRamMb;
    private JavaRuntimePreference _selectedJava;
    private string _versionFilter = string.Empty;
    private string _gameDirectory = string.Empty;
    private string _javaTitle = string.Empty;
    private string _javaDetail = string.Empty;
    private string _versionSummary = string.Empty;

    public PlayViewModel(
        IProfileService profiles,
        IMinecraftVersionService versions,
        IJavaService java,
        ISystemInfoService systemInfo,
        ISettingsService settings,
        INotificationService notifications,
        IShellService shell,
        IStatusService status,
        INavigationService navigation,
        GameController game,
        ILogger logger)
    {
        _profiles = profiles;
        _versions = versions;
        _java = java;
        _systemInfo = systemInfo;
        _settings = settings;
        _notifications = notifications;
        _shell = shell;
        _status = status;
        _logger = logger;
        Game = game;

        RamOptions = systemInfo.GetRamOptions();
        OpenGameDirectoryCommand = new RelayCommand(OpenGameDirectory);
        ManageProfilesCommand = new RelayCommand(() => navigation.Navigate(AppPage.Profiles));
        ClearFilterCommand = new RelayCommand(() => VersionFilter = string.Empty);

        _profiles.ProfilesChanged += (_, _) => Refresh();
        _versions.VersionsChanged += (_, _) => Refresh();
        _java.RuntimesChanged += (_, _) => UpdateJava();
        _settings.SettingsChanged += (_, _) =>
        {
            ApplyFilter();
            UpdateJava();
        };
    }

    public GameController Game { get; }

    public ObservableCollection<LauncherProfile> Profiles { get; } = new();

    public ObservableCollection<MinecraftVersionInfo> FilteredVersions { get; } = new();

    public IReadOnlyList<RamOption> RamOptions { get; }

    public IReadOnlyList<JavaPreferenceOption> JavaPreferences { get; } =
    [
        new(JavaRuntimePreference.Automatic, "Automatic (recommended)"),
        new(JavaRuntimePreference.Java21, "Java 21 (managed)"),
        new(JavaRuntimePreference.Java17, "Java 17 (managed)"),
    ];

    public string MemorySummary => _systemInfo.MemorySummary;

    public string VersionSourceText => string.IsNullOrEmpty(_versions.SourceDescription)
        ? "Version list not loaded – check your internet connection"
        : _versions.SourceDescription;

    public LauncherProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (_refreshing || value is null || ReferenceEquals(value, _selectedProfile))
            {
                return;
            }

            _selectedProfile = value;
            OnPropertyChanged();
            _ = RunSafeAsync(() => _profiles.SelectAsync(value.Id));
        }
    }

    public MinecraftVersionInfo? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            // null happens when the filter hides the selection – keep the real selection.
            if (_refreshing || value is null || ReferenceEquals(value, _selectedVersion))
            {
                return;
            }

            _selectedVersion = value;
            OnPropertyChanged();
            _ = UpdateSelectedProfileAsync(p => p.MinecraftVersion = value.Id, $"Minecraft {value.Id} selected");
        }
    }

    public int SelectedRamMb
    {
        get => _selectedRamMb;
        set
        {
            if (_refreshing || value == _selectedRamMb || value <= 0)
            {
                return;
            }

            _selectedRamMb = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RamText));
            _ = UpdateSelectedProfileAsync(p => p.RamMb = value, $"{value / 1024} GB RAM selected");
        }
    }

    public JavaRuntimePreference SelectedJava
    {
        get => _selectedJava;
        set
        {
            if (_refreshing || value == _selectedJava)
            {
                return;
            }

            _selectedJava = value;
            OnPropertyChanged();
            _ = UpdateSelectedProfileAsync(p => p.JavaPreference = value, "Java runtime preference changed");
        }
    }

    public string RamText => _selectedRamMb > 0 ? $"{_selectedRamMb / 1024} GB" : "–";

    public string VersionFilter
    {
        get => _versionFilter;
        set
        {
            if (SetProperty(ref _versionFilter, value))
            {
                ApplyFilter();
            }
        }
    }

    public string GameDirectory { get => _gameDirectory; private set => SetProperty(ref _gameDirectory, value); }

    public string JavaTitle { get => _javaTitle; private set => SetProperty(ref _javaTitle, value); }

    public string JavaDetail { get => _javaDetail; private set => SetProperty(ref _javaDetail, value); }

    public string VersionSummary { get => _versionSummary; private set => SetProperty(ref _versionSummary, value); }

    public ICommand OpenGameDirectoryCommand { get; }

    public ICommand ManageProfilesCommand { get; }

    public ICommand ClearFilterCommand { get; }

    public override void OnNavigatedTo() => Refresh();

    private void Refresh()
    {
        _refreshing = true;
        try
        {
            Profiles.Clear();
            foreach (var profile in _profiles.Profiles)
            {
                Profiles.Add(profile);
            }

            var selected = _profiles.SelectedProfile;
            _selectedProfile = selected;
            _selectedVersion = selected is null ? null : _versions.Find(selected.MinecraftVersion);
            _selectedRamMb = selected?.RamMb ?? 0;
            _selectedJava = selected?.JavaPreference ?? JavaRuntimePreference.Automatic;
            GameDirectory = selected?.GameDirectory ?? string.Empty;
            ApplyFilterCore();
        }
        finally
        {
            _refreshing = false;
        }

        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(SelectedVersion));
        OnPropertyChanged(nameof(SelectedRamMb));
        OnPropertyChanged(nameof(SelectedJava));
        OnPropertyChanged(nameof(RamText));
        OnPropertyChanged(nameof(VersionSourceText));
        Game.RefreshState();
        UpdateJava();
    }

    private void ApplyFilter()
    {
        _refreshing = true;
        try
        {
            ApplyFilterCore();
        }
        finally
        {
            _refreshing = false;
        }

        OnPropertyChanged(nameof(SelectedVersion));
    }

    private void ApplyFilterCore()
    {
        FilteredVersions.Clear();
        foreach (var version in _versions.Filter(VersionFilter, _settings.Current.ShowSnapshots))
        {
            FilteredVersions.Add(version);
        }
    }

    private void UpdateJava()
    {
        var profile = _profiles.SelectedProfile;
        if (profile is null)
        {
            return;
        }

        var version = _versions.Find(profile.MinecraftVersion);
        if (!Game.IsDirectMode)
        {
            VersionSummary = version is null ? profile.MinecraftVersion : $"{version.TypeText} · {version.ReleaseDateText}";
            JavaTitle = "Handled by the official Minecraft Launcher";
            JavaDetail = "PLAY writes this profile (version, RAM, JVM arguments, game directory) into the official launcher and opens it. Sign in there.";
            return;
        }

        var required = version?.RequiredJavaMajor;
        int? major = profile.JavaPreference switch
        {
            JavaRuntimePreference.Java17 => 17,
            JavaRuntimePreference.Java21 => 21,
            _ => required,
        };

        VersionSummary = version is null
            ? $"{profile.MinecraftVersion} · not in the version list"
            : $"{version.TypeText} · {version.InstallStateText} · {version.JavaText}";

        if (major is null)
        {
            JavaTitle = "Managed by GOAT CLIENT · Automatic";
            JavaDetail = "The required Java version is read from the official version data during installation and installed automatically.";
            return;
        }

        JavaTitle = profile.JavaPreference == JavaRuntimePreference.Automatic
            ? $"Java {major} · Automatic · Managed by GOAT CLIENT"
            : $"Java {major} · Managed by GOAT CLIENT";

        var runtime = _java.FindManagedRuntime(major.Value);
        JavaDetail = runtime is not null
            ? $"● Ready – {runtime.DisplayName}"
            : $"○ Java {major} is required and will be installed automatically.";

        if (required is not null && major < required)
        {
            JavaDetail = $"Minecraft {profile.MinecraftVersion} requires Java {required}. Set the Java runtime to Automatic.";
        }
    }

    private async Task UpdateSelectedProfileAsync(Action<LauncherProfile> change, string successMessage)
    {
        var current = _profiles.SelectedProfile;
        if (current is null)
        {
            return;
        }

        var updated = current.Clone();
        change(updated);

        var error = _profiles.Validate(updated);
        if (error is not null)
        {
            _notifications.Show(NotificationKind.Warning, "Not saved", error);
            Refresh();
            return;
        }

        await RunSafeAsync(async () =>
        {
            await _profiles.UpdateAsync(updated);
            _status.Set(LauncherStatus.Ready, $"{successMessage} · saved to \"{updated.Name}\"");
        });
    }

    private async Task RunSafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.Error("Play page update failed.", ex);
            _notifications.Show(NotificationKind.Error, "Could not save", ex.Message);
            Refresh();
        }
    }

    private void OpenGameDirectory()
    {
        try
        {
            System.IO.Directory.CreateDirectory(GameDirectory);
            _shell.OpenFolder(GameDirectory);
        }
        catch (Exception ex)
        {
            _logger.Error("Could not open game directory.", ex);
            _notifications.Show(NotificationKind.Error, "Could not open folder", ex.Message);
        }
    }
}
