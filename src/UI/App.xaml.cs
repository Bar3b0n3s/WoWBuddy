using System.Windows;
using System.Windows.Threading;
using WoWBuddy.Common.Logging;

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
    protected override void OnStartup(StartupEventArgs e)
    {
        Log.Initialise();
        Log.For<App>().Information("WoWBuddy starting");

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.For<App>().Fatal(args.ExceptionObject as Exception, "Unhandled exception on a background thread");

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
