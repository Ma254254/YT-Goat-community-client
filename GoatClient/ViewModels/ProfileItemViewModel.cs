using System.Windows.Input;
using GoatClient.Core;
using GoatClient.Models;

namespace GoatClient.ViewModels;

/// <summary>Display wrapper for one profile card.</summary>
public sealed class ProfileItemViewModel : ObservableObject
{
    public ProfileItemViewModel(LauncherProfile profile, bool isSelected, bool isDefault, ProfilesViewModel owner)
    {
        Profile = profile;
        IsSelected = isSelected;
        IsDefault = isDefault;
        SelectCommand = owner.SelectProfileCommand;
        EditCommand = owner.EditProfileCommand;
        DuplicateCommand = owner.DuplicateProfileCommand;
        DeleteCommand = owner.DeleteProfileCommand;
    }

    public LauncherProfile Profile { get; }

    public string Name => Profile.Name;

    public string Initial => string.IsNullOrEmpty(Profile.Name) ? "?" : Profile.Name[..1].ToUpperInvariant();

    public string VersionText => $"Minecraft {Profile.MinecraftVersion}";

    public string RamText => $"{Profile.RamMb / 1024} GB";

    public string JavaText => Profile.JavaPreference switch
    {
        JavaRuntimePreference.Java17 => "Java 17",
        JavaRuntimePreference.Java21 => "Java 21",
        _ => "Automatic",
    };

    public string GameDirectory => Profile.GameDirectory;

    public bool HasJvmArguments => !string.IsNullOrWhiteSpace(Profile.JvmArguments);

    public bool IsSelected { get; }

    public bool IsDefault { get; }

    public ICommand SelectCommand { get; }

    public ICommand EditCommand { get; }

    public ICommand DuplicateCommand { get; }

    public ICommand DeleteCommand { get; }
}
