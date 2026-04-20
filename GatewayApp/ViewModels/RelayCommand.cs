using System.Diagnostics;
using System.Windows.Input;

namespace GatewayApp.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Func<object?, Task>? _executeAsync;
    private readonly Action<object?>? _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Func<object?, Task> executeAsync, Predicate<object?>? canExecute = null)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        try
        {
            if (_executeAsync is not null)
            {
                await _executeAsync(parameter).ConfigureAwait(false);
                return;
            }

            _execute?.Invoke(parameter);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RelayCommand execution failed: {ex}");
        }
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class RelayCommand<T> : ICommand
{
    private readonly Func<T?, Task>? _executeAsync;
    private readonly Action<T?>? _execute;
    private readonly Predicate<T?>? _canExecute;

    public RelayCommand(Action<T?> execute, Predicate<T?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Func<T?, Task> executeAsync, Predicate<T?>? canExecute = null)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        var typed = parameter is T cast ? cast : default;
        return _canExecute?.Invoke(typed) ?? true;
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        var typed = parameter is T cast ? cast : default;

        try
        {
            if (_executeAsync is not null)
            {
                await _executeAsync(typed).ConfigureAwait(false);
                return;
            }

            _execute?.Invoke(typed);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RelayCommand<{typeof(T).Name}> execution failed: {ex}");
        }
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
