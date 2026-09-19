using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using GoatClient.Core;
using GoatClient.Core.Commands;
using GoatClient.Models;
using GoatClient.Services.Auth;
using GoatClient.Services.Dialogs;
using GoatClient.Services.Logging;
using GoatClient.Services.Navigation;
using GoatClient.Services.Notifications;
using GoatClient.Services.Platform;
using GoatClient.Services.Skins;

namespace GoatClient.ViewModels;

/// <summary>One skin in the local library.</summary>
public sealed class SkinItemViewModel : ObservableObject
{
    public SkinItemViewModel(SkinLibraryEntry entry, BitmapSource? front, ICommand apply, ICommand delete)
    {
        Entry = entry;
        Front = front;
        ApplyCommand = apply;
        DeleteCommand = delete;
    }

    public SkinLibraryEntry Entry { get; }

    public string Name => Entry.Name;

    public string ModelText => Entry.Slim ? "Slim (Alex)" : "Classic (Steve)";

    public BitmapSource? Front { get; }

    public ICommand ApplyCommand { get; }

    public ICommand DeleteCommand { get; }
}

/// <summary>
/// Skins page: current skin (signed-in account or linked Minecraft name), local skin library,
/// and skin changes via the Minecraft services API when signed in inside GOAT CLIENT.
/// </summary>
public sealed class SkinsViewModel : ViewModelBase
{
    public const string MinecraftNetSkinPage = "https://www.minecraft.net/msaprofile/mygames/editskin";

    private readonly PlayerIdentityService _identity;
    private readonly SkinTextureLoader _skins;
    private readonly SkinLibraryService _library;
    private readonly SkinUploadService _upload;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly IShellService _shell;
    private readonly ILogger _logger;

    private BitmapSource? _front;
    private BitmapSource? _face;
    private BitmapSource? _hat;
    private bool _isBusy;
    private bool _newSkinSlim;

    public SkinsViewModel(
        PlayerIdentityService identity,
        SkinTextureLoader skins,
        SkinLibraryService library,
        SkinUploadService upload,
        IDialogService dialogs,
        INotificationService notifications,
        IShellService shell,
        INavigationService navigation,
        ILogger logger)
    {
        _identity = identity;
        _skins = skins;
        _library = library;
        _upload = upload;
        _dialogs = dialogs;
        _notifications = notifications;
        _shell = shell;
        _logger = logger;

        OpenAccountCommand = new RelayCommand(() => navigation.Navigate(AppPage.Account));
        AddSkinCommand = new AsyncRelayCommand(AddSkinAsync, () => !IsBusy);
        SaveCurrentCommand = new AsyncRelayCommand(SaveCurrentAsync, () => !IsBusy && _identity.Current?.SkinUrl is not null);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy && _identity.Current is not null);
        ResetSkinCommand = new AsyncRelayCommand(ResetSkinAsync, () => !IsBusy && _upload.CanUpload);
        OpenSkinFolderCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(SkinLibraryService.Directory);
            _shell.OpenFolder(SkinLibraryService.Directory);
        });
        OpenMinecraftNetCommand = new RelayCommand(() => _shell.OpenUrl(MinecraftNetSkinPage));

        _identity.PropertyChanged += (_, _) => Refresh();
        _library.Changed += (_, _) => RebuildLibrary();
    }

    public ObservableCollection<SkinItemViewModel> Library { get; } = new();

    public bool HasProfile => _identity.Current is not null;

    public string Username => _identity.Current?.Username ?? string.Empty;

    public string SourceText => _identity.Source switch
    {
        IdentitySource.SignedIn => "Signed in with Microsoft in GOAT CLIENT",
        IdentitySource.LinkedName => "Linked Minecraft name (public profile – no login)",
        _ => string.Empty,
    };

    public string SkinModel => _identity.Current?.SkinVariant switch
    {
        "SLIM" => "Slim (Alex)",
        "CLASSIC" => "Classic (Steve)",
        _ => "Default skin",
    };

    /// <summary>Uploading needs the GOAT CLIENT sign-in (direct launch).</summary>
    public bool CanUpload => _upload.CanUpload;

    public string UploadHint => CanUpload
        ? "Apply uploads the skin to your Minecraft account."
        : "To change your skin from GOAT CLIENT, sign in with Microsoft here (direct launch). Without it, Apply opens minecraft.net and the skin folder so you can upload the file there.";

    public bool HasSkin => Front is not null;

    public bool IsLibraryEmpty => Library.Count == 0;

    public BitmapSource? Front { get => _front; private set { if (SetProperty(ref _front, value)) { OnPropertyChanged(nameof(HasSkin)); } } }

    public BitmapSource? Face { get => _face; private set => SetProperty(ref _face, value); }

    public BitmapSource? Hat { get => _hat; private set => SetProperty(ref _hat, value); }

    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) { RelayCommand.Refresh(); } } }

    /// <summary>Model used for the next skin that is added to the library.</summary>
    public bool NewSkinSlim { get => _newSkinSlim; set => SetProperty(ref _newSkinSlim, value); }

    public ICommand OpenAccountCommand { get; }

    public ICommand AddSkinCommand { get; }

    public ICommand SaveCurrentCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand ResetSkinCommand { get; }

    public ICommand OpenSkinFolderCommand { get; }

    public ICommand OpenMinecraftNetCommand { get; }

    public override void OnNavigatedTo()
    {
        Refresh();
        RebuildLibrary();
    }

    private async void Refresh()
    {
        OnPropertyChanged(nameof(HasProfile));
        OnPropertyChanged(nameof(Username));
        OnPropertyChanged(nameof(SourceText));
        OnPropertyChanged(nameof(SkinModel));
        OnPropertyChanged(nameof(CanUpload));
        OnPropertyChanged(nameof(UploadHint));
        RelayCommand.Refresh();

        var current = _identity.Current;
        try
        {
            var images = await _skins.LoadAsync(current?.SkinUrl, CancellationToken.None, current?.SkinVariant == "SLIM");
            Front = images?.Front;
            Face = images?.Face;
            Hat = images?.Hat;
        }
        catch (Exception ex)
        {
            _logger.Warning("Skin texture could not be loaded.", ex);
            Front = null;
        }
    }

    private void RebuildLibrary()
    {
        Library.Clear();
        foreach (var entry in _library.Skins)
        {
            BitmapSource? front = null;
            try
            {
                front = SkinTextureLoader.FromFile(_library.PathOf(entry), entry.Slim).Front;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Skin preview for '{entry.Name}' failed.", ex);
            }

            var captured = entry;
            Library.Add(new SkinItemViewModel(
                entry,
                front,
                new AsyncRelayCommand(() => ApplyAsync(captured), () => !IsBusy),
                new AsyncRelayCommand(() => DeleteAsync(captured), () => !IsBusy)));
        }

        OnPropertyChanged(nameof(IsLibraryEmpty));
    }

    private async Task AddSkinAsync()
    {
        var file = _dialogs.PickFile("Choose a Minecraft skin (PNG)", "Minecraft skin (*.png)|*.png");
        if (file is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _library.AddFileAsync(file, Path.GetFileNameWithoutExtension(file), NewSkinSlim);
            _notifications.Show(NotificationKind.Success, "Skin added", Path.GetFileName(file));
        });
    }

    private async Task SaveCurrentAsync()
    {
        var current = _identity.Current;
        if (current?.SkinUrl is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var bytes = await _skins.DownloadAsync(current.SkinUrl, CancellationToken.None);
            await _library.AddBytesAsync(bytes, $"{current.Username} ({DateTime.Now:yyyy-MM-dd})", current.SkinVariant == "SLIM");
            _notifications.Show(NotificationKind.Success, "Current skin saved", "Added to your skin library.");
        });
    }

    private async Task RefreshAsync()
    {
        await RunAsync(async () =>
        {
            if (_identity.Source == IdentitySource.LinkedName)
            {
                await _identity.RefreshLinkedAsync(CancellationToken.None);
            }

            Refresh();
        });
    }

    private async Task ApplyAsync(SkinLibraryEntry entry)
    {
        if (!_upload.CanUpload)
        {
            // Honest fallback: GOAT CLIENT has no access to the account without its own sign-in.
            _shell.OpenFolder(SkinLibraryService.Directory);
            _shell.OpenUrl(MinecraftNetSkinPage);
            _notifications.Show(NotificationKind.Info, "Upload on minecraft.net",
                $"Choose the file {entry.FileName} from the opened folder on the minecraft.net skin page.");
            return;
        }

        await RunAsync(async () =>
        {
            await _upload.UploadAsync(_library.PathOf(entry), entry.Slim, CancellationToken.None);
            _notifications.Show(NotificationKind.Success, "Skin changed", $"'{entry.Name}' is now your Minecraft skin.");
        });
    }

    private async Task DeleteAsync(SkinLibraryEntry entry)
    {
        if (!await _dialogs.ConfirmAsync("Delete skin?", $"'{entry.Name}' will be removed from your skin library.", "Delete", "Cancel", isDanger: true))
        {
            return;
        }

        await RunAsync(() => _library.RemoveAsync(entry));
    }

    private async Task ResetSkinAsync()
    {
        if (!await _dialogs.ConfirmAsync("Reset skin?", "Your Minecraft account will use the default skin again.", "Reset", "Cancel", isDanger: true))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _upload.ResetAsync(CancellationToken.None);
            _notifications.Show(NotificationKind.Success, "Skin reset", "Your account uses the default skin now.");
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.Error("Skin action failed.", ex);
            _notifications.Show(NotificationKind.Error, "Skin action failed", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
