using WoWBuddy.Behavior;
using WoWBuddy.BotBases;
using WoWBuddy.BotBases.Tests;
using WoWBuddy.CombatRoutines;
using WoWBuddy.Plugins;
using Xunit;

namespace WoWBuddy.Plugins.Tests;

public sealed class PluginManagerTests
{
    private static PluginManager Manager(out string dataRoot)
    {
        dataRoot = Path.Combine(Path.GetTempPath(), $"wowbuddy-plugins-{Guid.NewGuid():N}");
        return new PluginManager(new PluginHost(new RoutineCatalogue(), dataRoot));
    }

    [Fact]
    public void StartsAPluginAndGivesItTicks()
    {
        PluginManager manager = Manager(out _);
        WellBehavedPlugin plugin = new();

        LoadedPlugin loaded = manager.Add(plugin);

        Assert.Equal(PluginState.Running, loaded.State);
        Assert.Equal(1, plugin.Started);
        Assert.NotNull(plugin.Host);

        manager.Pulse(new FakeBotState());
        manager.Pulse(new FakeBotState());

        Assert.Equal(2, plugin.Pulses);
    }

    [Fact]
    public void APluginThatThrowsWhileStartingIsKeptSoTheUserCanSeeWhy()
    {
        PluginManager manager = Manager(out _);

        LoadedPlugin loaded = manager.Add(new BrokenPlugin { ThrowOnStart = true });

        // Dropping it silently would leave the user with a plugin that is simply not there,
        // and nothing to look at.
        Assert.Equal(PluginState.Faulted, loaded.State);
        Assert.Contains("could not start", loaded.LastError, StringComparison.Ordinal);
        Assert.Single(manager.Plugins);
        Assert.Empty(manager.Active);
    }

    [Fact]
    public void APluginThatKeepsThrowingIsSwitchedOffAndTheBotCarriesOn()
    {
        // An unattended bot that dies at 2am because someone's experimental plugin threw a
        // null reference is a worse outcome than one that switches it off and keeps grinding.
        PluginManager manager = Manager(out _);
        BrokenPlugin broken = new();
        WellBehavedPlugin good = new();

        LoadedPlugin loadedBroken = manager.Add(broken);
        manager.Add(good);

        for (int tick = 0; tick < PluginManager.FaultLimit + 3; tick++)
        {
            manager.Pulse(new FakeBotState());
        }

        Assert.Equal(PluginState.Faulted, loadedBroken.State);
        Assert.Equal(PluginManager.FaultLimit, broken.Pulses);

        // The well-behaved one got every tick, including the ones after the other faulted.
        Assert.Equal(PluginManager.FaultLimit + 3, good.Pulses);
    }

    [Fact]
    public void OneBadTickIsForgiven()
    {
        // A plugin can legitimately be caught out by a moment of odd state — a loading screen,
        // a target that vanished — and killing it for that would be harsh.
        PluginManager manager = Manager(out _);
        LoadedPlugin loaded = manager.Add(new BrokenPlugin());

        manager.Pulse(new FakeBotState());

        Assert.Equal(PluginState.Running, loaded.State);
        Assert.Equal(1, loaded.Faults);
    }

    [Fact]
    public void TurningAPluginBackOnClearsWhatWentWrong()
    {
        PluginManager manager = Manager(out _);
        LoadedPlugin loaded = manager.Add(new BrokenPlugin());

        for (int tick = 0; tick < PluginManager.FaultLimit; tick++)
        {
            manager.Pulse(new FakeBotState());
        }

        Assert.Equal(PluginState.Faulted, loaded.State);

        manager.Enable(loaded);

        Assert.Equal(PluginState.Running, loaded.State);
        Assert.Equal(0, loaded.Faults);
        Assert.Empty(loaded.LastError);
    }

    [Fact]
    public void ADisabledPluginGetsNoTicks()
    {
        PluginManager manager = Manager(out _);
        WellBehavedPlugin plugin = new();
        LoadedPlugin loaded = manager.Add(plugin);

        manager.Disable(loaded);
        manager.Pulse(new FakeBotState());

        Assert.Equal(0, plugin.Pulses);
        Assert.Empty(manager.Active);
    }

    [Fact]
    public void BehavioursFromPluginsAreCollectedByName()
    {
        PluginManager manager = Manager(out _);

        WellBehavedPlugin plugin = new();
        plugin.Offered["WaitTimer"] = new Do<IBotState>(_ => RunStatus.Success);
        manager.Add(plugin);

        Assert.True(manager.Behaviors().ContainsKey("waittimer"));
    }

    [Fact]
    public void TheFirstPluginToOfferANameKeepsIt()
    {
        // Silently taking one of them would make a profile behave differently depending on the
        // order files happened to be read in.
        PluginManager manager = Manager(out _);

        WellBehavedPlugin first = new() { Name = "First" };
        Node<IBotState> mine = new Do<IBotState>(_ => RunStatus.Success);
        first.Offered["WaitTimer"] = mine;

        WellBehavedPlugin second = new() { Name = "Second" };
        second.Offered["WaitTimer"] = new Do<IBotState>(_ => RunStatus.Failure);

        manager.Add(first);
        manager.Add(second);

        Assert.Same(mine, manager.Behaviors()["WaitTimer"]);
    }

    [Fact]
    public void ADisabledPluginOffersNoBehaviours()
    {
        PluginManager manager = Manager(out _);

        WellBehavedPlugin plugin = new();
        plugin.Offered["WaitTimer"] = new Do<IBotState>(_ => RunStatus.Success);
        LoadedPlugin loaded = manager.Add(plugin);

        manager.Disable(loaded);

        Assert.Empty(manager.Behaviors());
    }

    [Fact]
    public void ShutdownReachesEveryPluginEvenWhenOneThrows()
    {
        PluginManager manager = Manager(out _);

        manager.Add(new BrokenPlugin { ThrowOnStart = true });
        WellBehavedPlugin good = new();
        manager.Add(good);

        manager.Shutdown();

        Assert.Equal(1, good.ShutDown);
    }

    [Fact]
    public void APluginGetsAFolderOfItsOwn()
    {
        string root = Path.Combine(Path.GetTempPath(), $"wowbuddy-plugins-{Guid.NewGuid():N}");

        try
        {
            PluginHost host = new(new RoutineCatalogue(), root, "My Plugin");

            Assert.True(Directory.Exists(host.DataDirectory));
            Assert.StartsWith(root, host.DataDirectory, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void APluginCannotPickItsOwnFolderAnywhereOnDisk()
    {
        // The name comes from the plugin, so it is not to be trusted as a path.
        string root = Path.Combine(Path.GetTempPath(), $"wowbuddy-plugins-{Guid.NewGuid():N}");

        try
        {
            PluginHost host = new(new RoutineCatalogue(), root, "../../elsewhere");

            Assert.StartsWith(Path.GetFullPath(root), Path.GetFullPath(host.DataDirectory), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void AMissingPluginFolderIsNotAProblem()
    {
        // Most users have no plugins at all.
        PluginManager manager = Manager(out string root);
        PluginLoader loader = new(manager);

        PluginLoadResult result = loader.LoadDirectory(Path.Combine(root, "does-not-exist"));

        Assert.Empty(result.Loaded);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void AFileThatIsNotAnAssemblyIsSkippedQuietly()
    {
        string root = Path.Combine(Path.GetTempPath(), $"wowbuddy-plugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "notaplugin.dll"), "this is not an assembly");

            PluginManager manager = Manager(out _);
            PluginLoadResult result = new PluginLoader(manager).LoadDirectory(root);

            Assert.Empty(result.Loaded);
            Assert.Empty(result.Problems);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void APluginCanAddRoutinesToTheCatalogue()
    {
        // A plugin that ships a combat routine and nothing else is a real and useful thing.
        RoutineCatalogue catalogue = new();
        int before = catalogue.All.Count;

        Assert.True(catalogue.Add(typeof(RoutineCatalogue).Assembly) > 0);
        Assert.True(catalogue.All.Count > before);
    }
}
