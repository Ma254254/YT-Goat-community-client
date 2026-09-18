using GoatClient.Core;
using GoatClient.Models;

namespace GoatClient.Services.Status;

public sealed class StatusService : ObservableObject, IStatusService
{
    private LauncherStatus _status = LauncherStatus.Loading;
    private string _message = "Starting…";

    public LauncherStatus Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(IsBusy));
            }
        }
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public bool IsBusy => Status is LauncherStatus.Loading or LauncherStatus.Saving or LauncherStatus.DetectingJava;

    public void Set(LauncherStatus status, string? message = null)
    {
        Status = status;
        Message = message ?? DefaultMessage(status);
    }

    public IDisposable Begin(LauncherStatus status, string message)
    {
        Set(status, message);
        return new Scope(this);
    }

    private static string DefaultMessage(LauncherStatus status) => status switch
    {
        LauncherStatus.Ready => "Ready",
        LauncherStatus.Loading => "Loading…",
        LauncherStatus.Saving => "Saving…",
        LauncherStatus.DetectingJava => "Detecting Java runtimes…",
        LauncherStatus.Error => "An error occurred",
        _ => status.ToString(),
    };

    private sealed class Scope : IDisposable
    {
        private StatusService? _owner;

        public Scope(StatusService owner) => _owner = owner;

        public void Dispose()
        {
            // Do not overwrite an error that happened inside the scope.
            if (_owner is { Status: not LauncherStatus.Error })
            {
                _owner.Set(LauncherStatus.Ready);
            }

            _owner = null;
        }
    }
}
