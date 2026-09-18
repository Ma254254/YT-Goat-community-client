using System.Windows.Input;

namespace GoatClient.Core.Commands;

/// <summary>
/// Asynchronous MVVM command. Prevents re-entrancy while running and routes
/// unexpected exceptions to <see cref="GlobalErrorHandler"/> (logging + user feedback).
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private bool _isRunning;

    /// <summary>Set once by the composition root. Receives every unhandled command exception.</summary>
    public static Action<Exception>? GlobalErrorHandler { get; set; }

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : new Func<object?, bool>(_ => canExecute()))
    {
    }

    public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool IsRunning => _isRunning;

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isRunning = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await _execute(parameter);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is an expected outcome (e.g. shutdown).
        }
        catch (Exception ex)
        {
            if (GlobalErrorHandler is null)
            {
                throw;
            }

            GlobalErrorHandler(ex);
        }
        finally
        {
            _isRunning = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
