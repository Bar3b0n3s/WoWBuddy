using WoWBuddy.Behavior;
using WoWBuddy.BotBases;
using WoWBuddy.Plugins;

namespace WoWBuddy.Plugins.Tests;

/// <summary>A plugin that behaves.</summary>
internal sealed class WellBehavedPlugin : IPlugin
{
    public string Name { get; init; } = "Well Behaved";

    public string Author => "someone";

    public Version Version => new(1, 0);

    public string Description => "does as it is told";

    public int Started { get; private set; }

    public int Pulses { get; private set; }

    public int ShutDown { get; private set; }

    public IPluginHost? Host { get; private set; }

    /// <summary>Behaviours this plugin offers by name.</summary>
    public Dictionary<string, Node<IBotState>> Offered { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, Node<IBotState>> Behaviors => Offered;

    public void Initialise(IPluginHost host)
    {
        Host = host;
        Started++;
    }

    public void Pulse(IBotState state) => Pulses++;

    public void Shutdown() => ShutDown++;
}

/// <summary>A plugin that throws whenever it is asked to do anything.</summary>
internal sealed class BrokenPlugin : IPlugin
{
    public string Name { get; init; } = "Broken";

    public string Author => "someone";

    public Version Version => new(0, 1);

    public string Description => "throws";

    /// <summary>Whether it also throws while starting.</summary>
    public bool ThrowOnStart { get; init; }

    public int Pulses { get; private set; }

    public void Initialise(IPluginHost host)
    {
        if (ThrowOnStart)
        {
            throw new InvalidOperationException("could not start");
        }
    }

    public void Pulse(IBotState state)
    {
        Pulses++;
        throw new InvalidOperationException("it went wrong");
    }
}
