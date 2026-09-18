using System.ComponentModel;
using GoatClient.Models;

namespace GoatClient.Services.Status;

/// <summary>Central launcher status shown in the title bar and on the Home page.</summary>
public interface IStatusService : INotifyPropertyChanged
{
    LauncherStatus Status { get; }

    string Message { get; }

    bool IsBusy { get; }

    void Set(LauncherStatus status, string? message = null);

    /// <summary>Sets a busy status and returns to Ready when the scope is disposed.</summary>
    IDisposable Begin(LauncherStatus status, string message);
}
