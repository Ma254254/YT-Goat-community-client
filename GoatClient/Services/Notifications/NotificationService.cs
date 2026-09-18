using System.Collections.ObjectModel;
using System.Windows.Threading;
using GoatClient.Models;

namespace GoatClient.Services.Notifications;

public sealed class NotificationService : INotificationService
{
    private const int MaxVisible = 4;

    private readonly ObservableCollection<NotificationItem> _items = new();
    private readonly Func<bool> _isEnabled;
    private readonly Dispatcher _dispatcher;

    public NotificationService(Func<bool> isEnabled, Dispatcher dispatcher)
    {
        _isEnabled = isEnabled;
        _dispatcher = dispatcher;
        Items = new ReadOnlyObservableCollection<NotificationItem>(_items);
    }

    public ReadOnlyObservableCollection<NotificationItem> Items { get; }

    public void Show(NotificationKind kind, string title, string message)
    {
        if ((kind is NotificationKind.Info or NotificationKind.Success) && !_isEnabled())
        {
            return;
        }

        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.InvokeAsync(() => Show(kind, title, message));
            return;
        }

        var item = new NotificationItem(kind, title, message);
        _items.Add(item);
        while (_items.Count > MaxVisible)
        {
            _items.RemoveAt(0);
        }

        var lifetime = kind == NotificationKind.Error ? TimeSpan.FromSeconds(8) : TimeSpan.FromSeconds(4);
        var timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = lifetime };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Dismiss(item);
        };
        timer.Start();
    }

    public void Dismiss(NotificationItem item) => _items.Remove(item);
}
