using System.Windows.Input;
using GoatClient.Core;
using GoatClient.Core.Commands;

namespace GoatClient.ViewModels;

public sealed class DialogViewModel : ObservableObject
{
    private readonly Action<bool> _complete;
    private bool _completed;

    public DialogViewModel(string title, string message, string confirmText, string cancelText, bool showCancel, bool isDanger, Action<bool> complete)
    {
        Title = title;
        Message = message;
        ConfirmText = confirmText;
        CancelText = cancelText;
        ShowCancel = showCancel;
        IsDanger = isDanger;
        _complete = complete;
        ConfirmCommand = new RelayCommand(() => Complete(true));
        CancelCommand = new RelayCommand(() => Complete(!ShowCancel));
    }

    public string Title { get; }

    public string Message { get; }

    public string ConfirmText { get; }

    public string CancelText { get; }

    public bool ShowCancel { get; }

    public bool IsDanger { get; }

    public ICommand ConfirmCommand { get; }

    /// <summary>Also bound to Escape. For info dialogs it simply closes the dialog.</summary>
    public ICommand CancelCommand { get; }

    private void Complete(bool result)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _complete(result);
    }
}
