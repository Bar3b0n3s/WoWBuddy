using WoWBuddy.Behavior;
using WoWBuddy.BotBases;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Objects;
using WoWBuddy.WorldData;
using Xunit;

namespace WoWBuddy.BotBases.Tests;

public sealed class GatherBotBaseTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Vector3 Origin = new(-8900f, 500f, 90f);

    private static GatherSettings Settings(
        IReadOnlyList<Vector3>? route = null,
        IReadOnlyList<(Vector3, float)>? blackspots = null) =>
        new()
        {
            NodeEntries = new HashSet<uint> { 1617 },
            Route = route ?? [],
            Blackspots = blackspots ?? [],
        };

    private static VisibleObject Node(ulong guid, uint entry, float distance, bool hasPosition = true) =>
        new(new WoWGuid(guid), entry, new Vector3(Origin.X + distance, Origin.Y, Origin.Z),
            hasPosition, distance);

    [Fact]
    public void WritesDownEveryGatherableNodeItWalksPast()
    {
        // The whole of the learning: see something once, know where it is next time. This is
        // what a user with no access to a server database actually relies on.
        var memory = new WorldMemory();
        var gather = new GatherBotBase(Settings(), memory);
        var state = new FakeBotState { Position = Origin, Now = Start };
        state.VisibleObjects = [Node(1, 1617, 20f), Node(2, 9999, 10f)];

        int learned = gather.LearnVisibleNodes(state);

        Assert.Equal(1, learned);
        Assert.Equal(1617u, memory.Places[0].Entry);
    }

    [Fact]
    public void RefusesToLearnANodeWhosePositionIsNotKnown()
    {
        // When the game object position offset could not be worked out at attach, positions
        // read as zero. Writing those down would teach the bot to walk to the map's centre.
        var memory = new WorldMemory();
        var gather = new GatherBotBase(Settings(), memory);
        var state = new FakeBotState { Position = Origin, Now = Start };
        state.VisibleObjects = [Node(1, 1617, 20f, hasPosition: false) with { Position = Vector3.Zero }];

        Assert.Equal(0, gather.LearnVisibleNodes(state));
        Assert.Equal(0, memory.Count);
    }

    [Fact]
    public void PicksTheNearestNodeActuallyInView()
    {
        var gather = new GatherBotBase(Settings(), new WorldMemory());
        var state = new FakeBotState { Position = Origin, Now = Start };
        state.VisibleObjects = [Node(1, 1617, 40f), Node(2, 1617, 10f)];

        Assert.Equal(2u, gather.SelectVisibleNode(state, Start)?.Guid.Value);
    }

    [Fact]
    public void IgnoresNodeTypesTheProfileDoesNotWant()
    {
        var gather = new GatherBotBase(Settings(), new WorldMemory());
        var state = new FakeBotState { Position = Origin, Now = Start };
        state.VisibleObjects = [Node(1, 9999, 5f)];

        Assert.Null(gather.SelectVisibleNode(state, Start));
    }

    [Fact]
    public void LeavesANodeAloneAfterEmptyingIt()
    {
        // Without this the bot loops between two points forever.
        var gather = new GatherBotBase(Settings(), new WorldMemory());
        var state = new FakeBotState { Position = Origin, Now = Start };
        state.VisibleObjects = [Node(1, 1617, 5f)];

        gather.NoteGathered(1, Start);

        Assert.Null(gather.SelectVisibleNode(state, Start.AddMinutes(1)));
        Assert.NotNull(gather.SelectVisibleNode(state, Start.AddMinutes(10)));
    }

    [Fact]
    public void SkipsNodesInsideABlackspot()
    {
        var gather = new GatherBotBase(
            Settings(blackspots: [(new Vector3(Origin.X + 10f, Origin.Y, Origin.Z), 20f)]),
            new WorldMemory());

        var state = new FakeBotState { Position = Origin, Now = Start };
        state.VisibleObjects = [Node(1, 1617, 10f)];

        Assert.Null(gather.SelectVisibleNode(state, Start));
    }

    [Fact]
    public void GoesBackToARememberedNodeWhenNothingIsInView()
    {
        // A remembered position says a node spawns there, not that one is up. Going to look
        // still beats walking a fixed route past nothing.
        var memory = new WorldMemory();
        memory.Remember(RememberedKind.Node, 1617, 0, new Vector3(-8850f, 500f, 90f), "", Start);

        var gather = new GatherBotBase(Settings(), memory);
        var state = new FakeBotState { Position = Origin, Now = Start };

        Assert.NotNull(gather.SelectRememberedNode(state, Start));
    }

    [Fact]
    public void IgnoresRememberedNodesTooFarToBeWorthIt()
    {
        var memory = new WorldMemory();
        memory.Remember(RememberedKind.Node, 1617, 0, new Vector3(-5000f, 500f, 90f), "", Start);

        var gather = new GatherBotBase(Settings(), memory);
        var state = new FakeBotState { Position = Origin, Now = Start };

        Assert.Null(gather.SelectRememberedNode(state, Start));
    }

    [Fact]
    public void GathersWhatIsInReach()
    {
        var gather = new GatherBotBase(Settings(), new WorldMemory());
        Node<IBotState> tree = gather.Build();
        var state = new FakeBotState { Position = Origin, Now = Start };
        state.VisibleObjects = [Node(1, 1617, 2f)];

        Assert.Equal(RunStatus.Running, tree.Tick(state));
        Assert.Contains(state.Actions, a => a.StartsWith("Interact(", StringComparison.Ordinal));
    }

    [Fact]
    public void WalksToANodeThatIsOutOfReach()
    {
        var gather = new GatherBotBase(Settings(), new WorldMemory());
        Node<IBotState> tree = gather.Build();
        var state = new FakeBotState { Position = Origin, Now = Start };
        state.VisibleObjects = [Node(1, 1617, 30f)];

        Assert.Equal(RunStatus.Running, tree.Tick(state));
        Assert.Contains("MoveTo", state.Actions);
    }

    [Fact]
    public void FollowsItsRouteWhenItKnowsOfNothingBetter()
    {
        var route = new List<Vector3> { new(-8800f, 500f, 90f), new(-8700f, 500f, 90f) };
        var gather = new GatherBotBase(Settings(route), new WorldMemory());
        Node<IBotState> tree = gather.Build();
        var state = new FakeBotState { Position = Origin, Now = Start };

        Assert.Equal(RunStatus.Running, tree.Tick(state));
        Assert.Contains(route[0], state.MoveRequests);
    }

    [Fact]
    public void LearnsWhileWalkingTheRouteRatherThanOnlyWhileGathering()
    {
        // A first lap is blind; by the third the bot knows the route.
        var memory = new WorldMemory();
        var gather = new GatherBotBase(
            Settings(route: [new Vector3(-8800f, 500f, 90f)]), memory);

        Node<IBotState> tree = gather.Build();
        var state = new FakeBotState { Position = Origin, Now = Start };
        state.VisibleObjects = [Node(1, 1617, 200f)];

        tree.Tick(state);

        Assert.Equal(1, memory.Count);
    }
}

public sealed class FishBotBaseTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DetectsABiteAsAChangeRatherThanAGuessedValue()
    {
        // The state at rest is recorded at cast, so a bite is a change from whatever this
        // client uses rather than equality with a value this project invented.
        var fish = new FishBotBase(new FishSettings());

        fish.NoteCast(Start, bobberState: 0);

        Assert.False(fish.HasBite(0));
        Assert.True(fish.HasBite(1));
    }

    [Fact]
    public void ReportsNoBiteWhenThereIsNoBobber()
    {
        var fish = new FishBotBase(new FishSettings());
        fish.NoteCast(Start, bobberState: 0);

        Assert.False(fish.HasBite(null));
    }

    [Fact]
    public void GivesUpOnACastThatHasBeenOutTooLong()
    {
        // A cast lasts about thirty seconds; waiting longer means watching a bobber that has
        // already gone.
        var fish = new FishBotBase(new FishSettings { BiteTimeout = TimeSpan.FromSeconds(25) });
        fish.NoteCast(Start, 0);

        Assert.False(fish.CastHasExpired(Start.AddSeconds(20)));
        Assert.True(fish.CastHasExpired(Start.AddSeconds(26)));
    }

    [Fact]
    public void ReappliesALureOnlyWhenItHasWornOff()
    {
        var fish = new FishBotBase(new FishSettings
        {
            LureName = "Shiny Bauble",
            LureDuration = TimeSpan.FromMinutes(10),
        });

        Assert.True(fish.NeedsLure(Start));

        fish.NoteLureApplied(Start);

        Assert.False(fish.NeedsLure(Start.AddMinutes(5)));
        Assert.True(fish.NeedsLure(Start.AddMinutes(11)));
    }

    [Fact]
    public void NeverNeedsALureWhenNoneIsConfigured()
    {
        var fish = new FishBotBase(new FishSettings { LureName = "" });

        Assert.False(fish.NeedsLure(Start));
    }

    [Fact]
    public void MovesOnAfterFishingASpotLongEnough()
    {
        var fish = new FishBotBase(new FishSettings
        {
            Spots = [new Vector3(1f, 1f, 1f), new Vector3(2f, 2f, 2f)],
            TimePerSpot = TimeSpan.FromMinutes(20),
        });

        fish.AdvanceSpot(Start);

        Assert.False(fish.ShouldMoveOn(Start.AddMinutes(10)));
        Assert.True(fish.ShouldMoveOn(Start.AddMinutes(21)));
    }

    [Fact]
    public void StaysPutWithOnlyOneSpot()
    {
        var fish = new FishBotBase(new FishSettings
        {
            Spots = [new Vector3(1f, 1f, 1f)],
            TimePerSpot = TimeSpan.FromMinutes(1),
        });

        fish.AdvanceSpot(Start);

        Assert.False(fish.ShouldMoveOn(Start.AddHours(1)));
    }

    [Fact]
    public void CastsWhenThereIsNothingElseToDo()
    {
        var fish = new FishBotBase(new FishSettings());
        int casts = 0;

        Node<IBotState> tree = fish.Build(
            bobberState: _ => 0,
            castLine: _ => { casts++; return true; },
            applyLure: _ => true,
            clickBobber: _ => true);

        Assert.Equal(RunStatus.Running, tree.Tick(new FakeBotState { Now = Start }));
        Assert.Equal(1, casts);
    }

    [Fact]
    public void ReelsInAsSoonAsSomethingBites()
    {
        // The window is a second or two; missing it wastes the whole cast.
        var fish = new FishBotBase(new FishSettings());
        byte state = 0;
        int clicks = 0;

        Node<IBotState> tree = fish.Build(
            bobberState: _ => state,
            castLine: _ => true,
            applyLure: _ => true,
            clickBobber: _ => { clicks++; return true; });

        var bot = new FakeBotState { Now = Start };
        tree.Tick(bot);

        state = 1;
        tree.Tick(bot);

        Assert.Equal(1, clicks);
        Assert.Equal(1, fish.Bites);
    }

    [Fact]
    public void RunningOutOfLuresSlowsFishingRatherThanStoppingIt()
    {
        var fish = new FishBotBase(new FishSettings
        {
            LureName = "Shiny Bauble",
            LureDuration = TimeSpan.FromMinutes(10),
        });

        Node<IBotState> tree = fish.Build(
            bobberState: _ => 0,
            castLine: _ => true,
            applyLure: _ => false,
            clickBobber: _ => true);

        var bot = new FakeBotState { Now = Start };
        tree.Tick(bot);

        // The clock is reset even on failure, so the next tick fishes instead of retrying
        // the lure forever.
        Assert.False(fish.NeedsLure(Start));
    }
}
