using WoWBuddy.Behavior;
using WoWBuddy.BotBases;
using WoWBuddy.CombatRoutines;
using WoWBuddy.Common.Logging;

namespace WoWBuddy.Plugins;

/// <summary>Why a plugin is not running.</summary>
public enum PluginState
{
    /// <summary>Loaded and running.</summary>
    Running,

    /// <summary>Turned off by the user.</summary>
    Disabled,

    /// <summary>Turned off by the bot after it kept throwing.</summary>
    Faulted,
}

/// <summary>A plugin as the bot sees it.</summary>
/// <param name="Plugin">The plugin itself.</param>
/// <param name="Source">Where it came from, for the UI.</param>
public sealed record LoadedPlugin(IPlugin Plugin, string Source)
{
    /// <summary>Whether it is running.</summary>
    public PluginState State { get; internal set; } = PluginState.Running;

    /// <summary>How many times it has thrown on a tick.</summary>
    public int Faults { get; internal set; }

    /// <summary>What went wrong the last time, for the UI.</summary>
    public string LastError { get; internal set; } = string.Empty;

    /// <summary>True when it should be given ticks.</summary>
    public bool IsActive => State == PluginState.Running;
}

/// <summary>
/// Runs the plugins, and stops running the ones that misbehave.
/// </summary>
/// <remarks>
/// <para>
/// The whole design point is that a broken plugin costs its own feature and not the session. An
/// unattended bot that dies at 2am because someone's experimental plugin threw a null reference
/// is a worse outcome than one that logs the fault, switches that plugin off, and carries on
/// grinding.
/// </para>
/// <para>
/// So every call into a plugin is guarded, faults are counted, and a plugin that keeps throwing
/// is put in <see cref="PluginState.Faulted"/> rather than being given more chances forever.
/// Turning it back on is a deliberate act.
/// </para>
/// </remarks>
public sealed class PluginManager
{
    /// <summary>How many times a plugin may throw on a tick before it is switched off.</summary>
    /// <remarks>
    /// More than one, because a plugin can legitimately be caught out by a moment of odd state
    /// — a loading screen, a target that vanished — and killing it for that would be harsh.
    /// Not many more, because a plugin throwing on every tick fills the log and wastes the tick
    /// it was given.
    /// </remarks>
    public const int FaultLimit = 5;

    private readonly List<LoadedPlugin> _plugins = [];
    private readonly IPluginHost _host;

    public PluginManager(IPluginHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>Everything loaded, running or not.</summary>
    public IReadOnlyList<LoadedPlugin> Plugins => _plugins;

    /// <summary>The plugins currently being given ticks.</summary>
    public IEnumerable<LoadedPlugin> Active => _plugins.Where(plugin => plugin.IsActive);

    /// <summary>
    /// Adds a plugin and starts it.
    /// </summary>
    /// <returns>
    /// The plugin as the bot now sees it. A plugin that threw while starting is added in
    /// <see cref="PluginState.Faulted"/> rather than dropped, so the user can see it failed
    /// and why.
    /// </returns>
    public LoadedPlugin Add(IPlugin plugin, string source = "")
    {
        ArgumentNullException.ThrowIfNull(plugin);

        LoadedPlugin loaded = new(plugin, source);
        _plugins.Add(loaded);

        try
        {
            plugin.Initialise(_host);
            Log.For<PluginManager>().Information(
                "Loaded plugin {Name} {Version} by {Author}",
                plugin.Name, plugin.Version, plugin.Author);
        }
        catch (Exception exception)
        {
            Fault(loaded, exception, "starting");
            loaded.State = PluginState.Faulted;
        }

        return loaded;
    }

    /// <summary>Gives every running plugin a tick.</summary>
    public void Pulse(IBotState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        foreach (LoadedPlugin loaded in _plugins)
        {
            if (!loaded.IsActive)
            {
                continue;
            }

            try
            {
                loaded.Plugin.Pulse(state);
            }
            catch (Exception exception)
            {
                Fault(loaded, exception, "on a tick");

                if (loaded.Faults >= FaultLimit)
                {
                    loaded.State = PluginState.Faulted;

                    Log.For<PluginManager>().Error(
                        "Plugin {Name} has thrown {Count} times and has been switched off. The "
                        + "bot carries on without it.", loaded.Plugin.Name, loaded.Faults);
                }
            }
        }
    }

    /// <summary>
    /// Every behaviour the plugins offer, by name.
    /// </summary>
    /// <remarks>
    /// Where two plugins offer the same name the first one loaded keeps it, and the clash is
    /// reported. Silently taking one of them would make a profile behave differently depending
    /// on the order files happened to be read in.
    /// </remarks>
    public IReadOnlyDictionary<string, Node<IBotState>> Behaviors()
    {
        Dictionary<string, Node<IBotState>> behaviors = new(StringComparer.OrdinalIgnoreCase);

        foreach (LoadedPlugin loaded in Active)
        {
            IReadOnlyDictionary<string, Node<IBotState>> offered;

            try
            {
                offered = loaded.Plugin.Behaviors;
            }
            catch (Exception exception)
            {
                Fault(loaded, exception, "listing its behaviours");
                continue;
            }

            foreach ((string name, Node<IBotState> node) in offered)
            {
                if (behaviors.TryAdd(name, node))
                {
                    continue;
                }

                Log.For<PluginManager>().Warning(
                    "Plugin {Name} offers a behaviour called '{Behaviour}', which another plugin "
                    + "already provides. The first one is being used.", loaded.Plugin.Name, name);
            }
        }

        return behaviors;
    }

    /// <summary>Turns a plugin off.</summary>
    public void Disable(LoadedPlugin loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        loaded.State = PluginState.Disabled;
    }

    /// <summary>Turns a plugin back on, clearing whatever went wrong before.</summary>
    public void Enable(LoadedPlugin loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);

        loaded.State = PluginState.Running;
        loaded.Faults = 0;
        loaded.LastError = string.Empty;
    }

    /// <summary>Stops every plugin.</summary>
    public void Shutdown()
    {
        foreach (LoadedPlugin loaded in _plugins)
        {
            try
            {
                loaded.Plugin.Shutdown();
            }
            catch (Exception exception)
            {
                // Nothing useful left to do about it: the bot is closing either way.
                Log.For<PluginManager>().Warning(
                    exception, "Plugin {Name} threw while shutting down", loaded.Plugin.Name);
            }
        }
    }

    private static void Fault(LoadedPlugin loaded, Exception exception, string doing)
    {
        loaded.Faults++;
        loaded.LastError = exception.Message;

        Log.For<PluginManager>().Error(
            exception, "Plugin {Name} threw while {Doing}", loaded.Plugin.Name, doing);
    }
}
