using System.Windows.Input;
using GoatClient.Core;
using GoatClient.Core.Commands;
using GoatClient.Models;
using GoatClient.Services.Dialogs;
using GoatClient.Services.Java;
using GoatClient.Services.Minecraft;

namespace GoatClient.ViewModels;

public sealed record JavaPreferenceOption(JavaRuntimePreference Value, string Label);

/// <summary>Create / edit form shown as overlay on the Profiles page.</summary>
public sealed class ProfileEditorViewModel : ObservableObject
{
    private readonly LauncherProfile _draft;
    private readonly IMinecraftVersionService _versions;
    private readonly IJavaService _java;
    private readonly IDialogService _dialogs;
    private readonly Func<LauncherProfile, Task<string?>> _save;

    private string _name;
    private string _minecraftVersion;
    private int _ramMb;
    private JavaRuntimePreference _javaPreference;
    private string _gameDirectory;
    private string _jvmArguments;
    private string? _errorMessage;

    public ProfileEditorViewModel(
        LauncherProfile draft,
        bool isNew,
        IReadOnlyList<RamOption> ramOptions,
        IMinecraftVersionService versions,
        IJavaService java,
        IDialogService dialogs,
        Func<LauncherProfile, Task<string?>> save,
        Action close)
    {
        _draft = draft.Clone();
        _versions = versions;
        _java = java;
        _dialogs = dialogs;
        _save = save;
        IsNew = isNew;
        RamOptions = ramOptions;
        var list = versions.Filter(null).ToList();
        if (versions.Find(draft.MinecraftVersion) is { } current && !list.Contains(current))
        {
            list.Insert(0, current);
        }

        Versions = list;

        _name = draft.Name;
        _minecraftVersion = draft.MinecraftVersion;
        _ramMb = draft.RamMb;
        _javaPreference = draft.JavaPreference;
        _gameDirectory = draft.GameDirectory;
        _jvmArguments = draft.JvmArguments;

        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CancelCommand = new RelayCommand(close);
        BrowseGameDirectoryCommand = new RelayCommand(BrowseGameDirectory);
    }

    public bool IsNew { get; }

    public string Title => IsNew ? "Create profile" : "Edit profile";

    public string SaveText => IsNew ? "Create profile" : "Save changes";

    public IReadOnlyList<MinecraftVersionInfo> Versions { get; }

    public IReadOnlyList<RamOption> RamOptions { get; }

    public IReadOnlyList<JavaPreferenceOption> JavaPreferences { get; } =
    [
        new(JavaRuntimePreference.Automatic, "Automatic (recommended)"),
        new(JavaRuntimePreference.Java21, "Java 21 (managed)"),
        new(JavaRuntimePreference.Java17, "Java 17 (managed)"),
    ];

    public string Name { get => _name; set => SetField(ref _name, value); }

    public string MinecraftVersion
    {
        get => _minecraftVersion;
        set
        {
            if (value is not null && SetField(ref _minecraftVersion, value))
            {
                OnPropertyChanged(nameof(JavaHint));
            }
        }
    }

    public int RamMb
    {
        get => _ramMb;
        set
        {
            if (value > 0)
            {
                SetField(ref _ramMb, value);
            }
        }
    }

    public JavaRuntimePreference JavaPreference
    {
        get => _javaPreference;
        set
        {
            if (SetField(ref _javaPreference, value))
            {
                OnPropertyChanged(nameof(JavaHint));
            }
        }
    }

    public string GameDirectory { get => _gameDirectory; set => SetField(ref _gameDirectory, value); }

    public string JvmArguments { get => _jvmArguments; set => SetField(ref _jvmArguments, value); }

    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }

    public string JavaHint
    {
        get
        {
            var required = _versions.Find(MinecraftVersion)?.RequiredJavaMajor;
            int? major = JavaPreference switch
            {
                JavaRuntimePreference.Java17 => 17,
                JavaRuntimePreference.Java21 => 21,
                _ => required,
            };

            if (major is null)
            {
                return "Automatic: the required Java version is read from the official Minecraft version data during installation. No separate Java installation required.";
            }

            if (required is not null && major < required)
            {
                return $"Minecraft {MinecraftVersion} requires Java {required}. Java {major} will not work for this version.";
            }

            return _java.FindManagedRuntime(major.Value) is not null
                ? $"Uses Java {major}, managed by GOAT CLIENT · Ready."
                : $"Uses Java {major}, managed by GOAT CLIENT · installed automatically when needed.";
        }
    }

    public ICommand SaveCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand BrowseGameDirectoryCommand { get; }

    private bool SetField<T>(ref T field, T value, [global::System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!SetProperty(ref field, value, name))
        {
            return false;
        }

        ErrorMessage = null;
        return true;
    }

    private async Task SaveAsync()
    {
        var profile = _draft.Clone();
        profile.Name = Name?.Trim() ?? string.Empty;
        profile.MinecraftVersion = MinecraftVersion;
        profile.RamMb = RamMb;
        profile.JavaPreference = JavaPreference;
        profile.GameDirectory = GameDirectory?.Trim() ?? string.Empty;
        profile.JvmArguments = JvmArguments?.Trim() ?? string.Empty;

        ErrorMessage = await _save(profile);
    }

    private void BrowseGameDirectory()
    {
        var folder = _dialogs.PickFolder(GameDirectory, "Choose the game directory");
        if (folder is not null)
        {
            GameDirectory = folder;
        }
    }
}
