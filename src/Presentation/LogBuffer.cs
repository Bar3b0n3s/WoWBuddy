using System.Collections.ObjectModel;

namespace WoWBuddy.Presentation;

/// <summary>One line of the bot's log, as the window shows it.</summary>
/// <param name="Timestamp">When it happened.</param>
/// <param name="Level">How bad it is.</param>
/// <param name="Message">What happened.</param>
public readonly record struct LogLine(DateTimeOffset Timestamp, string Level, string Message)
{
    public override string ToString() =>
        $"{Timestamp:HH:mm:ss}  {Level,-11}  {Message}";
}

/// <summary>
/// The last so many log lines, for the window to show.
/// </summary>
/// <remarks>
/// <para>
/// Bounded on purpose. A bot left running overnight writes a great many lines, and a window
/// holding every one of them ends the night using more memory than the bot does. The file log
/// is the record; this is a window onto the recent past.
/// </para>
/// <para>
/// It is an <see cref="ObservableCollection{T}"/> so the window updates as lines arrive, which
/// means every change has to happen on the thread that owns it — see <see cref="Add"/>.
/// </para>
/// </remarks>
public sealed class LogBuffer(int capacity = 500)
{
    private readonly int _capacity = capacity > 0
        ? capacity
        : throw new ArgumentOutOfRangeException(nameof(capacity), "A log buffer holding nothing is not useful.");

    /// <summary>The lines, oldest first.</summary>
    public ObservableCollection<LogLine> Lines { get; } = [];

    /// <summary>How many lines it will hold before dropping the oldest.</summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Adds a line, dropping the oldest when it is full.
    /// </summary>
    /// <remarks>
    /// Logging happens on whichever thread was working at the time, and a WPF
    /// <see cref="ObservableCollection{T}"/> may only be changed on the thread that owns it, so
    /// the caller marshals. The view model does that; this stays free of any window type.
    /// </remarks>
    public void Add(LogLine line)
    {
        Lines.Add(line);

        while (Lines.Count > _capacity)
        {
            Lines.RemoveAt(0);
        }
    }

    /// <summary>Empties it.</summary>
    public void Clear() => Lines.Clear();

    /// <summary>The whole buffer as text, for copying out.</summary>
    public string AsText() => string.Join(Environment.NewLine, Lines.Select(line => line.ToString()));
}
