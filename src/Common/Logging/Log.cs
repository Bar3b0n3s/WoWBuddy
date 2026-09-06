using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace WoWBuddy.Common.Logging;

/// <summary>
/// Process-wide logging entry point.
/// <para>
/// Everything the bot does is logged through here. The level switch is exposed so
/// the UI can raise or lower verbosity at runtime without tearing the logger down,
/// which matters because the log pane is the only window a user has into a bot that
/// is otherwise playing unattended.
/// </para>
/// </summary>
public static class Log
{
    private static readonly LoggingLevelSwitch LevelSwitch = new(LogEventLevel.Information);
    private static ILogger _logger = Serilog.Core.Logger.None;
    private static bool _initialised;

    /// <summary>Minimum level that reaches the sinks. Settable at runtime.</summary>
    public static LogEventLevel MinimumLevel
    {
        get => LevelSwitch.MinimumLevel;
        set => LevelSwitch.MinimumLevel = value;
    }

    /// <summary>The configured root logger. Safe to use before <see cref="Initialise"/> (it discards).</summary>
    public static ILogger Logger => _logger;

    /// <summary>
    /// Configures console plus rolling-file logging. Idempotent: calling twice is a no-op
    /// so that a dev tool and the UI can both call it defensively.
    /// </summary>
    /// <param name="logDirectory">
    /// Directory for rolling log files. Defaults to a <c>logs</c> folder beside the executable.
    /// </param>
    public static void Initialise(string? logDirectory = null)
    {
        if (_initialised)
        {
            return;
        }

        logDirectory ??= Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDirectory);

        const string template =
            "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

        _logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(LevelSwitch)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: template)
            .WriteTo.File(
                path: Path.Combine(logDirectory, "wowbuddy-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: template,
                shared: true)
            .CreateLogger();

        Serilog.Log.Logger = _logger;
        _initialised = true;
    }

    /// <summary>Returns a logger tagged with <paramref name="context"/> as its source.</summary>
    public static ILogger For(string context) => _logger.ForContext("SourceContext", context);

    /// <summary>Returns a logger tagged with <typeparamref name="T"/> as its source.</summary>
    public static ILogger For<T>() => For(typeof(T).Name);

    /// <summary>Flushes and disposes the sinks. Call on shutdown so the file sink is not truncated.</summary>
    public static void Shutdown()
    {
        (_logger as IDisposable)?.Dispose();
        _logger = Serilog.Core.Logger.None;
        Serilog.Log.Logger = Serilog.Core.Logger.None;
        _initialised = false;
    }
}
