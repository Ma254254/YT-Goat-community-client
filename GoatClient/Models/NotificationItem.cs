namespace GoatClient.Models;

public enum NotificationKind
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed record NotificationItem(NotificationKind Kind, string Title, string Message)
{
    public Guid Id { get; } = Guid.NewGuid();
}
