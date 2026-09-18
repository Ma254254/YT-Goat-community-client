using System.Collections.ObjectModel;
using System.Windows.Input;
using GoatClient.Core.Commands;
using GoatClient.Models;
using GoatClient.Services.Dialogs;
using GoatClient.Services.Java;
using GoatClient.Services.Logging;
using GoatClient.Services.Minecraft;
using GoatClient.Services.Navigation;
using GoatClient.Services.Notifications;
using GoatClient.Services.Platform;
using GoatClient.Services.Profiles;
using GoatClient.Services.Status;

namespace GoatClient.ViewModels;

/// <summary>Play page: choose profile, version and RAM. Changes are saved to the selected profile.</summary>
public sealed class PlayViewModel : ViewModelBase
{
    private readonly IProfileService _profiles;
    private readonly IMinecraftVersionService _versions;
    private readonly IJavaService _java;
    private readonly ISystemInfoService _systemInfo;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly IShellService _shell;
    private readonly IStatusService _status;
    private readonly ILogger _logger;

    private bool _refreshing;
    private LauncherProfile? _selectedProfile;
    private MinecraftVersionInfo? _selectedVersion;
    private int _selectedRamMb;
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
        IDialogService dialogs,
        INotificationService notifications,
        IShellService shell,
        IStatusService status,
        INavigationService navigation,
        ILogger logger)
    {
        _profiles = profiles;
        _versions = versions;
        _java = java;
        _systemInfo = systemInfo;
        _dialogs = dialogs;
        _notifications = notifications;
        _shell = shell;
        _status = status;
        _logger = logger;

        RamOptions = systemInfo.GetRamOptions();
        PlayMinecraftCommand = new AsyncRelayCommand(ShowLaunchNotAvailableAsync);
        OpenGameDirectoryCommand = new RelayCommand(OpenGameDirectory);
        ManageProfilesCommand = new RelayCommand(() => navigation.Navigate(AppPage.Profiles));
        ClearFilterCommand = new RelayCommand(() => VersionFilter = string.Empty);

        _profiles.ProfilesChanged += (_, _) => Refresh();
        _versions.VersionsChanged += (_, _) => Refresh();
        _java.RuntimesChanged += (_, _) => UpdateJava();
    }

    public ObservableCollection<LauncherProfile> Profiles { get; } = new();

    public ObservableCollection<MinecraftVersionInfo> FilteredVersions { get; } = new();

    public IReadOnlyList<RamOption> RamOptions { get; }

    public string MemorySummary => _systemInfo.MemorySummary;

    public string VersionSourceText => _versions.UsesDevelopmentData
        ? "Local development data · versions are not installed yet"
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
            _ = UpdateSelectedProfileAsync(p => p.MinecraftVersion = value.Id, $"Version {value.Id} selected");
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

    public ICommand PlayMinecraftCommand { get; }

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
        OnPropertyChanged(nameof(RamText));
        OnPropertyChanged(nameof(VersionSourceText));
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
        foreach (var version in _versions.Filter(VersionFilter))
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
        var major = _java.ResolveTargetMajor(profile.JavaPreference, version);
        var runtime = _java.FindManagedRuntime(major);

        VersionSummary = version is null
            ? $"{profile.MinecraftVersion} · unknown version"
            : $"{version.TypeText} · {version.InstallStateText} · requires Java {version.RequiredJavaMajor}";

        JavaTitle = profile.JavaPreference == JavaRuntimePreference.Automatic
            ? $"Managed by GOAT CLIENT · Automatic (Java {major})"
            : $"Managed by GOAT CLIENT · Java {major}";

        JavaDetail = runtime is not null
            ? $"Ready: {runtime.DisplayName}"
            : "Ready for future Minecraft installation – the runtime will be provided automatically. No separate Java installation required.";

        if (version is not null && major < version.RequiredJavaMajor)
        {
            JavaDetail = $"Warning: Minecraft {version.Id} requires Java {version.RequiredJavaMajor}. Set the profile's Java preference to Automatic.";
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
            if (!_shell.OpenFolder(GameDirectory))
            {
                _notifications.Show(NotificationKind.Warning, "Folder not found", $"The game directory does not exist yet:{Environment.NewLine}{GameDirectory}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Could not open game directory.", ex);
            _notifications.Show(NotificationKind.Error, "Could not open folder", ex.Message);
        }
    }

    private Task ShowLaunchNotAvailableAsync()
        => _dialogs.ShowInfoAsync(
            "Launch not available yet",
            "Minecraft launching will be available in a future phase." + Environment.NewLine + Environment.NewLine
            + "Your selection (profile, version and RAM) is already saved and will be used once launching is implemented.");
}
