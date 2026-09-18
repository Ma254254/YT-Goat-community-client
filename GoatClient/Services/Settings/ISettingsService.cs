using GoatClient.Models;

namespace GoatClient.Services.Settings;

public interface ISettingsService
{
    /// <summary>Current settings. Treat as read-only – use <see cref="SaveAsync"/> with a clone to change.</summary>
    LauncherSettings Current { get; }

    event EventHandler? SettingsChanged;

    Task LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default);

    Task SaveCurrentAsync(CancellationToken cancellationToken = default);
}
