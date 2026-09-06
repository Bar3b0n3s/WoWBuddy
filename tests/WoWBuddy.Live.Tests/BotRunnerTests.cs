using WoWBuddy.Behavior;
using WoWBuddy.BotBases;
using WoWBuddy.CombatRoutines.Routines;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Scheduling;
using WoWBuddy.Core.Execution;
using WoWBuddy.Core.Tests;
using WoWBuddy.GameApi.Capabilities;
using WoWBuddy.Live;
using WoWBuddy.Navigation.Movement;
using Xunit;

namespace WoWBuddy.Live.Tests;

public sealed class BotRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static (BotRunner Runner, LiveBotState State, FakeCharacterView View, List<string> Log)
        Build(Func<IBotState, RunStatus>? behaviour = null, Action<IBotState>? plugins = null)
    {
        FakeLua lua = FakeLua.Typical335a();
        CapabilityReport capabilities = CapabilityProbes.Probe(lua);

        FakeCharacterView view = new();
        List<string> log = [];

        MovementController movement = new(new ClickToMoveWriter(new FakeClientMemory()));
        movement.Enable();

        LiveBotState state = new(
            view,
            movement,
            new FakeNavigation(),
            new LuaQuestLog(lua, capabilities, () => Now),
            new LuaPartyState(lua, capabilities, () => view.Position, view.Locate, () => Now),
            new LuaBattlegrounds(lua, capabilities, () => Now),
            new LuaInventory(lua, capabilities, () => Now),
            new SessionScheduler(new SessionSchedule()),
            new FuryWarrior(),
            new StubCombat(),
            () => Now);

        Node<IBotState> tree = new Do<IBotState>(s =>
        {
            log.Add("tree");
            return behaviour?.Invoke(s) ?? RunStatus.Running;
        })
        { Name = "test" };

        BotRunner runner = new(state, tree, plugins);

        return (runner, state, view, log);
    }

    [Fact]
    public void NothingHappensUntilItIsStarted()
    {
        (BotRunner runner, _, _, List<string> log) = Build();

        Assert.Equal(TickOutcome.Stopped, runner.Tick(Now));
        Assert.Empty(log);
        Assert.False(runner.IsRunning);
    }

    [Fact]
    public void TheTreeRunsOnceStarted()
    {
        (BotRunner runner, _, _, List<string> log) = Build();

        runner.Start();

        Assert.Equal(TickOutcome.Ran, runner.Tick(Now));
        Assert.Equal(["tree"], log);
        Assert.Equal(1, runner.Ticks);
    }

    [Fact]
    public void NothingRunsDuringALoadingScreen()
    {
        // The object manager is empty, and every reading taken from it would be about nobody.
        (BotRunner runner, _, FakeCharacterView view, List<string> log) = Build();

        view.IsInWorld = false;
        runner.Start();

        Assert.Equal(TickOutcome.NotInWorld, runner.Tick(Now));
        Assert.Empty(log);
        Assert.True(runner.IsRunning);
    }

    [Fact]
    public void ArrivingInTheWorldThrowsAwayReadingsFromBeforeIt()
    {
        // Everything read before a loading screen is about a different situation.
        (BotRunner runner, LiveBotState state, FakeCharacterView view, _) = Build();

        runner.Start();
        runner.Tick(Now);

        view.IsInWorld = false;
        runner.Tick(Now);

        view.IsInWorld = true;

        Assert.Equal(TickOutcome.Ran, runner.Tick(Now));
        Assert.Equal(Now, state.Now);
    }

    [Fact]
    public void MovementIsAdvancedBeforeTheTreeDecidesAnything()
    {
        // Otherwise the tree sees the position the character had when the last decision was
        // made, not the one it actually reached.
        DateTimeOffset seenAt = default;

        (BotRunner runner, LiveBotState state, _, _) = Build(s =>
        {
            seenAt = s.Now;
            return RunStatus.Running;
        });

        runner.Start();
        runner.Tick(Now);

        Assert.Equal(Now, seenAt);
    }

    [Fact]
    public void PluginsGetTheirTickAfterTheBotHasActed()
    {
        List<string> order = [];

        (BotRunner runner, _, _, _) = Build(
            _ =>
            {
                order.Add("tree");
                return RunStatus.Running;
            },
            _ => order.Add("plugins"));

        runner.Start();
        runner.Tick(Now);

        Assert.Equal(["tree", "plugins"], order);
    }

    [Fact]
    public void ABotWithNoPluginsStillRuns()
    {
        (BotRunner runner, _, _, List<string> log) = Build(plugins: null);

        runner.Start();

        Assert.Equal(TickOutcome.Ran, runner.Tick(Now));
        Assert.Single(log);
    }

    [Fact]
    public void AnythingThatThrowsStopsTheBotRatherThanTheProcess()
    {
        // An unattended session that hits an unexpected state should stop somewhere harmless.
        (BotRunner runner, _, FakeCharacterView view, _) = Build(
            _ => throw new InvalidOperationException("something unexpected"));

        runner.Start();

        Assert.Equal(TickOutcome.Faulted, runner.Tick(Now));
        Assert.False(runner.IsRunning);
        Assert.Contains("something unexpected", runner.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public void APluginThatThrowsAlsoStopsTheBotRatherThanBeingSwallowed()
    {
        // The plugin manager forgives a plugin its faults; a plugin invoked directly, with no
        // manager between, has nothing to catch it, and pretending otherwise here would hide
        // the failure from both.
        (BotRunner runner, _, _, _) = Build(
            plugins: _ => throw new InvalidOperationException("plugin broke"));

        runner.Start();

        Assert.Equal(TickOutcome.Faulted, runner.Tick(Now));
        Assert.False(runner.IsRunning);
    }

    [Fact]
    public void StoppingLeavesTheCharacterStandingStill()
    {
        // A bot that stops while the character is still walking somewhere is not stopped.
        (BotRunner runner, LiveBotState state, _, _) = Build();

        runner.Start();
        state.MoveTo(new Vector3(1600f, -2500f, 60f));
        Assert.True(state.IsMoving);

        runner.Stop();

        Assert.False(runner.IsRunning);
        Assert.False(state.IsMoving);
    }

    [Fact]
    public void StoppingTwiceIsHarmless()
    {
        (BotRunner runner, _, _, _) = Build();

        runner.Start();
        runner.Stop();
        runner.Stop();

        Assert.False(runner.IsRunning);
    }

    [Fact]
    public void WhatTheTreeSaidIsKeptForTheWindow()
    {
        (BotRunner runner, _, _, _) = Build(_ => RunStatus.Success);

        runner.Start();
        runner.Tick(Now);

        Assert.Equal(RunStatus.Success, runner.LastStatus);
    }

    [Fact]
    public void TicksAreOnlyCountedWhenTheTreeActuallyRan()
    {
        (BotRunner runner, _, FakeCharacterView view, _) = Build();

        runner.Start();
        runner.Tick(Now);

        view.IsInWorld = false;
        runner.Tick(Now);
        runner.Tick(Now);

        Assert.Equal(1, runner.Ticks);
    }
}
