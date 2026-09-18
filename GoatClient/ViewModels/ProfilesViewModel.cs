using System.Collections.ObjectModel;
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

/// <summary>Profiles page: create, edit, delete, duplicate and select profiles.</summary>
public sealed class ProfilesViewModel : ViewModelBase
{
    private readonly IProfileService _profiles;
    private readonly ISettingsService _settings;
    private readonly IMinecraftVersionService _versions;
    private readonly IJavaService _java;
    private readonly ISystemInfoService _systemInfo;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly ILogger _logger;
    private ProfileEditorViewModel? _editor;

    public ProfilesViewModel(
        IProfileService profiles,
        ISettingsService settings,
        IMinecraftVersionService versions,
        IJavaService java,
        ISystemInfoService systemInfo,
        IDialogService dialogs,
        INotificationService notifications,
        ILogger logger)
    {
        _profiles = profiles;
        _settings = settings;
        _versions = versions;
        _java = java;
        _systemInfo = systemInfo;
        _dialogs = dialogs;
        _notifications = notifications;
        _logger = logger;

        CreateProfileCommand = new RelayCommand(OpenCreateEditor);
        EditProfileCommand = new RelayCommand(p => OpenEditEditor(p as ProfileItemViewModel), p => p is ProfileItemViewModel);
        DeleteProfileCommand = new AsyncRelayCommand(p => DeleteAsync(p as ProfileItemViewModel), p => p is ProfileItemViewModel && _profiles.Profiles.Count > 1);
        DuplicateProfileCommand = new AsyncRelayCommand(p => DuplicateAsync(p as ProfileItemViewModel), p => p is ProfileItemViewModel);
        SelectProfileCommand = new AsyncRelayCommand(p => SelectAsync(p as ProfileItemViewModel), p => p is ProfileItemViewModel { IsSelected: false });

        _profiles.ProfilesChanged += (_, _) => Refresh();
        _settings.SettingsChanged += (_, _) => Refresh();
    }

    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = new();

    public ProfileEditorViewModel? Editor
    {
        get => _editor;
        private set
        {
            if (SetProperty(ref _editor, value))
            {
                OnPropertyChanged(nameof(IsEditorOpen));
            }
        }
    }

    public bool IsEditorOpen => Editor is not null;

    public string CountText => Profiles.Count == 1 ? "1 profile" : $"{Profiles.Count} profiles";

    public ICommand CreateProfileCommand { get; }

    public ICommand EditProfileCommand { get; }

    public ICommand DeleteProfileCommand { get; }

    public ICommand DuplicateProfileCommand { get; }

    public ICommand SelectProfileCommand { get; }

    public override void OnNavigatedTo() => Refresh();

    public override void OnNavigatedFrom() => Editor = null;

    private void Refresh()
    {
        var selectedId = _profiles.SelectedProfile?.Id;
        var defaultId = _settings.Current.DefaultProfileId;

        Profiles.Clear();
        foreach (var profile in _profiles.Profiles)
        {
            Profiles.Add(new ProfileItemViewModel(profile, profile.Id == selectedId, profile.Id == defaultId, this));
        }

        OnPropertyChanged(nameof(CountText));
        RelayCommand.Refresh();
    }

    private void OpenCreateEditor() => Editor = CreateEditor(_profiles.CreateDraft(), isNew: true);

    private void OpenEditEditor(ProfileItemViewModel? item)
    {
        if (item is not null)
        {
            Editor = CreateEditor(item.Profile, isNew: false);
        }
    }

    private ProfileEditorViewModel CreateEditor(LauncherProfile profile, bool isNew)
        => new(profile, isNew, _systemInfo.GetRamOptions(), _versions, _java, _dialogs, p => SaveEditorAsync(p, isNew), () => Editor = null);

    /// <summary>Returns an error for the editor, or null on success (editor closes).</summary>
    private async Task<string?> SaveEditorAsync(LauncherProfile profile, bool isNew)
    {
        var error = _profiles.Validate(profile);
        if (error is not null)
        {
            return error;
        }

        try
        {
            if (isNew)
            {
                await _profiles.AddAsync(profile);
            }
            else
            {
                await _profiles.UpdateAsync(profile);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Saving the profile failed.", ex);
            return ex.Message;
        }

        Editor = null;
        _notifications.Show(NotificationKind.Success, isNew ? "Profile created" : "Profile saved", profile.Name.Trim());
        return null;
    }

    private async Task DeleteAsync(ProfileItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Delete profile?",
            $"\"{item.Name}\" will be removed permanently. Files in its game directory are not touched.",
            "Delete",
            "Cancel",
            isDanger: true);

        if (!confirmed)
        {
            return;
        }

        await _profiles.DeleteAsync(item.Profile.Id);
        _notifications.Show(NotificationKind.Success, "Profile deleted", item.Name);
    }

    private async Task DuplicateAsync(ProfileItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var copy = await _profiles.DuplicateAsync(item.Profile.Id);
        _notifications.Show(NotificationKind.Success, "Profile duplicated", copy.Name);
    }

    private async Task SelectAsync(ProfileItemViewModel? item)
    {
        if (item is not null)
        {
            await _profiles.SelectAsync(item.Profile.Id);
        }
    }
}
