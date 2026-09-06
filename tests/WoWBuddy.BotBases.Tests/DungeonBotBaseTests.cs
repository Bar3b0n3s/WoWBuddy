using WoWBuddy.Behavior;
using WoWBuddy.BotBases.Group;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Objects;
using Xunit;

namespace WoWBuddy.BotBases.Tests;

public sealed class DungeonBotBaseTests
{
    [Fact]
    public void SaysSoWhenItIsRunningWithoutAGroup()
    {
        FakeBotState state = new();

        Assert.Equal(RunStatus.Running, new DungeonBotBase().Build().Tick(state));
        Assert.Contains("StopMoving", state.Actions);
    }

    [Fact]
    public void AttacksWhatTheTankIsAttacking()
    {
        // The single most important rule in group play. A damage character that picks its own
        // targets breaks crowd control and pulls the next room, in front of four people who
        // will notice.
        FakeBotState state = new();
        state.PartyState.MyRole = PartyRole.Damage;

        CandidateTarget wanted = state.AddEnemy(1, distance: 25f, entry: 100);
        state.AddEnemy(2, distance: 5f, entry: 200);

        state.PartyState.Add(0x11, "Tank", role: PartyRole.Tank, targetGuid: wanted.Guid);

        new DungeonBotBase().Build().Tick(state);

        Assert.Equal(wanted.Guid, state.Target!.Value.Guid);
    }

    [Fact]
    public void AttacksNothingOfItsOwnWhenTheTankIsAttackingNothing()
    {
        // Nothing at all is the answer that keeps groups alive.
        FakeBotState state = new();
        state.PartyState.MyRole = PartyRole.Damage;
        state.AddEnemy(1, distance: 5f);
        state.PartyState.Add(0x11, "Tank", role: PartyRole.Tank);

        new DungeonBotBase().Build().Tick(state);

        Assert.Null(state.Target);
    }

    [Fact]
    public void DefendsItselfWhenSomethingIsAlreadyHittingIt()
    {
        // Standing there being eaten helps nobody.
        FakeBotState state = new();
        state.PartyState.MyRole = PartyRole.Damage;

        CandidateTarget attacker = state.AddEnemy(1, distance: 4f, targetingMe: true);
        state.PartyState.Add(0x11, "Tank", role: PartyRole.Tank);

        new DungeonBotBase().Build().Tick(state);

        Assert.Equal(attacker.Guid, state.Target!.Value.Guid);
    }

    [Fact]
    public void FallsBackToTheLeaderWhenNobodyIsMarkedAsTank()
    {
        // Most groups have not been told their roles.
        FakeBotState state = new();
        state.PartyState.MyRole = PartyRole.Damage;

        CandidateTarget wanted = state.AddEnemy(1, distance: 25f);
        state.PartyState.Add(0x11, "Leader", leader: true, targetGuid: wanted.Guid);

        new DungeonBotBase().Build().Tick(state);

        Assert.Equal(wanted.Guid, state.Target!.Value.Guid);
    }

    [Fact]
    public void IgnoresWhatTheAnchorTargetsWhenItIsNotAnEnemy()
    {
        // A tank talking to a friendly NPC is not a reason to attack anything.
        FakeBotState state = new();
        state.PartyState.MyRole = PartyRole.Damage;
        state.PartyState.Add(0x11, "Tank", role: PartyRole.Tank,
            targetGuid: new WoWGuid(0xF130000000009999));

        new DungeonBotBase().Build().Tick(state);

        Assert.Null(state.Target);
    }

    [Fact]
    public void ATankPicksItsOwnTargets()
    {
        FakeBotState state = new();
        state.PartyState.MyRole = PartyRole.Tank;

        CandidateTarget nearest = state.AddEnemy(1, distance: 10f);
        state.AddEnemy(2, distance: 25f);
        state.PartyState.Add(0x11, "Healer", role: PartyRole.Healer, distance: 8f);

        new DungeonBotBase().Build().Tick(state);

        Assert.Equal(nearest.Guid, state.Target!.Value.Guid);
    }

    [Fact]
    public void ATankWaitsForTheGroupBeforePulling()
    {
        // Pulling with the healer three rooms back is the most reliable way to wipe a group,
        // and it is entirely avoidable: waiting costs seconds, a wipe costs a run.
        FakeBotState state = new();
        state.PartyState.MyRole = PartyRole.Tank;
        state.AddEnemy(1, distance: 10f);
        state.PartyState.Add(0x11, "Healer", role: PartyRole.Healer, distance: 90f);

        Assert.Equal(RunStatus.Running, new DungeonBotBase().Build().Tick(state));
        Assert.Null(state.Target);
        Assert.Contains("StopMoving", state.Actions);
    }

    [Fact]
    public void ATankDoesNotWaitForTheDead()
    {
        // Waiting for a corpse run before every pull would stop the run entirely, and the
        // group's own decision to keep going is not the bot's to override.
        FakeBotState state = new();
        state.PartyState.MyRole = PartyRole.Tank;
        CandidateTarget enemy = state.AddEnemy(1, distance: 10f);
        state.PartyState.Add(0x11, "Ghost", distance: 300f, alive: false);
        state.PartyState.Add(0x22, "Healer", role: PartyRole.Healer, distance: 8f);

        new DungeonBotBase().Build().Tick(state);

        Assert.Equal(enemy.Guid, state.Target!.Value.Guid);
    }

    [Fact]
    public void ATankWalksItsRouteWhenThereIsNothingToFight()
    {
        Vector3 first = new(100f, 100f, 50f);
        Vector3 second = new(200f, 100f, 50f);

        DungeonBotBase dungeon = new(new DungeonSettings { Route = [first, second] });

        FakeBotState state = new() { Position = new Vector3(0f, 0f, 50f) };
        state.PartyState.MyRole = PartyRole.Tank;
        state.PartyState.Add(0x11, "Healer", role: PartyRole.Healer, distance: 8f);

        Assert.Equal(RunStatus.Running, dungeon.Build().Tick(state));
        Assert.Contains(first, state.MoveRequests);

        // Standing on a point moves on to the next.
        state.Position = first;
        state.MoveRequests.Clear();

        dungeon.Build().Tick(state);
        Assert.Contains(second, state.MoveRequests);
    }

    [Fact]
    public void ATankWithoutARouteFollowsInsteadOfGuessing()
    {
        // The bot does not know where the instance goes — that is per-dungeon data it does not
        // ship — and guessing means walking a party into a wall.
        FakeBotState state = new();
        state.PartyState.MyRole = PartyRole.Tank;
        state.PartyState.Add(0x11, "Leader", leader: true, distance: 40f);

        Assert.Equal(RunStatus.Running, new DungeonBotBase().Build().Tick(state));
        Assert.Contains(state.PartyState.Actions, action => action.StartsWith("Follow", StringComparison.Ordinal));
    }

    [Fact]
    public void AHealerStandsAtRangeRatherThanOnTopOfTheTank()
    {
        DungeonBotBase dungeon = new(new DungeonSettings { StandOffRange = 25f });

        FakeBotState state = new() { Position = new Vector3(0f, 0f, 50f) };
        state.PartyState.MyRole = PartyRole.Healer;
        state.PartyState.Add(0x11, "Tank", role: PartyRole.Tank, distance: 60f,
            position: new Vector3(60f, 0f, 50f));

        Assert.Equal(RunStatus.Running, dungeon.Build().Tick(state));

        Vector3 destination = Assert.Single(state.MoveRequests);
        Assert.Equal(25f, new Vector3(60f, 0f, 50f).Distance2D(destination), 1);
    }

    [Fact]
    public void AHealerAlreadyInRangeStaysStillAndCasts()
    {
        // A healer that repositions every tick is not casting.
        DungeonBotBase dungeon = new(new DungeonSettings { StandOffRange = 25f });

        FakeBotState state = new();
        state.PartyState.MyRole = PartyRole.Healer;
        state.PartyState.Add(0x11, "Tank", role: PartyRole.Tank, distance: 15f);

        Assert.Equal(RunStatus.Running, dungeon.Build().Tick(state));
        Assert.Empty(state.MoveRequests);
        Assert.Contains("StopMoving", state.Actions);
    }

    [Fact]
    public void UsesTheClientsOwnFollowWhenItCan()
    {
        // It keeps a sensible distance, handles doorways, and looks like a person following
        // someone. Pathing to a moving target does none of that.
        FakeBotState state = new();
        state.PartyState.MyRole = PartyRole.Damage;
        PartyMember leader = state.PartyState.Add(0x11, "Leader", leader: true, distance: 40f);

        new DungeonBotBase().Build().Tick(state);

        Assert.Contains($"Follow({leader.Guid})", state.PartyState.Actions);
        Assert.Empty(state.MoveRequests);
    }

    [Fact]
    public void PathsToTheAnchorWhenTheClientsFollowRefuses()
    {
        FakeBotState state = new();
        state.PartyState.MyRole = PartyRole.Damage;
        state.PartyState.CanFollow = false;
        PartyMember leader = state.PartyState.Add(0x11, "Leader", leader: true, distance: 40f);

        new DungeonBotBase().Build().Tick(state);

        Assert.Contains(leader.Position, state.MoveRequests);
    }

    [Fact]
    public void StopsWhenTheGroupHasGoneSomewhereItCannotWalk()
    {
        FakeBotState state = new();
        state.PartyState.MyRole = PartyRole.Damage;
        state.PartyState.Add(0x11, "Leader", leader: true, distance: 900f);

        Assert.Equal(RunStatus.Failure, new DungeonBotBase().Build().Tick(state));
        Assert.Contains("StopMoving", state.Actions);
        Assert.Empty(state.MoveRequests);
    }

    [Fact]
    public void LeavesAloneWhatTheSettingsSayToAvoid()
    {
        DungeonBotBase dungeon = new(new DungeonSettings
        {
            AvoidEntries = new HashSet<uint> { 200 },
        });

        FakeBotState state = new();
        state.PartyState.MyRole = PartyRole.Tank;
        state.AddEnemy(1, distance: 5f, entry: 200);
        CandidateTarget allowed = state.AddEnemy(2, distance: 20f, entry: 100);
        state.PartyState.Add(0x11, "Healer", role: PartyRole.Healer, distance: 8f);

        dungeon.Build().Tick(state);

        Assert.Equal(allowed.Guid, state.Target!.Value.Guid);
    }
}
