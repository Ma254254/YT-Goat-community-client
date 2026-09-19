using System.ComponentModel;
using System.Windows.Input;
using GoatClient.Core.Commands;
using GoatClient.Core.Constants;
using GoatClient.Models;
using GoatClient.Services.Auth;
using GoatClient.Services.Java;
using GoatClient.Services.Minecraft;
using GoatClient.Services.Navigation;
using GoatClient.Services.Profiles;
using GoatClient.Services.Skins;
using GoatClient.Services.Settings;
using GoatClient.Services.Status;

namespace GoatClient.ViewModels;

public sealed class HomeViewModel : ViewModelBase
{
    private readonly IProfileService _profiles;
    private readonly ISettingsService _settings;
    private readonly IMinecraftVersionService _versions;
    private readonly IJavaService _java;
    private readonly IAuthService _auth;
    private readonly PlayerIdentityService _identity;

    private string _versionText = "–";
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
        IAuthService auth,
        IStatusService status,
        GameController game,
        INavigationService navigation,
        PlayerIdentityService identity)
    {
        _identity = identity;
        _profiles = profiles;
        _settings = settings;
        _versions = versions;
        _java = java;
        _auth = auth;
        Status = status;
        Game = game;

        OpenPlayCommand = new RelayCommand(() => navigation.Navigate(AppPage.Play));
        OpenProfilesCommand = new RelayCommand(() => navigation.Navigate(AppPage.Profiles));
        OpenAccountCommand = new RelayCommand(() => navigation.Navigate(AppPage.Account));

        _profiles.ProfilesChanged += (_, _) => Refresh();
        _settings.SettingsChanged += (_, _) => Refresh();
        _java.RuntimesChanged += (_, _) => Refresh();
        _versions.VersionsChanged += (_, _) => Refresh();
        _auth.PropertyChanged += (_, _) => Refresh();
        _identity.PropertyChanged += (_, _) => Refresh();
    }

    public IStatusService Status { get; }

    public GameController Game { get; }

    /// <summary>"Welcome back, {real Minecraft name}" – or plain "Welcome back" when signed out.</summary>
    public string Greeting => _identity.Current is { } account ? $"Welcome back, {account.Username}" : "Welcome back";

    public bool IsSignedIn => _auth.State == AuthState.SignedIn;

    /// <summary>Direct launch needs a GOAT CLIENT sign-in; the official launcher handles its own.</summary>
    public bool ShowSignInHint => Game.IsDirectMode && !IsSignedIn;

    public bool ShowOfficialHint => !Game.IsDirectMode;

    public string ProductName => AppInfo.ProductName;

    public string Tagline => AppInfo.Tagline;

    public string VersionText { get => _versionText; private set => SetProperty(ref _versionText, value); }

    public string ProfileName { get => _profileName; private set => SetProperty(ref _profileName, value); }

    public string RamText { get => _ramText; private set => SetProperty(ref _ramText, value); }

    public string JavaTitle { get => _javaTitle; private set => SetProperty(ref _javaTitle, value); }

    public string JavaDetail { get => _javaDetail; private set => SetProperty(ref _javaDetail, value); }

    public bool ShowLogo { get => _showLogo; private set => SetProperty(ref _showLogo, value); }

    public ICommand OpenPlayCommand { get; }

    public ICommand OpenProfilesCommand { get; }

    public ICommand OpenAccountCommand { get; }

    public override void OnNavigatedTo() => Refresh();

    private void Refresh()
    {
        OnPropertyChanged(nameof(Greeting));
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(ShowSignInHint));
        OnPropertyChanged(nameof(ShowOfficialHint));
        Game.RefreshState();

        var profile = _profiles.SelectedProfile;
        if (profile is null)
        {
            return;
        }

        ProfileName = profile.Name;
        VersionText = profile.MinecraftVersion;
        RamText = $"{profile.RamMb / 1024} GB";

        if (!Game.IsDirectMode)
        {
            JavaTitle = "Handled by the official Minecraft Launcher";
            JavaDetail = "Sign-in, downloads and Java are managed there. RAM and JVM arguments are applied to your GOAT CLIENT profile.";
            return;
        }

        var required = _versions.Find(profile.MinecraftVersion)?.RequiredJavaMajor;
        int? major = profile.JavaPreference switch
        {
            JavaRuntimePreference.Java17 => 17,
            JavaRuntimePreference.Java21 => 21,
            _ => required,
        };

        if (major is null)
        {
            JavaTitle = "Managed by GOAT CLIENT · Automatic";
            JavaDetail = "The required Java version is determined from the official version data when Minecraft is installed.";
            return;
        }

        var runtime = _java.FindManagedRuntime(major.Value);
        JavaTitle = $"Java {major} · Managed by GOAT CLIENT";
        JavaDetail = runtime is not null
            ? $"● Ready – {runtime.DisplayName}"
            : $"Java {major} is required and will be installed automatically. No separate Java installation needed.";
    }
}
