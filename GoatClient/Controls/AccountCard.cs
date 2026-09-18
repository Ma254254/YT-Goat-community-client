using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GoatClient.Controls;

/// <summary>Sidebar account block. Styled in Themes/Controls.xaml.</summary>
public class AccountCard : Control
{
    public static readonly DependencyProperty StatusTextProperty =
        DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(AccountCard), new PropertyMetadata("Not connected"));

    public static readonly DependencyProperty LoginCommandProperty =
        DependencyProperty.Register(nameof(LoginCommand), typeof(ICommand), typeof(AccountCard), new PropertyMetadata(null));

    public static readonly DependencyProperty OpenCommandProperty =
        DependencyProperty.Register(nameof(OpenCommand), typeof(ICommand), typeof(AccountCard), new PropertyMetadata(null));

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public ICommand? LoginCommand
    {
        get => (ICommand?)GetValue(LoginCommandProperty);
        set => SetValue(LoginCommandProperty, value);
    }

    public ICommand? OpenCommand
    {
        get => (ICommand?)GetValue(OpenCommandProperty);
        set => SetValue(OpenCommandProperty, value);
    }
}
