using System.Windows.Input;

namespace EF_Config.Wpf.Mvvm;

// Hand-rolled ICommand - no MVVM toolkit. WPF data-binds a Button's Command
// to an instance of this, calling CanExecute to enable/disable the control
// and Execute on click.
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    // Convenience overload for the common case of commands that don't need
    // the CommandParameter WPF passes through Execute/CanExecute.
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    // CommandManager.RequerySuggested fires on common input events (focus
    // change, mouse/keyboard activity) and is WPF's built-in mechanism for
    // re-querying CanExecute without every command needing its own timer or
    // manual wiring.
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    // Call after a state change that affects CanExecute but doesn't
    // necessarily correspond to one of CommandManager's tracked input
    // events (e.g. SelectedPerson changing via code, not a click).
    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}
