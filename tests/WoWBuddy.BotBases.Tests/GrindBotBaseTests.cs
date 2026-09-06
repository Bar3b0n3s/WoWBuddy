using WoWBuddy.Behavior;
using WoWBuddy.BotBases;
using WoWBuddy.Common.Geometry;
using Xunit;

namespace WoWBuddy.BotBases.Tests;

public sealed class GrindBotBaseTests
{
    private static readonly Vector3 HotspotA = new(-8900f, 500f, 90f);
    private static readonly Vector3 HotspotB = new(-8700f, 500f, 90f);

    private static GrindSettings Settings(
        int minimumLevel = 60,
        int maximumLevel = 75,
        IReadOnlySet<uint>? avoid = null,
        IReadOnlyList<(Vector3, float)>? blackspots = null) =>
        new()
        {
            Hotspots = [HotspotA, HotspotB],
            MinimumLevel = minimumLevel,
            MaximumLevel = maximumLevel,
            AvoidEntries = avoid ?? new HashSet<uint>(),
            Blackspots = blackspots ?? [],
        };

    [Fact]
    public void PicksTheNearestAcceptableTarget()
    {
        // Nearest keeps the character inside its hotspot rather than being drawn across
        // the zone one mob at a time.
        var grind = new GrindBotBase(Settings());
        var state = new FakeBotState();
        state.AddEnemy(1, distance: 40f);
        CandidateTarget near = state.AddEnemy(2, distance: 10f);
        state.AddEnemy(3, distance: 25f);

        Assert.Equal(near.Guid, grind.SelectTarget(state)?.Guid);
    }

    [Fact]
    public void SkipsTargetsOutsideTheLevelRange()
    {
        var grind = new GrindBotBase(Settings(minimumLevel: 60, maximumLevel: 70));
        var state = new FakeBotState();
        state.AddEnemy(1, distance: 5f, level: 80);
        state.AddEnemy(2, distance: 8f, level: 20);
        CandidateTarget right = state.AddEnemy(3, distance: 30f, level: 65);

        Assert.Equal(right.Guid, grind.SelectTarget(state)?.Guid);
    }

    [Fact]
    public void SkipsAvoidedCreatures()
    {
        // The escape hatch for whatever turned out to kill the bot at three in the morning.
        var grind = new GrindBotBase(Settings(avoid: new HashSet<uint> { 1234 }));
        var state = new FakeBotState();
        state.AddEnemy(1, distance: 5f, entry: 1234);
        CandidateTarget other = state.AddEnemy(2, distance: 20f, entry: 999);

        Assert.Equal(other.Guid, grind.SelectTarget(state)?.Guid);
    }

    [Fact]
    public void SkipsFightsSomebodyElseStarted()
    {
        // Both rude and a good way to get reported.
        var grind = new GrindBotBase(Settings());
        var state = new FakeBotState();
        state.AddEnemy(1, distance: 5f, inCombat: true);
        CandidateTarget free = state.AddEnemy(2, distance: 30f);

        Assert.Equal(free.Guid, grind.SelectTarget(state)?.Guid);
    }

    [Fact]
    public void StillTakesAFightSomethingStartedWithTheCharacter()
    {
        var grind = new GrindBotBase(Settings());
        var state = new FakeBotState();
        CandidateTarget attacker = state.AddEnemy(1, distance: 5f, inCombat: true, targetingMe: true);

        Assert.Equal(attacker.Guid, grind.SelectTarget(state)?.Guid);
    }

    [Fact]
    public void SkipsTargetsStandingInABlackspot()
    {
        var blackspot = new Vector3(-8880f, 500f, 90f);
        var grind = new GrindBotBase(Settings(blackspots: [(blackspot, 30f)]));
        var state = new FakeBotState();
        state.AddEnemy(1, distance: 20f);

        Assert.Null(grind.SelectTarget(state));
    }

    [Fact]
    public void SkipsDeadCreatures()
    {
        var grind = new GrindBotBase(Settings());
        var state = new FakeBotState();
        state.AddEnemy(1, distance: 5f, alive: false);

        Assert.Null(grind.SelectTarget(state));
    }

    [Fact]
    public void ApproachesATargetThatIsOutOfPullRange()
    {
        var grind = new GrindBotBase(Settings());
        Node<IBotState> tree = grind.Build();
        var state = new FakeBotState();
        state.RecordingRoutine.PullRange = 5f;
        state.Target = state.AddEnemy(1, distance: 30f);

        Assert.Equal(RunStatus.Running, tree.Tick(state));
        Assert.Contains("MoveTo", state.Actions);
        Assert.DoesNotContain("Pull", state.RecordingRoutine.Calls);
    }

    [Fact]
    public void PullsOnceTheTargetIsInTheRoutinesRange()
    {
        // A caster opens from thirty yards; walking it into melee first would be wrong for
        // the class and slower besides.
        var grind = new GrindBotBase(Settings());
        Node<IBotState> tree = grind.Build();
        var state = new FakeBotState();
        state.RecordingRoutine.PullRange = 30f;
        state.Target = state.AddEnemy(1, distance: 25f);

        tree.Tick(state);

        Assert.Contains("Pull", state.RecordingRoutine.Calls);
        Assert.Contains("StopMoving", state.Actions);
        Assert.DoesNotContain("MoveTo", state.Actions);
    }

    [Fact]
    public void SelectsATargetWhenThereIsNoneAndSomethingIsNearby()
    {
        var grind = new GrindBotBase(Settings());
        Node<IBotState> tree = grind.Build();
        var state = new FakeBotState();
        CandidateTarget enemy = state.AddEnemy(1, distance: 20f);

        Assert.Equal(RunStatus.Success, tree.Tick(state));
        Assert.Contains($"SetTarget({enemy.Guid})", state.Actions);
    }

    [Fact]
    public void TravelsToAHotspotWhenThereIsNothingToKill()
    {
        var grind = new GrindBotBase(Settings());
        Node<IBotState> tree = grind.Build();
        var state = new FakeBotState { Position = new Vector3(-9500f, 500f, 90f) };

        Assert.Equal(RunStatus.Running, tree.Tick(state));
        Assert.Contains(HotspotA, state.MoveRequests);
    }

    [Fact]
    public void MovesOnWhenAHotspotIsExhausted()
    {
        // Standing at a hotspot with nothing to kill means waiting for a respawn; moving on
        // is nearly always better.
        var grind = new GrindBotBase(Settings());
        Node<IBotState> tree = grind.Build();
        var state = new FakeBotState { Position = HotspotA };

        tree.Tick(state);

        Assert.Contains(HotspotB, state.MoveRequests);
    }

    [Fact]
    public void MovesOnWhenAHotspotCannotBeReached()
    {
        var grind = new GrindBotBase(Settings());
        Node<IBotState> tree = grind.Build();
        var state = new FakeBotState
        {
            Position = new Vector3(-9500f, 500f, 90f),
            MovementFailed = true,
        };

        Assert.Equal(RunStatus.Failure, tree.Tick(state));
        Assert.Equal(HotspotB, grind.CurrentHotspot);
    }

    [Fact]
    public void HotspotsCycleRatherThanRunningOut()
    {
        var grind = new GrindBotBase(Settings());

        Assert.Equal(HotspotA, grind.CurrentHotspot);
        grind.AdvanceHotspot();
        Assert.Equal(HotspotB, grind.CurrentHotspot);
        grind.AdvanceHotspot();
        Assert.Equal(HotspotA, grind.CurrentHotspot);
    }

    [Fact]
    public void AProfileWithNoHotspotsSimplyFindsNothingToDo()
    {
        var grind = new GrindBotBase(new GrindSettings());
        Node<IBotState> tree = grind.Build();

        Assert.Equal(RunStatus.Failure, tree.Tick(new FakeBotState()));
    }
}
