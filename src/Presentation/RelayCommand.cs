using System.Windows.Input;

namespace WoWBuddy.Presentation;

/// <summary>
/// A command that runs a delegate, and knows when it cannot.
/// </summary>
/// <remarks>
/// <see cref="ICommand"/> lives in the base library rather than in WPF, which is what lets the
/// whole of this project's presentation logic be built and tested on any platform — including
/// the Linux machines this repository's CI uses. Only the XAML needs Windows.
/// </remarks>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    private readonly Action _execute = execute ?? throw new ArgumentNullException(nameof(execute));

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => canExecute is null || canExecute();

    /// <inheritdoc />
    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            _execute();
        }
    }

    /// <summary>Tells the window to ask again whether this command can run.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
