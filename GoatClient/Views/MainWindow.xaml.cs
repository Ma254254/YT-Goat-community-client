using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GoatClient.Core.Helpers;
using GoatClient.ViewModels;

namespace GoatClient.Views;

/// <summary>
/// Code-behind contains window chrome behaviour only (drag, min/max/close, corner radius,
/// page transition). All application logic lives in <see cref="MainViewModel"/> and services.
/// </summary>
public partial class MainWindow : Window
{
    private bool _closeApproved;
    private bool _closeInProgress;

    public MainWindow()
    {
        InitializeComponent();
        WindowMaximizeHelper.Attach(this);
        StateChanged += (_, _) => ApplyWindowState();
        DataContextChanged += OnDataContextChanged;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is MainViewModel newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentViewModel) && ViewModel?.EnablePageTransitions == true)
        {
            PlayPageTransition();
        }
    }

    private void PlayPageTransition()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        if (PageHost.RenderTransform is TranslateTransform shift)
        {
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        }
    }

    private void ApplyWindowState()
    {
        var maximized = WindowState == WindowState.Maximized;
        RootGrid.Margin = maximized ? new Thickness(0) : new Thickness(12);
        RootBorder.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(14);
        RootBorder.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
        ShadowBorder.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        SidebarBorder.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(13, 0, 0, 13);
        MaxRestoreIcon.Data = (Geometry)FindResource(maximized ? "IconWinRestore" : "IconWinMaximize");
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            // Restore under the cursor, then continue dragging.
            var position = e.GetPosition(this);
            var ratio = ActualWidth > 0 ? position.X / ActualWidth : 0.5;
            var restoreWidth = RestoreBounds.Width;
            var screen = PointToScreen(position);
            var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var cursor = fromDevice.Transform(screen);

            WindowState = WindowState.Normal;
            Left = cursor.X - (restoreWidth * ratio);
            Top = cursor.Y - position.Y - 12;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // Mouse was released before DragMove started.
            }
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>Set for an app restart (theme change): closes cleanly without the confirmation dialog.</summary>
    public bool SkipCloseConfirmation { get; set; }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_closeApproved)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        if (_closeInProgress || ViewModel is not { } vm)
        {
            if (ViewModel is null)
            {
                _closeApproved = true;
                Close();
            }

            return;
        }

        _closeInProgress = true;
        try
        {
            if (vm.ConfirmBeforeClosing && !SkipCloseConfirmation &&
                !await vm.Dialogs.ConfirmAsync("Close GOAT CLIENT?", "Do you really want to close the launcher?", "Close", "Cancel"))
            {
                return;
            }

            await vm.ShutdownAsync();
            _closeApproved = true;
        }
        finally
        {
            _closeInProgress = false;
        }

        Close();
    }
}
