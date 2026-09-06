using WoWBuddy.Behavior;
using WoWBuddy.BotBases;
using WoWBuddy.Common.Geometry;
using WoWBuddy.WorldData;
using Xunit;

namespace WoWBuddy.BotBases.Tests;

public sealed class GatherBotBaseTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Vector3 Origin = new(-8900f, 500f, 90f);

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "wowbuddy-gather", Guid.NewGuid().ToString("N"));

    public GatherBotBaseTests()
    {
        Directory.CreateDirectory(_directory);

        File.WriteAllText(
            Path.Combine(_directory, WorldDataSet.FileNames.GameObjectSpawns),
            string.Join('\n',
                "guid\tentry\tmap\tx\ty\tz",
                "1\t1617\t0\t-8890\t500\t90",   // 10 yards away
                "2\t1617\t0\t-8850\t500\t90",   // 50 yards away
                "3\t1618\t0\t-8895\t500\t90")); // a different node type
    }

    private WorldDataSet World()
    {
        var set = new WorldDataSet();
        set.LoadFrom(_directory);
        return set;
    }

    private GatherBotBase Base(GatherSettings? settings = null) =>
        new(settings ?? new GatherSettings { NodeEntries = new HashSet<uint> { 1617 } }, World());

    [Fact]
    public void PicksTheNearestNodeThatIsActuallyThere()
    {
        // The database says where nodes spawn; only the client knows whether one is up.
        GatherBotBase gather = Base();
        var state = new FakeBotState { Position = Origin };

        GameObjectSpawn? node = gather.SelectNode(state, new HashSet<uint> { 1617 }, Start);

        Assert.Equal(1u, node?.Guid);
    }

    [Fact]
    public void IgnoresSpawnPointsWhereNothingIsCurrentlyUp()
    {
        // Walking to an empty spawn point is the classic gathering-bot failure.
        GatherBotBase gather = Base();
        var state = new FakeBotState { Position = Origin };

        Assert.Null(gather.SelectNode(state, new HashSet<uint>(), Start));
    }

    [Fact]
    public void IgnoresNodeTypesTheProfileDoesNotWant()
    {
        GatherBotBase gather = Base();
        var state = new FakeBotState { Position = Origin };

        GameObjectSpawn? node = gather.SelectNode(state, new HashSet<uint> { 1618 }, Start);

        Assert.Null(node);
    }

    [Fact]
    public void LeavesANodeAloneAfterVisitingIt()
    {
        // Without this the bot loops between two points forever.
        GatherBotBase gather = Base();
        var state = new FakeBotState { Position = Origin };
        var visible = new HashSet<uint> { 1617 };

        GameObjectSpawn first = gather.SelectNode(state, visible, Start)!.Value;
        gather.NoteVisited(first.Guid, Start);

        GameObjectSpawn? second = gather.SelectNode(state, visible, Start.AddSeconds(1));

        Assert.NotEqual(first.Guid, second?.Guid);
    }

    [Fact]
    public void ReturnsToANodeOnceItsCooldownHasPassed()
    {
        GatherBotBase gather = Base(new GatherSettings
        {
            NodeEntries = new HashSet<uint> { 1617 },
            NodeCooldown = TimeSpan.FromMinutes(8),
        });

        var state = new FakeBotState { Position = Origin };
        var visible = new HashSet<uint> { 1617 };

        gather.NoteVisited(1, Start);
        Assert.NotEqual(1u, gather.SelectNode(state, visible, Start.AddMinutes(1))?.Guid);
        Assert.Equal(1u, gather.SelectNode(state, visible, Start.AddMinutes(10))?.Guid);
    }

    [Fact]
    public void SkipsNodesInsideABlackspot()
    {
        GatherBotBase gather = Base(new GatherSettings
        {
            NodeEntries = new HashSet<uint> { 1617 },
            Blackspots = [(new Vector3(-8890f, 500f, 90f), 20f)],
        });

        var state = new FakeBotState { Position = Origin };

        Assert.Equal(2u, gather.SelectNode(state, new HashSet<uint> { 1617 }, Start)?.Guid);
    }

    [Fact]
    public void GathersNothingWithoutAnyConfiguredNodeTypes()
    {
        // No node list ships with the bot; a profile or the user supplies it.
        GatherBotBase gather = Base(new GatherSettings());
        var state = new FakeBotState { Position = Origin };

        Assert.Null(gather.SelectNode(state, new HashSet<uint> { 1617 }, Start));
    }

    [Fact]
    public void WalksToANodeThatIsOutOfReach()
    {
        GatherBotBase gather = Base();
        Node<IBotState> tree = gather.Build(_ => new HashSet<uint> { 1617 });
        var state = new FakeBotState { Position = Origin, Now = Start };

        Assert.Equal(RunStatus.Running, tree.Tick(state));
        Assert.Contains("MoveTo", state.Actions);
    }

    [Fact]
    public void FollowsItsRouteWhenNothingIsUp()
    {
        var route = new List<Vector3> { new(-8800f, 500f, 90f), new(-8700f, 500f, 90f) };
        GatherBotBase gather = Base(new GatherSettings
        {
            NodeEntries = new HashSet<uint> { 1617 },
            Route = route,
        });

        Node<IBotState> tree = gather.Build(_ => new HashSet<uint>());
        var state = new FakeBotState { Position = Origin, Now = Start };

        Assert.Equal(RunStatus.Running, tree.Tick(state));
        Assert.Contains(route[0], state.MoveRequests);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
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
