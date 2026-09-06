using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using WoWBuddy.CombatRoutines;
using WoWBuddy.Common.Logging;
using WoWBuddy.Plugins;
using WoWBuddy.Presentation;

namespace WoWBuddy.UI;

/// <summary>
/// Application entry point.
/// </summary>
/// <remarks>
/// The unhandled-exception handler is the crash-safety rule in practice: if anything goes
/// wrong the bot stops and the character is left standing, rather than the process dying
/// mid-action or, worse, continuing to send input in an unknown state.
/// </remarks>
public partial class App : Application
{
    private PluginManager? _plugins;

    protected override void OnStartup(StartupEventArgs e)
    {
        MainViewModel model = Compose();

        Log.For<App>().Information("WoWBuddy starting");

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.For<App>().Fatal(args.ExceptionObject as Exception, "Unhandled exception on a background thread");

        base.OnStartup(e);

        new MainWindow(model).Show();
    }

    /// <summary>
    /// Builds everything the window needs, in the order it has to be built.
    /// </summary>
    /// <remarks>
    /// Written out rather than done with a container. There are six things here, they are
    /// constructed once, and a list of six constructor calls is easier to follow than the
    /// registrations that would replace it.
    /// </remarks>
    private MainViewModel Compose()
    {
        LogBuffer buffer = new();

        // The window's log panel is a second sink rather than a second logger, so what it
        // shows and what the file records cannot drift apart.
        Log.Initialise(extraSink: new LogSink(buffer));

        RoutineCatalogue routines = new();

        string pluginRoot = Path.Combine(AppContext.BaseDirectory, "Plugins");

        _plugins = new PluginManager(new PluginHost(routines, pluginRoot));

        PluginLoadResult loaded = new PluginLoader(_plugins).LoadDirectory(pluginRoot, routines);

        foreach (string problem in loaded.Problems)
        {
            Log.For<App>().Warning("{Problem}", problem);
        }

        MainViewModel model = new(
            new WowClientDiscovery(),
            new BotController(),
            routines,
            _plugins);

        // The buffer the window binds to is the one the sink writes into, so lines logged
        // during start-up above are already there when the window opens.
        foreach (LogLine line in buffer.Lines)
        {
            model.Log.Add(line);
        }

        return model;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _plugins?.Shutdown();

        Log.For<App>().Information("WoWBuddy exiting");
        Log.Shutdown();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.For<App>().Fatal(e.Exception, "Unhandled exception on the UI thread");

        MessageBox.Show(
            "WoWBuddy hit an unexpected error and has stopped. Your character has been left " +
            "standing where it was.\n\n" +
            "The full details are in the log folder next to the executable.\n\n" +
            e.Exception.Message,
            "WoWBuddy",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Handled so the window survives; the bot itself is stopped by whoever owns it.
        e.Handled = true;
    }
}
