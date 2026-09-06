using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WoWBuddy.Presentation;

/// <summary>
/// The smallest useful base class for something the window binds to.
/// </summary>
/// <remarks>
/// Hand-written rather than taken from a toolkit. It is thirty lines, it has no behaviour worth
/// depending on someone else for, and every dependency added here has to be justified to the
/// licence allow-list. See <c>docs/legal-and-licensing.md</c>.
/// </remarks>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Says a property changed.</summary>
    protected void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Assigns a field and reports the change, doing nothing when the value is the same.
    /// </summary>
    /// <returns>True when the value actually changed.</returns>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(propertyName);
        return true;
    }
}
