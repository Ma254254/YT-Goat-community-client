using System.Windows.Input;
using GoatClient.Core.Commands;
using GoatClient.Core.Constants;
using GoatClient.Models;
using GoatClient.Services.Dialogs;
using GoatClient.Services.Java;
using GoatClient.Services.Minecraft;
using GoatClient.Services.Navigation;
using GoatClient.Services.Profiles;
using GoatClient.Services.Settings;
using GoatClient.Services.Status;

namespace GoatClient.ViewModels;

public sealed class HomeViewModel : ViewModelBase
{
    private readonly IProfileService _profiles;
    private readonly ISettingsService _settings;
    private readonly IMinecraftVersionService _versions;
    private readonly IJavaService _java;
    private readonly IDialogService _dialogs;

    private string _versionText = "–";
    private string _versionState = string.Empty;
    private string _profileName = "–";
    private string _ramText = "–";
    private string _javaTitle = string.Empty;
    private string _javaDetail = string.Empty;
    private bool _showLogo = true;

    public HomeViewModel(
        IProfileService profiles,
        ISettingsService settings,
        IMinecraftVersionService versions,
        IJavaService java,
        IStatusService status,
        IDialogService dialogs,
        INavigationService navigation)
    {
        _profiles = profiles;
        _settings = settings;
        _versions = versions;
        _java = java;
        _dialogs = dialogs;
        Status = status;

        PlayCommand = new AsyncRelayCommand(ShowLaunchNotAvailableAsync);
        OpenPlayCommand = new RelayCommand(() => navigation.Navigate(AppPage.Play));
        OpenProfilesCommand = new RelayCommand(() => navigation.Navigate(AppPage.Profiles));

        _profiles.ProfilesChanged += (_, _) => Refresh();
        _settings.SettingsChanged += (_, _) => Refresh();
        _java.RuntimesChanged += (_, _) => Refresh();
        _versions.VersionsChanged += (_, _) => Refresh();
    }

    public IStatusService Status { get; }

    public string Greeting => "Welcome back";

    public string ProductName => AppInfo.ProductName;

    public string Tagline => AppInfo.Tagline;

    public string VersionText { get => _versionText; private set => SetProperty(ref _versionText, value); }

    public string VersionState { get => _versionState; private set => SetProperty(ref _versionState, value); }

    public string ProfileName { get => _profileName; private set => SetProperty(ref _profileName, value); }

    public string RamText { get => _ramText; private set => SetProperty(ref _ramText, value); }

    public string JavaTitle { get => _javaTitle; private set => SetProperty(ref _javaTitle, value); }

    public string JavaDetail { get => _javaDetail; private set => SetProperty(ref _javaDetail, value); }

    public bool ShowLogo { get => _showLogo; private set => SetProperty(ref _showLogo, value); }

    public ICommand PlayCommand { get; }

    public ICommand OpenPlayCommand { get; }

    public ICommand OpenProfilesCommand { get; }

    public override void OnNavigatedTo() => Refresh();

    private void Refresh()
    {
        ShowLogo = _settings.Current.ShowHomeLogo;
        var profile = _profiles.SelectedProfile;
        if (profile is null)
        {
            return;
        }

        var version = _versions.Find(profile.MinecraftVersion);
        ProfileName = profile.Name;
        VersionText = profile.MinecraftVersion;
        VersionState = version is null ? "Unknown version" : version.InstallStateText;
        RamText = $"{profile.RamMb / 1024} GB";

        var major = _java.ResolveTargetMajor(profile.JavaPreference, version);
        var runtime = _java.FindManagedRuntime(major);
        JavaTitle = $"Java {major} · Managed by GOAT CLIENT";
        JavaDetail = runtime is not null
            ? $"{runtime.DisplayName} is available in the GOAT CLIENT runtime folder."
            : "No separate Java installation needed. GOAT CLIENT will provide this runtime automatically in a future phase.";
    }

    private Task ShowLaunchNotAvailableAsync()
        => _dialogs.ShowInfoAsync(
            "Launch not available yet",
            "Minecraft launching will be available in a future phase." + Environment.NewLine + Environment.NewLine
            + "Phase 1 provides the launcher foundation: profiles, versions, memory settings and the managed Java runtime architecture.");
}
