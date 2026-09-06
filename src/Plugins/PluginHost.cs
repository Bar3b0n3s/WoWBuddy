using WoWBuddy.CombatRoutines;
using WoWBuddy.Common.Logging;

namespace WoWBuddy.Plugins;

/// <summary>
/// The bot, as a plugin sees it.
/// </summary>
/// <remarks>
/// Deliberately small. Everything a plugin is meant to reach goes through here rather than
/// through statics, so that what a plugin can touch is one file long and can be read in a
/// minute — which matters, because deciding whether to trust a plugin means knowing what it was
/// handed.
/// </remarks>
public sealed class PluginHost : IPluginHost
{
    private readonly string _root;
    private readonly string _pluginName;

    /// <summary>Builds a host for a named plugin.</summary>
    /// <param name="routines">The catalogue a plugin may add to.</param>
    /// <param name="dataRoot">Where plugin folders live.</param>
    /// <param name="pluginName">Which plugin this host is for.</param>
    public PluginHost(RoutineCatalogue routines, string dataRoot, string pluginName = "plugin")
    {
        ArgumentNullException.ThrowIfNull(routines);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        Routines = routines;
        _root = dataRoot;
        _pluginName = pluginName;
    }

    /// <inheritdoc />
    public RoutineCatalogue Routines { get; }

    /// <inheritdoc />
    public string DataDirectory
    {
        get
        {
            string path = Path.Combine(_root, Sanitise(_pluginName));
            Directory.CreateDirectory(path);
            return path;
        }
    }

    /// <inheritdoc />
    public void Log(string message) =>
        Common.Logging.Log.For<PluginHost>().Information("[{Plugin}] {Message}", _pluginName, message);

    /// <summary>
    /// Makes a plugin's name safe to use as a folder name.
    /// </summary>
    /// <remarks>
    /// The name comes from the plugin, so it is not to be trusted as a path: one containing
    /// a separator or a "<c>..</c>" would otherwise pick its own folder anywhere on disk.
    /// </remarks>
    private static string Sanitise(string name)
    {
        char[] cleaned = name.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) || character is '.' ? '_' : character)
            .ToArray();

        string result = new string(cleaned).Trim();

        return result.Length == 0 ? "plugin" : result;
    }
}
