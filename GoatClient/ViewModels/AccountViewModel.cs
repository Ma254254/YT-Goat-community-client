using System.Windows.Input;

namespace GoatClient.ViewModels;

/// <summary>Account page. Phase 1: not connected – no fake usernames, UUIDs or sign-ins.</summary>
public sealed class AccountViewModel : ViewModelBase
{
    public AccountViewModel(ICommand loginCommand)
    {
        LoginCommand = loginCommand;
    }

    public string StatusText => "Not connected";

    public string Title => "Microsoft Account Authentication";

    public string Phase => "Coming in Phase 2";

    public ICommand LoginCommand { get; }
}
