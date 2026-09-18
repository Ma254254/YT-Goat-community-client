using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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

    public static readonly DependencyProperty SubTextProperty =
        DependencyProperty.Register(nameof(SubText), typeof(string), typeof(AccountCard), new PropertyMetadata("Microsoft account"));

    public static readonly DependencyProperty IsConnectedProperty =
        DependencyProperty.Register(nameof(IsConnected), typeof(bool), typeof(AccountCard), new PropertyMetadata(false));

    public static readonly DependencyProperty FaceProperty =
        DependencyProperty.Register(nameof(Face), typeof(ImageSource), typeof(AccountCard), new PropertyMetadata(null));

    public static readonly DependencyProperty HatProperty =
        DependencyProperty.Register(nameof(Hat), typeof(ImageSource), typeof(AccountCard), new PropertyMetadata(null));

    public static readonly DependencyProperty ButtonTextProperty =
        DependencyProperty.Register(nameof(ButtonText), typeof(string), typeof(AccountCard), new PropertyMetadata("LOGIN"));

    public string SubText
    {
        get => (string)GetValue(SubTextProperty);
        set => SetValue(SubTextProperty, value);
    }

    public bool IsConnected
    {
        get => (bool)GetValue(IsConnectedProperty);
        set => SetValue(IsConnectedProperty, value);
    }

    public ImageSource? Face
    {
        get => (ImageSource?)GetValue(FaceProperty);
        set => SetValue(FaceProperty, value);
    }

    public ImageSource? Hat
    {
        get => (ImageSource?)GetValue(HatProperty);
        set => SetValue(HatProperty, value);
    }

    public string ButtonText
    {
        get => (string)GetValue(ButtonTextProperty);
        set => SetValue(ButtonTextProperty, value);
    }
}
