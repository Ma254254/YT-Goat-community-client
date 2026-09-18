using System.Windows.Input;
using System.Windows.Media.Imaging;
using GoatClient.Core.Commands;
using GoatClient.Models;
using GoatClient.Services.Auth;
using GoatClient.Services.Logging;
using GoatClient.Services.Navigation;

namespace GoatClient.ViewModels;

/// <summary>Shows the real skin of the signed-in account. Upload/management follows in a later phase.</summary>
public sealed class SkinsViewModel : ViewModelBase
{
    private readonly IAuthService _auth;
    private readonly SkinTextureLoader _skins;
    private readonly ILogger _logger;
    private BitmapSource? _texture;
    private BitmapSource? _face;
    private BitmapSource? _hat;

    public SkinsViewModel(IAuthService auth, SkinTextureLoader skins, INavigationService navigation, ILogger logger)
    {
        _auth = auth;
        _skins = skins;
        _logger = logger;
        OpenAccountCommand = new RelayCommand(() => navigation.Navigate(AppPage.Account));
        _auth.PropertyChanged += (_, _) => Refresh();
    }

    public bool IsSignedIn => _auth.Account is not null;

    public string Username => _auth.Account?.Username ?? string.Empty;

    public string SkinModel => _auth.Account?.SkinVariant switch
    {
        "SLIM" => "Slim (Alex)",
        "CLASSIC" => "Classic (Steve)",
        _ => "Unknown",
    };

    public bool HasSkin => SkinTexture is not null;

    public BitmapSource? SkinTexture { get => _texture; private set { if (SetProperty(ref _texture, value)) { OnPropertyChanged(nameof(HasSkin)); } } }

    public BitmapSource? Face { get => _face; private set => SetProperty(ref _face, value); }

    public BitmapSource? Hat { get => _hat; private set => SetProperty(ref _hat, value); }

    public IReadOnlyList<string> PlannedFeatures { get; } =
    [
        "Upload skins (classic and slim model)",
        "Local skin library for quick switching",
        "3D preview",
    ];

    public ICommand OpenAccountCommand { get; }

    public override void OnNavigatedTo() => Refresh();

    private async void Refresh()
    {
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(Username));
        OnPropertyChanged(nameof(SkinModel));
        try
        {
            var images = await _skins.LoadAsync(_auth.Account?.SkinUrl, CancellationToken.None);
            SkinTexture = images?.Texture;
            Face = images?.Face;
            Hat = images?.Hat;
        }
        catch (Exception ex)
        {
            _logger.Warning("Skin texture could not be loaded.", ex);
        }
    }
}
