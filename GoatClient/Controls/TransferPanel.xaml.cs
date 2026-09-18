using System.Windows.Controls;

namespace GoatClient.Controls;

/// <summary>Install / download / launch progress with cancel. DataContext: <see cref="ViewModels.GameController"/>.</summary>
public partial class TransferPanel : UserControl
{
    public TransferPanel()
    {
        InitializeComponent();
    }
}
