using System;
using System.Windows;
using Serilog.Core;
using Serilog.Events;
using WoWBuddy.Presentation;

namespace WoWBuddy.UI;

/// <summary>
/// Copies the bot's log into the window.
/// </summary>
/// <remarks>
/// Logging happens on whichever thread was working at the time, and the collection the window
/// binds to may only be changed on the thread that owns it — so every line is marshalled. Doing
/// it here rather than in <see cref="LogBuffer"/> is what keeps that class free of any window
/// type, and therefore testable anywhere.
/// </remarks>
public sealed class LogSink(LogBuffer buffer) : ILogEventSink
{
    private readonly LogBuffer _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        LogLine line = new(
            logEvent.Timestamp,
            logEvent.Level.ToString(),
            logEvent.RenderMessage());

        Application? application = Application.Current;

        if (application is null)
        {
            // No window yet, which happens during start-up. The file log still has it.
            return;
        }

        application.Dispatcher.BeginInvoke(() => _buffer.Add(line));
    }
}
