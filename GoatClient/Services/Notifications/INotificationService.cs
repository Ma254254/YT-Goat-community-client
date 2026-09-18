using System.Collections.ObjectModel;
using GoatClient.Models;

namespace GoatClient.Services.Notifications;

/// <summary>Toast notifications inside the launcher window.</summary>
public interface INotificationService
{
    ReadOnlyObservableCollection<NotificationItem> Items { get; }

    /// <summary>Info/success toasts respect the "Notifications" setting. Warnings and errors are always shown.</summary>
    void Show(NotificationKind kind, string title, string message);

    void Dismiss(NotificationItem item);
}
