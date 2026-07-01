using System.Windows.Input;

namespace CutterStudio.ViewModels;

/// <summary>
/// ICommand wrapper that prevents accidental re-entry and routes failures to a supplied handler.
/// </summary>
public sealed class AsyncRelayCommand(
    Func<Task> execute,
    Func<bool>? canExecute = null,
    Action<Exception>? onError = null) : ICommand
{
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isExecuting && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;
        _isExecuting = true;
        NotifyCanExecuteChanged();
        try
        {
            await execute();
        }
        catch (OperationCanceledException)
        {
            // User cancellation is not an application error.
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
        }
        finally
        {
            _isExecuting = false;
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
