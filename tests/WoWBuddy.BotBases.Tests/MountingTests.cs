using WoWBuddy.Behavior;
using WoWBuddy.BotBases;
using WoWBuddy.BotBases.Support;
using Xunit;

namespace WoWBuddy.BotBases.Tests;

public sealed class MountingTests
{
    private static readonly TravelSettings On = new() { Enabled = true, WorthMountingFor = 100f };

    [Fact]
    public void MountsBeforeALongJourney()
    {
        FakeBotState state = new() { RemainingDistance = 400f };

        Assert.Equal(RunStatus.Running, RootTree.HandleMounting(On).Tick(state));
        Assert.Contains("Mount", state.TravelState.Actions);
    }

    [Fact]
    public void DoesNotMountToCrossAClearing()
    {
        // Mounting and dismounting on the far side wastes more time than it saves, and looks
        // precisely like a program doing arithmetic.
        FakeBotState state = new() { RemainingDistance = 40f };

        Assert.Equal(RunStatus.Failure, RootTree.HandleMounting(On).Tick(state));
        Assert.Empty(state.TravelState.Actions);
    }

    [Fact]
    public void MeasuresTheJourneyRatherThanTheDistanceToTheDestination()
    {
        // A destination twenty yards away round a cliff is a long journey, and the difference
        // is exactly what decides whether mounting is worth it.
        FakeBotState state = new() { RemainingDistance = 300f };

        RootTree.HandleMounting(On).Tick(state);

        Assert.Contains("Mount", state.TravelState.Actions);
    }

    [Fact]
    public void NeverMountsInCombat()
    {
        FakeBotState state = new() { RemainingDistance = 400f, IsInCombat = true };

        RootTree.HandleMounting(On).Tick(state);

        Assert.DoesNotContain("Mount", state.TravelState.Actions);
    }

    [Fact]
    public void DoesNotTryWhereTheClientWouldRefuse()
    {
        // Indoors, underwater, in a battleground before the gates open. Asking first is cheaper
        // than a failed cast per tick.
        FakeBotState state = new() { RemainingDistance = 400f };
        state.TravelState.CanMount = false;

        Assert.Equal(RunStatus.Failure, RootTree.HandleMounting(On).Tick(state));
        Assert.Empty(state.TravelState.Actions);
    }

    [Fact]
    public void GetsOffOnceTheJourneyIsNearlyDone()
    {
        FakeBotState state = new() { RemainingDistance = 10f };
        state.TravelState.IsMounted = true;

        Assert.Equal(RunStatus.Success, RootTree.HandleMounting(On).Tick(state));
        Assert.Contains("Dismount", state.TravelState.Actions);
    }

    [Fact]
    public void GetsOffForCombat()
    {
        // A mounted character cannot attack, so a bot that forgets to get off simply stops
        // working.
        FakeBotState state = new() { RemainingDistance = 400f, IsInCombat = true };
        state.TravelState.IsMounted = true;

        RootTree.HandleMounting(On).Tick(state);

        Assert.Contains("Dismount", state.TravelState.Actions);
    }

    [Fact]
    public void GetsOffForACorpseWorthLooting()
    {
        FakeBotState state = new() { RemainingDistance = 400f };
        state.TravelState.IsMounted = true;
        state.LootableCorpses = [state.AddEnemy(1, distance: 5f, alive: false)];

        RootTree.HandleMounting(On).Tick(state);

        Assert.Contains("Dismount", state.TravelState.Actions);
    }

    [Fact]
    public void GetsOffForACorpseWorthSkinning()
    {
        FakeBotState state = new() { RemainingDistance = 400f };
        state.TravelState.IsMounted = true;
        state.SkinnableCorpses = [state.AddEnemy(1, distance: 5f, alive: false)];

        RootTree.HandleMounting(On).Tick(state);

        Assert.Contains("Dismount", state.TravelState.Actions);
    }

    [Fact]
    public void StaysMountedForTheRestOfALongJourney()
    {
        FakeBotState state = new() { RemainingDistance = 400f };
        state.TravelState.IsMounted = true;

        Assert.Equal(RunStatus.Failure, RootTree.HandleMounting(On).Tick(state));
        Assert.Empty(state.TravelState.Actions);
    }

    [Fact]
    public void MountingOffMeansWalkingEverywhere()
    {
        // Slower, and never wrong.
        FakeBotState state = new() { RemainingDistance = 400f };

        Assert.Equal(RunStatus.Failure, RootTree.HandleMounting(null).Tick(state));
        Assert.Empty(state.TravelState.Actions);
    }

    [Fact]
    public void MountingNeverDelaysAFight()
    {
        FakeBotState state = new() { RemainingDistance = 400f, IsInCombat = true };
        state.Target = state.AddEnemy(1, targetingMe: true);

        RootTree.Build(new Do<IBotState>(_ => RunStatus.Success), travel: On).Tick(state);

        Assert.DoesNotContain("Mount", state.TravelState.Actions);
        Assert.Contains("Combat", state.RecordingRoutine.Calls);
    }
}
