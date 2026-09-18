using GoatClient.Core;
using GoatClient.ViewModels;
using Microsoft.Win32;

namespace GoatClient.Services.Dialogs;

public sealed class DialogService : ObservableObject, IDialogService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DialogViewModel? _current;

    public DialogViewModel? Current
    {
        get => _current;
        private set => SetProperty(ref _current, value);
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText = "Confirm", string cancelText = "Cancel", bool isDanger = false)
        => ShowAsync(title, message, confirmText, cancelText, showCancel: true, isDanger);

    public Task ShowInfoAsync(string title, string message, string closeText = "Got it")
        => ShowAsync(title, message, closeText, string.Empty, showCancel: false, isDanger: false);

    public string? PickFolder(string? initialDirectory, string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private async Task<bool> ShowAsync(string title, string message, string confirmText, string cancelText, bool showCancel, bool isDanger)
    {
        await _gate.WaitAsync();
        try
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Current = new DialogViewModel(title, message, confirmText, cancelText, showCancel, isDanger, result => completion.TrySetResult(result));
            var result = await completion.Task;
            Current = null;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }
}
