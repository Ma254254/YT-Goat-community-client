using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using GoatClient.Core.Commands;
using GoatClient.Models;
using GoatClient.Services.Auth;
using GoatClient.Services.Dialogs;
using GoatClient.Services.Logging;
using GoatClient.Services.Navigation;
using GoatClient.Services.Notifications;
using GoatClient.Services.Platform;

namespace GoatClient.ViewModels;

/// <summary>Account page: real Microsoft sign-in (device code), real Minecraft profile, logout.</summary>
public sealed class AccountViewModel : ViewModelBase
{
    private readonly IAuthService _auth;
    private readonly IShellService _shell;
    private readonly SkinTextureLoader _skins;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly ILogger _logger;

    private CancellationTokenSource? _signIn;
    private bool _isSigningIn;
    private string? _userCode;
    private string? _verificationUri;
    private string _signInStatus = string.Empty;
    private BitmapSource? _face;
    private BitmapSource? _hat;
    private BitmapSource? _texture;

    public AccountViewModel(
        IAuthService auth,
        IShellService shell,
        SkinTextureLoader skins,
        IDialogService dialogs,
        INotificationService notifications,
        INavigationService navigation,
        ILogger logger)
    {
        _auth = auth;
        _shell = shell;
        _skins = skins;
        _dialogs = dialogs;
        _notifications = notifications;
        _logger = logger;

        LoginCommand = new AsyncRelayCommand(SignInAsync, () => !IsSigningIn);
        CancelLoginCommand = new RelayCommand(() => _signIn?.Cancel(), () => IsSigningIn);
        CopyCodeCommand = new RelayCommand(CopyCode, () => UserCode is not null);
        OpenVerificationPageCommand = new RelayCommand(OpenVerificationPage, () => VerificationUri is not null);
        LogoutCommand = new AsyncRelayCommand(SignOutAsync, () => _auth.Account is not null && !IsSigningIn);
        OpenSettingsCommand = new RelayCommand(() => navigation.Navigate(AppPage.Settings));

        _auth.PropertyChanged += (_, _) => Refresh();
    }

    public bool IsConfigured => _auth.IsConfigured;

    public bool IsSignedIn => _auth.State == AuthState.SignedIn && _auth.Account is not null;

    public bool IsExpired => _auth.State == AuthState.SessionExpired;

    public bool IsSignedOut => !IsSignedIn;

    public string Username => _auth.Account?.Username ?? string.Empty;

    public string Uuid => _auth.Account?.FormattedUuid ?? string.Empty;

    public string SkinModel => _auth.Account?.SkinVariant switch
    {
        "SLIM" => "Slim (Alex)",
        "CLASSIC" => "Classic (Steve)",
        _ => "Unknown",
    };

    public string StatusText => _auth.State switch
    {
        AuthState.SignedIn => "● Connected",
        AuthState.SessionExpired => "Session expired – please sign in again",
        _ => "Not connected",
    };

    public bool IsSigningIn { get => _isSigningIn; private set { if (SetProperty(ref _isSigningIn, value)) { RelayCommand.Refresh(); } } }

    public string? UserCode { get => _userCode; private set => SetProperty(ref _userCode, value); }

    public string? VerificationUri { get => _verificationUri; private set => SetProperty(ref _verificationUri, value); }

    public string SignInStatus { get => _signInStatus; private set => SetProperty(ref _signInStatus, value); }

    public BitmapSource? Face { get => _face; private set => SetProperty(ref _face, value); }

    public BitmapSource? Hat { get => _hat; private set => SetProperty(ref _hat, value); }

    public BitmapSource? SkinTexture { get => _texture; private set => SetProperty(ref _texture, value); }

    public ICommand LoginCommand { get; }

    public ICommand CancelLoginCommand { get; }

    public ICommand CopyCodeCommand { get; }

    public ICommand OpenVerificationPageCommand { get; }

    public ICommand LogoutCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public override void OnNavigatedTo() => Refresh();

    private void Refresh()
    {
        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(IsSignedOut));
        OnPropertyChanged(nameof(IsExpired));
        OnPropertyChanged(nameof(Username));
        OnPropertyChanged(nameof(Uuid));
        OnPropertyChanged(nameof(SkinModel));
        OnPropertyChanged(nameof(StatusText));
        RelayCommand.Refresh();
        _ = LoadSkinAsync();
    }

    private async Task LoadSkinAsync()
    {
        try
        {
            var images = await _skins.LoadAsync(_auth.Account?.SkinUrl, CancellationToken.None);
            Face = images?.Face;
            Hat = images?.Hat;
            SkinTexture = images?.Texture;
        }
        catch (Exception ex)
        {
            _logger.Warning("Skin texture could not be loaded.", ex);
        }
    }

    private async Task SignInAsync()
    {
        if (!_auth.IsConfigured)
        {
            if (await _dialogs.ConfirmAsync(
                    "Microsoft sign-in not configured",
                    "To sign in, GOAT CLIENT needs the application (client) ID of your Azure app registration that is approved for Minecraft. Enter it in Settings → Launcher. See README → Microsoft Authentication.",
                    "Open Settings",
                    "Close"))
            {
                OpenSettingsCommand.Execute(null);
            }

            return;
        }

        _signIn = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        IsSigningIn = true;
        SignInStatus = "Requesting sign-in code…";
        try
        {
            var code = await _auth.StartDeviceLoginAsync(_signIn.Token);
            UserCode = code.UserCode;
            VerificationUri = code.VerificationUri;
            SignInStatus = "Enter the code on the Microsoft page and approve the sign-in. Waiting…";
            CopyCode();
            OpenVerificationPage();

            await _auth.CompleteDeviceLoginAsync(code, _signIn.Token);
            _notifications.Show(NotificationKind.Success, "Signed in", $"Welcome, {_auth.Account?.Username}!");
        }
        catch (OperationCanceledException)
        {
            SignInStatus = string.Empty;
        }
        catch (AuthException ex)
        {
            _logger.Warning($"Sign-in failed: {ex.Message}");
            await _dialogs.ShowInfoAsync("Sign-in failed", ex.Message, "Close");
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            _logger.Warning("Sign-in failed (network).", ex);
            await _dialogs.ShowInfoAsync("Sign-in failed", "Microsoft or Xbox services could not be reached. Check your internet connection.", "Close");
        }
        finally
        {
            IsSigningIn = false;
            UserCode = null;
            VerificationUri = null;
            SignInStatus = string.Empty;
            _signIn.Dispose();
            _signIn = null;
        }
    }

    private async Task SignOutAsync()
    {
        if (!await _dialogs.ConfirmAsync("Log out?", "Your Microsoft sign-in will be removed from this PC (Windows Credential Manager). Installed Minecraft versions stay installed.", "Log out", "Cancel", isDanger: true))
        {
            return;
        }

        await _auth.SignOutAsync();
        Face = null;
        Hat = null;
        SkinTexture = null;
        _notifications.Show(NotificationKind.Info, "Logged out", "Your sign-in was removed from this PC.");
    }

    private void CopyCode()
    {
        if (UserCode is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(UserCode);
        }
        catch (Exception ex)
        {
            _logger.Warning("Could not copy the sign-in code to the clipboard.", ex);
        }
    }

    private void OpenVerificationPage()
    {
        if (VerificationUri is null)
        {
            return;
        }

        try
        {
            _shell.OpenUrl(VerificationUri);
        }
        catch (Exception ex)
        {
            _logger.Warning("Could not open the Microsoft sign-in page.", ex);
            _notifications.Show(NotificationKind.Warning, "Open this page", VerificationUri);
        }
    }
}
