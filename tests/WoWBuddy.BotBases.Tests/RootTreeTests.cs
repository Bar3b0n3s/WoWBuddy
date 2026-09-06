using WoWBuddy.Behavior;
using WoWBuddy.BotBases;
using WoWBuddy.BotBases.Support;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Scheduling;
using Xunit;

namespace WoWBuddy.BotBases.Tests;

/// <summary>
/// Covers the order in which the bot considers things, which is the whole of its judgement.
/// </summary>
/// <remarks>
/// These are the situations that are hardest to stage deliberately in a live client and the
/// ones where an unattended bot actually goes wrong: dying at the wrong moment, being
/// attacked while walking somewhere, running out of mana mid-pull.
/// </remarks>
public sealed class RootTreeTests
{
    private static (BehaviorTree<IBotState> Tree, Recorder Base) BuildWithMarker()
    {
        var marker = new Recorder();
        return (RootTree.Build(marker), marker);
    }

    /// <summary>A stand-in bot base that records whether it was reached.</summary>
    private sealed class Recorder : Node<IBotState>
    {
        public int Ticks { get; private set; }

        protected override RunStatus OnTick(IBotState context)
        {
            Ticks++;
            return RunStatus.Running;
        }
    }

    [Fact]
    public void NothingRunsWhileTheWorldIsNotLoaded()
    {
        // Reading the object manager during a loading screen produces nonsense.
        (BehaviorTree<IBotState> tree, Recorder botBase) = BuildWithMarker();
        var state = new FakeBotState { IsInWorld = false };

        Assert.Equal(RunStatus.Running, tree.Tick(state));
        Assert.Equal(0, botBase.Ticks);
        Assert.Empty(state.Actions);
    }

    [Fact]
    public void DyingPreemptsEverythingElse()
    {
        (BehaviorTree<IBotState> tree, Recorder botBase) = BuildWithMarker();
        var state = new FakeBotState { IsDead = true, IsInCombat = true };
        state.AddEnemy(1, targetingMe: true);

        tree.Tick(state);

        Assert.Equal(0, botBase.Ticks);
        Assert.Contains("ReleaseCorpse", state.Actions);
    }

    [Fact]
    public void AGhostWalksToItsCorpseAndThenReclaimsIt()
    {
        (BehaviorTree<IBotState> tree, _) = BuildWithMarker();
        var state = new FakeBotState
        {
            IsDead = true,
            IsGhost = true,
            CorpsePosition = new Vector3(-8500f, 500f, 90f),
        };

        tree.Tick(state);
        Assert.Contains("MoveTo", state.Actions);

        // Standing on the corpse now.
        state.Position = state.CorpsePosition!.Value;
        state.Actions.Clear();
        tree.Tick(state);

        Assert.Contains("RetrieveCorpse", state.Actions);
    }

    [Fact]
    public void AGhostWithNoKnownCorpseWaitsRatherThanGrinding()
    {
        // Falling through to the bot base here would have a ghost trying to pull things.
        (BehaviorTree<IBotState> tree, Recorder botBase) = BuildWithMarker();
        var state = new FakeBotState { IsDead = true, IsGhost = true, CorpsePosition = null };

        Assert.Equal(RunStatus.Running, tree.Tick(state));
        Assert.Equal(0, botBase.Ticks);
    }

    [Fact]
    public void CombatPreemptsTheBotBase()
    {
        (BehaviorTree<IBotState> tree, Recorder botBase) = BuildWithMarker();
        var state = new FakeBotState { IsInCombat = true };
        CandidateTarget enemy = state.AddEnemy(1, targetingMe: true);
        state.Target = enemy;

        tree.Tick(state);

        Assert.Equal(0, botBase.Ticks);
        Assert.Contains("Combat", state.RecordingRoutine.Calls);
    }

    [Fact]
    public void SomethingAttackingIsTargetedRatherThanIgnored()
    {
        // Ignoring an attacker to keep hitting a different mob is how a bot dies with its
        // target still at ninety per cent.
        (BehaviorTree<IBotState> tree, _) = BuildWithMarker();
        var state = new FakeBotState { IsInCombat = true };
        CandidateTarget attacker = state.AddEnemy(7, targetingMe: true);

        tree.Tick(state);

        Assert.Contains($"SetTarget({attacker.Guid})", state.Actions);
    }

    [Fact]
    public void RestingHappensBeforeTheBotBaseStartsAnotherFight()
    {
        // A character that pulls at half health dies at a predictable rate.
        (BehaviorTree<IBotState> tree, Recorder botBase) = BuildWithMarker();
        var state = new FakeBotState();
        state.RecordingRoutine.Ready = false;
        state.RecordingRoutine.RestTicksRemaining = 3;

        tree.Tick(state);

        Assert.Equal(0, botBase.Ticks);
        Assert.Contains("Rest", state.RecordingRoutine.Calls);
        Assert.Contains("StartResting", state.Actions);
    }

    [Fact]
    public void RestingStopsTheCharacterMovingFirst()
    {
        (BehaviorTree<IBotState> tree, _) = BuildWithMarker();
        var state = new FakeBotState { IsMoving = true };
        state.RecordingRoutine.Ready = false;
        state.RecordingRoutine.RestTicksRemaining = 1;

        tree.Tick(state);

        Assert.Contains("StopMoving", state.Actions);
    }

    [Fact]
    public void RestingDoesNotHappenDuringAFight()
    {
        (BehaviorTree<IBotState> tree, _) = BuildWithMarker();
        var state = new FakeBotState { IsInCombat = true };
        state.RecordingRoutine.Ready = false;
        state.Target = state.AddEnemy(1);

        tree.Tick(state);

        Assert.DoesNotContain("Rest", state.RecordingRoutine.Calls);
        Assert.Contains("Combat", state.RecordingRoutine.Calls);
    }

    [Fact]
    public void TheBotBaseRunsWhenNothingHasGoneWrong()
    {
        (BehaviorTree<IBotState> tree, Recorder botBase) = BuildWithMarker();

        tree.Tick(new FakeBotState());

        Assert.Equal(1, botBase.Ticks);
    }

    [Fact]
    public void DyingMidFightHandsControlToTheDeathBranchOnTheNextTick()
    {
        // The reason the root is a priority selector rather than a sequence: a branch that
        // becomes relevant takes over rather than waiting for the current one to finish.
        (BehaviorTree<IBotState> tree, _) = BuildWithMarker();
        var state = new FakeBotState { IsInCombat = true };
        state.Target = state.AddEnemy(1);

        tree.Tick(state);
        Assert.Contains("Combat", state.RecordingRoutine.Calls);

        state.IsDead = true;
        state.Actions.Clear();
        tree.Tick(state);

        Assert.Contains("ReleaseCorpse", state.Actions);
    }
}

/// <summary>
/// Covers the branches added in phase 5 and where they sit relative to the others.
/// </summary>
public sealed class RootTreeSupportTests
{
    /// <summary>A stand-in bot base that records whether it was reached.</summary>
    private sealed class Marker : Node<IBotState>
    {
        public int Ticks { get; private set; }

        protected override RunStatus OnTick(IBotState context)
        {
            Ticks++;
            return RunStatus.Running;
        }
    }

    /// <summary>A node that reports whatever a test tells it to, and counts its ticks.</summary>
    private sealed class Handler(RunStatus status) : Node<IBotState>
    {
        public int Ticks { get; private set; }

        protected override RunStatus OnTick(IBotState context)
        {
            Ticks++;
            return status;
        }
    }

    private static (BehaviorTree<IBotState> Tree, Marker Base) Build(
        ErrandPlanner? errands = null,
        Node<IBotState>? errandHandler = null)
    {
        var marker = new Marker();
        return (RootTree.Build(marker, errands, errandHandler), marker);
    }

    [Fact]
    public void ABreakStopsTheCharacterAndSuspendsEverythingElse()
    {
        (BehaviorTree<IBotState> tree, Marker botBase) = Build();
        var state = new FakeBotState { Session = SessionState.OnBreak, IsMoving = true };
        state.AddEnemy(1);

        Assert.Equal(RunStatus.Running, tree.Tick(state));
        Assert.Contains("StopMoving", state.Actions);
        Assert.Equal(0, botBase.Ticks);
    }

    [Fact]
    public void ACharacterStillRecoversFromDeathDuringABreak()
    {
        // Spending a ten-minute break lying dead just means resuming to a corpse run that
        // could have been done already.
        (BehaviorTree<IBotState> tree, _) = Build();
        var state = new FakeBotState { Session = SessionState.OnBreak, IsDead = true };

        tree.Tick(state);

        Assert.Contains("ReleaseCorpse", state.Actions);
    }

    [Fact]
    public void ACharacterStillDefendsItselfDuringABreak()
    {
        // Standing still while something eats you is not a break, it is dying.
        (BehaviorTree<IBotState> tree, _) = Build();
        var state = new FakeBotState { Session = SessionState.OnBreak, IsInCombat = true };
        state.Target = state.AddEnemy(1, targetingMe: true);

        tree.Tick(state);

        Assert.Contains("Combat", state.RecordingRoutine.Calls);
    }

    [Fact]
    public void ABreakStopsTheBotStartingAnythingNew()
    {
        // Below the break: looting, eating, errands, looking for the next fight.
        (BehaviorTree<IBotState> tree, Marker botBase) = Build();
        var state = new FakeBotState { Session = SessionState.OnBreak };
        state.AddCorpse(5, distance: 2f);
        state.RecordingRoutine.Ready = false;

        tree.Tick(state);

        Assert.DoesNotContain(state.Actions, a => a.StartsWith("Loot(", StringComparison.Ordinal));
        Assert.DoesNotContain("Rest", state.RecordingRoutine.Calls);
        Assert.Equal(0, botBase.Ticks);
    }

    [Fact]
    public void CorpsesAreLootedOnceTheFightIsOver()
    {
        (BehaviorTree<IBotState> tree, Marker botBase) = Build();
        var state = new FakeBotState();
        CandidateTarget corpse = state.AddCorpse(5, distance: 2f);

        Assert.Equal(RunStatus.Running, tree.Tick(state));
        Assert.Contains($"Loot({corpse.Guid})", state.Actions);
        Assert.Equal(0, botBase.Ticks);
    }

    [Fact]
    public void TheCharacterWalksToACorpseOutOfLootRange()
    {
        (BehaviorTree<IBotState> tree, _) = Build();
        var state = new FakeBotState();
        state.AddCorpse(5, distance: 20f);

        tree.Tick(state);

        Assert.Contains("MoveTo", state.Actions);
        Assert.DoesNotContain(state.Actions, a => a.StartsWith("Loot(", StringComparison.Ordinal));
    }

    [Fact]
    public void LootingIsInterruptedByBeingAttacked()
    {
        (BehaviorTree<IBotState> tree, _) = Build();
        var state = new FakeBotState { IsInCombat = true };
        state.AddCorpse(5, distance: 2f);
        state.Target = state.AddEnemy(1);

        tree.Tick(state);

        Assert.DoesNotContain(state.Actions, a => a.StartsWith("Loot(", StringComparison.Ordinal));
        Assert.Contains("Combat", state.RecordingRoutine.Calls);
    }

    [Fact]
    public void LootingHappensBeforeRestingBecauseCorpsesExpire()
    {
        // The character's health does not expire on a timer; the corpse does.
        (BehaviorTree<IBotState> tree, _) = Build();
        var state = new FakeBotState();
        state.RecordingRoutine.Ready = false;
        state.RecordingRoutine.RestTicksRemaining = 5;
        CandidateTarget corpse = state.AddCorpse(5, distance: 2f);

        tree.Tick(state);

        Assert.Contains($"Loot({corpse.Guid})", state.Actions);
        Assert.DoesNotContain("Rest", state.RecordingRoutine.Calls);
    }

    [Fact]
    public void AnErrandIsRunWhenOneIsDue()
    {
        var planner = new ErrandPlanner(new ErrandSettings { TrainingEnabled = false });
        (BehaviorTree<IBotState> tree, Marker botBase) = Build(planner, new Handler(RunStatus.Running));
        var state = new FakeBotState { Inventory = new InventoryState(0, 16, 20d, 100_000) };

        Assert.Equal(RunStatus.Running, tree.Tick(state));
        Assert.Contains($"BeginErrand({Errand.Repair})", state.Actions);
        Assert.Equal(0, botBase.Ticks);
    }

    [Fact]
    public void AnErrandThatFinishesHandsControlBackInsteadOfHoldingTheBotForever()
    {
        // The defect this replaced: an errand began, the branch reported Running, and nothing
        // anywhere ever cleared it — so the character stood still for the rest of the night the
        // first time its bags filled.
        var planner = new ErrandPlanner(new ErrandSettings { TrainingEnabled = false });
        var handler = new Handler(RunStatus.Success);
        (BehaviorTree<IBotState> tree, Marker botBase) = Build(planner, handler);
        var state = new FakeBotState { Inventory = new InventoryState(0, 16, 20d, 100_000) };

        tree.Tick(state);

        Assert.Equal(1, handler.Ticks);
        Assert.Contains($"EndErrand({Errand.Repair})", state.Actions);
        Assert.Equal(Errand.None, state.CurrentErrand);

        // And the bot base gets this tick rather than the character standing still until the
        // next one.
        Assert.Equal(1, botBase.Ticks);
    }

    [Fact]
    public void AnErrandTheHandlerGivesUpOnIsAbandonedRatherThanRetriedForever()
    {
        var planner = new ErrandPlanner(new ErrandSettings { TrainingEnabled = false });
        (BehaviorTree<IBotState> tree, Marker botBase) = Build(planner, new Handler(RunStatus.Failure));
        var state = new FakeBotState { Inventory = new InventoryState(0, 16, 20d, 100_000) };

        tree.Tick(state);

        Assert.Equal(Errand.None, state.CurrentErrand);
        Assert.Equal(1, botBase.Ticks);
    }

    [Fact]
    public void ErrandsAreNotStartedWhenNothingCanCarryThemOut()
    {
        // Deciding an errand is due and having no way to run it is how the bot deadlocked.
        // With no handler the branch is off entirely and the bot base keeps playing.
        var planner = new ErrandPlanner(new ErrandSettings { TrainingEnabled = false });
        (BehaviorTree<IBotState> tree, Marker botBase) = Build(planner, errandHandler: null);
        var state = new FakeBotState { Inventory = new InventoryState(0, 16, 20d, 100_000) };

        tree.Tick(state);

        Assert.DoesNotContain(state.Actions, a => a.StartsWith("BeginErrand", StringComparison.Ordinal));
        Assert.Equal(1, botBase.Ticks);
    }

    [Fact]
    public void AnErrandInProgressKeepsRunningAcrossTicks()
    {
        var planner = new ErrandPlanner(new ErrandSettings { TrainingEnabled = false });
        var handler = new Handler(RunStatus.Running);
        (BehaviorTree<IBotState> tree, _) = Build(planner, handler);
        var state = new FakeBotState { Inventory = new InventoryState(0, 16, 20d, 100_000) };

        tree.Tick(state);
        tree.Tick(state);
        tree.Tick(state);

        Assert.Equal(3, handler.Ticks);

        // Begun once, not once per tick.
        Assert.Single(state.Actions, a => a.StartsWith("BeginErrand", StringComparison.Ordinal));
    }

    [Fact]
    public void FullBagsOfWorthlessThingsAreNotAReasonToWalkToTown()
    {
        // A bot that goes anyway makes the trip over and over without ever freeing a slot.
        var planner = new ErrandPlanner(new ErrandSettings { TrainingEnabled = false });
        (BehaviorTree<IBotState> tree, Marker botBase) = Build(planner, new Handler(RunStatus.Running));
        var state = new FakeBotState
        {
            Inventory = new InventoryState(0, 16, 100d, 100_000),
            HasSellableItems = false,
        };

        tree.Tick(state);

        Assert.DoesNotContain(state.Actions, a => a.StartsWith("BeginErrand", StringComparison.Ordinal));
        Assert.Equal(1, botBase.Ticks);
    }

    [Fact]
    public void TheBotCarriesOnWhenItDoesNotKnowWhereToRunAnErrand()
    {
        // The normal state before profiles supply vendor locations. Stalling here would stop
        // the bot doing anything at all.
        var planner = new ErrandPlanner(new ErrandSettings { TrainingEnabled = false });
        (BehaviorTree<IBotState> tree, Marker botBase) = Build(planner, new Handler(RunStatus.Running));
        var state = new FakeBotState
        {
            Inventory = new InventoryState(0, 16, 20d, 100_000),
            KnowsWhereErrandsAre = false,
        };

        tree.Tick(state);

        Assert.Equal(1, botBase.Ticks);
    }

    [Fact]
    public void ErrandsDoNotInterruptAFight()
    {
        var planner = new ErrandPlanner(new ErrandSettings { TrainingEnabled = false });
        (BehaviorTree<IBotState> tree, _) = Build(planner, new Handler(RunStatus.Running));
        var state = new FakeBotState
        {
            IsInCombat = true,
            Inventory = new InventoryState(0, 16, 5d, 100_000),
        };
        state.Target = state.AddEnemy(1);

        tree.Tick(state);

        Assert.DoesNotContain(state.Actions, a => a.StartsWith("BeginErrand", StringComparison.Ordinal));
        Assert.Contains("Combat", state.RecordingRoutine.Calls);
    }

    [Fact]
    public void RestingHappensBeforeAnErrandSoTheCharacterDoesNotArriveDead()
    {
        var planner = new ErrandPlanner(new ErrandSettings { TrainingEnabled = false });
        (BehaviorTree<IBotState> tree, _) = Build(planner, new Handler(RunStatus.Running));
        var state = new FakeBotState { Inventory = new InventoryState(0, 16, 20d, 100_000) };
        state.RecordingRoutine.Ready = false;
        state.RecordingRoutine.RestTicksRemaining = 5;

        tree.Tick(state);

        Assert.Contains("Rest", state.RecordingRoutine.Calls);
        Assert.DoesNotContain(state.Actions, a => a.StartsWith("BeginErrand", StringComparison.Ordinal));
    }

    [Fact]
    public void ErrandsAreSkippedEntirelyWhenNoPlannerIsSupplied()
    {
        (BehaviorTree<IBotState> tree, Marker botBase) = Build(errands: null);
        var state = new FakeBotState { Inventory = new InventoryState(0, 16, 1d, 100_000) };

        tree.Tick(state);

        Assert.Equal(1, botBase.Ticks);
    }
}
