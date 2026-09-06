using WoWBuddy.Behavior;
using WoWBuddy.BotBases;
using WoWBuddy.CombatRoutines;

namespace WoWBuddy.Plugins;

/// <summary>
/// What the bot gives a plugin to work with.
/// </summary>
/// <remarks>
/// Handed to a plugin once, when it starts. Everything a plugin is meant to reach goes through
/// here rather than through statics, so that what a plugin can touch is visible in one place.
/// </remarks>
public interface IPluginHost
{
    /// <summary>The routines the bot knows about. A plugin may add its own.</summary>
    RoutineCatalogue Routines { get; }

    /// <summary>A folder of the plugin's own, for whatever it needs to keep.</summary>
    /// <remarks>
    /// Created before the plugin starts. Writing outside it is not prevented — nothing here
    /// can prevent it — but it is where a well-behaved plugin puts its files.
    /// </remarks>
    string DataDirectory { get; }

    /// <summary>Writes a line to the bot's log, tagged with the plugin's name.</summary>
    void Log(string message);
}

/// <summary>
/// Something a user wrote that runs inside the bot.
/// </summary>
/// <remarks>
/// <para>
/// <b>A plugin is code, and it is as trusted as the bot itself.</b> That is worth saying
/// plainly, because profiles are the opposite — deliberately data, so that running one you
/// downloaded is a small act of trust. A plugin is a .NET assembly loaded into this process. It
/// can read and write the game's memory, read and write your files, and open network
/// connections. .NET has no way to sandbox it: code access security was removed years ago, and
/// anything this project did to restrict a plugin could be undone by the plugin. So the
/// protection is social rather than technical — read what you load, or do not load it.
/// </para>
/// <para>
/// What a plugin is <em>for</em> is the things a profile cannot express: a combat routine for a
/// specialisation played differently, a behaviour a profile's <c>CustomBehavior</c> step names,
/// something that watches the bot and reacts. <see cref="Pulse"/> runs on the bot's own tick,
/// so it must be cheap and must not block.
/// </para>
/// </remarks>
public interface IPlugin
{
    /// <summary>Its name, shown in the UI and used in logs.</summary>
    string Name { get; }

    /// <summary>Who wrote it.</summary>
    string Author { get; }

    /// <summary>Its version.</summary>
    Version Version { get; }

    /// <summary>What it does, in a sentence.</summary>
    string Description { get; }

    /// <summary>
    /// Called once when the plugin is loaded, before the bot starts.
    /// </summary>
    /// <remarks>
    /// Anything thrown here disables the plugin rather than stopping the bot.
    /// </remarks>
    void Initialise(IPluginHost host);

    /// <summary>Called once when the bot shuts down or the plugin is unloaded.</summary>
    void Shutdown()
    {
    }

    /// <summary>
    /// Called on every tick of the bot while it is running.
    /// </summary>
    /// <remarks>
    /// Must be cheap and must not block: it runs on the tick that also drives combat. A plugin
    /// that throws here repeatedly is disabled — see <see cref="PluginManager"/> — because a
    /// broken plugin should cost its own feature, not the session.
    /// </remarks>
    void Pulse(IBotState state)
    {
    }

    /// <summary>
    /// A behaviour this plugin offers to a profile's <c>CustomBehavior</c> steps, by name.
    /// </summary>
    /// <remarks>
    /// This is how an imported Honorbuddy profile that names a behaviour can be made to work:
    /// write one under the same name and the questing base will find it.
    /// </remarks>
    IReadOnlyDictionary<string, Node<IBotState>> Behaviors =>
        new Dictionary<string, Node<IBotState>>(StringComparer.OrdinalIgnoreCase);
}
