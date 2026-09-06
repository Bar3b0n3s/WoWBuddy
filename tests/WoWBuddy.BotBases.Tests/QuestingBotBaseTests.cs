using WoWBuddy.Behavior;
using WoWBuddy.BotBases.Questing;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Profiles;
using Xunit;

namespace WoWBuddy.BotBases.Tests;

public sealed class QuestingBotBaseTests
{
    private static readonly Vector3 Giver = new(1500f, -2500f, 60f);
    private static readonly Vector3 Field = new(1560f, -2460f, 60f);

    private static Profile Quest(params ProfileStep[] steps) => new() { Name = "test", Steps = steps };

    [Fact]
    public void WalksToAQuestGiverThatIsNotInSight()
    {
        QuestingBotBase questing = new(Quest(
            new ProfileStep(StepKind.PickUp, QuestId: 9001, Entry: 8100, Position: Giver)));

        FakeBotState state = new() { Position = Field };

        Assert.Equal(RunStatus.Running, questing.Build().Tick(state));
        Assert.Contains(Giver, state.MoveRequests);
    }

    [Fact]
    public void WalksToAQuestGiverItCanSee()
    {
        QuestingBotBase questing = new(Quest(
            new ProfileStep(StepKind.PickUp, QuestId: 9001, Entry: 8100, Position: Giver)));

        FakeBotState state = new();
        VisibleObject giver = state.AddVisible(1, entry: 8100, distance: 30f);

        Assert.Equal(RunStatus.Running, questing.Build().Tick(state));
        Assert.Contains(giver.Position, state.MoveRequests);
    }

    [Fact]
    public void TakesTheQuestOnceItIsStandingAtTheGiver()
    {
        QuestingBotBase questing = new(Quest(
            new ProfileStep(StepKind.PickUp, QuestId: 9001, Entry: 8100, Position: Giver)));

        FakeBotState state = new();
        VisibleObject giver = state.AddVisible(1, entry: 8100, distance: 2f);

        Assert.Equal(RunStatus.Success, questing.Build().Tick(state));
        Assert.Contains($"Interact({giver.Guid})", state.Actions);
        Assert.Contains("Accept(9001)", state.QuestLog.Actions);
        Assert.True(state.QuestLog.IsInLog(9001));
    }

    [Fact]
    public void KeepsTalkingWhileTheQuestWindowIsStillOpening()
    {
        QuestingBotBase questing = new(Quest(
            new ProfileStep(StepKind.PickUp, QuestId: 9001, Entry: 8100, Position: Giver)));

        FakeBotState state = new();
        state.AddVisible(1, entry: 8100, distance: 2f);
        state.QuestLog.AcceptResult = QuestGiverResult.NotReady;

        // A slow window must not be read as a refusal: doing so would make the bot walk away
        // from quests it could have taken.
        Assert.Equal(RunStatus.Running, questing.Build().Tick(state));
        Assert.DoesNotContain("MarkCompleted(9001)", state.QuestLog.Actions);
    }

    [Fact]
    public void AQuestTheGiverDoesNotHaveIsRecordedAsDone()
    {
        QuestingBotBase questing = new(Quest(
            new ProfileStep(StepKind.PickUp, QuestId: 9001, Entry: 8100, Position: Giver),
            new ProfileStep(StepKind.RunTo, Position: Field)));

        FakeBotState state = new();
        state.AddVisible(1, entry: 8100, distance: 2f);
        state.QuestLog.AcceptResult = QuestGiverResult.NotOffered;

        // On 3.3.5a there is no way to ask whether a character did a quest years ago, so
        // finding the giver empty is how the bot learns it. Without recording that, it would
        // walk back to this NPC every tick for the rest of the session.
        questing.Build().Tick(state);

        Assert.Contains("MarkCompleted(9001)", state.QuestLog.Actions);
        Assert.Equal(StepKind.RunTo, questing.Current(state).Step!.Kind);
    }

    [Fact]
    public void AFullQuestLogStopsItTakingMore()
    {
        QuestingBotBase questing = new(Quest(
            new ProfileStep(StepKind.PickUp, QuestId: 9001, Entry: 8100, Position: Giver)));

        FakeBotState state = new();
        state.AddVisible(1, entry: 8100, distance: 2f);
        state.QuestLog.Fill();

        Assert.Equal(RunStatus.Failure, questing.Build().Tick(state));
        Assert.DoesNotContain("Accept(9001)", state.QuestLog.Actions);
    }

    [Fact]
    public void HandsInAFinishedQuestAndTakesTheRewardTheProfileNames()
    {
        QuestingBotBase questing = new(Quest(
            new ProfileStep(StepKind.PickUp, QuestId: 9001, Entry: 8100, Position: Giver),
            new ProfileStep(StepKind.TurnIn, QuestId: 9001, Entry: 8100, Position: Giver, RewardIndex: 2)));

        FakeBotState state = new();
        state.AddVisible(1, entry: 8100, distance: 2f);
        state.QuestLog.Add(9001, complete: true);

        Assert.Equal(RunStatus.Success, questing.Build().Tick(state));
        Assert.Contains("TurnIn(9001, reward 2)", state.QuestLog.Actions);
    }

    [Fact]
    public void DoesNotTryToHandInAQuestThatIsNotFinished()
    {
        QuestingBotBase questing = new(Quest(
            new ProfileStep(StepKind.PickUp, QuestId: 9001, Entry: 8100, Position: Giver),
            new ProfileStep(StepKind.TurnIn, QuestId: 9001, Entry: 8100, Position: Giver)));

        FakeBotState state = new();
        state.AddVisible(1, entry: 8100, distance: 2f);
        state.QuestLog.Add(9001, complete: false);

        Assert.Equal(RunStatus.Failure, questing.Build().Tick(state));
        Assert.DoesNotContain(state.QuestLog.Actions, action => action.StartsWith("TurnIn", StringComparison.Ordinal));
    }

    [Fact]
    public void TargetsTheCreatureAKillObjectiveNames()
    {
        QuestingBotBase questing = new(Quest(
            new ProfileStep(StepKind.Objective, QuestId: 9001, Entry: 8200, Position: Field,
                Objective: ObjectiveKind.Kill, Radius: 80f)));

        FakeBotState state = new() { Position = Field };
        state.AddEnemy(1, distance: 40f, entry: 9999);
        CandidateTarget wanted = state.AddEnemy(2, distance: 50f, entry: 8200);
        state.QuestLog.Add(9001);

        Assert.Equal(RunStatus.Running, questing.Build().Tick(state));
        Assert.Equal(wanted.Guid, state.Target!.Value.Guid);
    }

    [Fact]
    public void LeavesAloneWhatTheProfileSaysToAvoid()
    {
        Profile profile = new()
        {
            Steps =
            [
                new ProfileStep(StepKind.Objective, QuestId: 9001, Entry: 8200, Position: Field,
                    Objective: ObjectiveKind.Kill, Radius: 80f),
            ],
            AvoidMobs = new HashSet<uint> { 8200 },
        };

        FakeBotState state = new() { Position = Field };
        state.AddEnemy(1, distance: 20f, entry: 8200);
        state.QuestLog.Add(9001);

        new QuestingBotBase(profile).Build().Tick(state);

        Assert.Null(state.Target);
    }

    [Fact]
    public void MovesBetweenHotspotsWhenNothingIsInSight()
    {
        Vector3 far = new(1700f, -2300f, 62f);

        QuestingBotBase questing = new(Quest(
            new ProfileStep(StepKind.Objective, QuestId: 9001, Entry: 8200,
                Objective: ObjectiveKind.Kill, Radius: 30f, Hotspots: [Field, far])));

        FakeBotState state = new() { Position = Field };
        state.QuestLog.Add(9001);

        // Standing on one hotspot with nothing to kill: head for the other rather than wait
        // for a respawn.
        Assert.Equal(RunStatus.Running, questing.Build().Tick(state));
        Assert.Contains(far, state.MoveRequests);
    }

    [Fact]
    public void InteractsWithWhatAnInteractObjectiveNames()
    {
        QuestingBotBase questing = new(Quest(
            new ProfileStep(StepKind.Objective, QuestId: 9001, Entry: 8700, Position: Field,
                Objective: ObjectiveKind.Interact)));

        FakeBotState state = new() { Position = Field };
        VisibleObject thing = state.AddVisible(1, entry: 8700, distance: 2f);
        state.QuestLog.Add(9001);

        Assert.Equal(RunStatus.Running, questing.Build().Tick(state));
        Assert.Contains($"Interact({thing.Guid})", state.Actions);
    }

    [Fact]
    public void UsesTheItemAUseItemObjectiveNames()
    {
        QuestingBotBase questing = new(Quest(
            new ProfileStep(StepKind.Objective, QuestId: 9001, ItemId: 8600, Position: Field,
                Objective: ObjectiveKind.UseItem)));

        FakeBotState state = new() { Position = Field };
        state.Items[8600] = 1;
        state.QuestLog.Add(9001);

        Assert.Equal(RunStatus.Running, questing.Build().Tick(state));
        Assert.Contains("UseItem(8600)", state.Actions);
    }

    [Fact]
    public void SaysSoWhenTheItemAnObjectiveNeedsIsNotCarried()
    {
        QuestingBotBase questing = new(Quest(
            new ProfileStep(StepKind.Objective, QuestId: 9001, ItemId: 8600, Position: Field,
                Objective: ObjectiveKind.UseItem)));

        FakeBotState state = new() { Position = Field };
        state.QuestLog.Add(9001);

        Assert.Equal(RunStatus.Failure, questing.Build().Tick(state));
        Assert.DoesNotContain("UseItem(8600)", state.Actions);
    }

    [Fact]
    public void AnObjectiveItCannotDescribeWorksTheAreaAndWaitsForTheLog()
    {
        QuestingBotBase questing = new(Quest(
            new ProfileStep(StepKind.Objective, QuestId: 9001, Position: Field,
                Objective: ObjectiveKind.Unknown, Radius: 40f)));

        FakeBotState state = new() { Position = new Vector3(1f, 1f, 1f) };
        state.QuestLog.Add(9001);

        Assert.Equal(RunStatus.Running, questing.Build().Tick(state));
        Assert.Contains(Field, state.MoveRequests);

        // Once there, it stays put and lets the quest log decide when the step is done.
        state.Position = Field;
        state.MoveRequests.Clear();

        Assert.Equal(RunStatus.Running, questing.Build().Tick(state));
        Assert.Empty(state.MoveRequests);
    }

    [Fact]
    public void FallsBackToGrindingWhenTheProfileIsFinished()
    {
        // Running out of quests is what success looks like for a levelling profile, and a
        // character standing still is worse than one levelling slowly.
        bool grinded = false;

        QuestingBotBase questing = new(
            Quest(new ProfileStep(StepKind.RunTo, Position: Giver)),
            fallback: new Do<IBotState>(_ =>
            {
                grinded = true;
                return RunStatus.Running;
            }));

        FakeBotState state = new() { Position = Giver };

        Assert.Equal(RunStatus.Running, questing.Build().Tick(state));
        Assert.True(grinded);
    }

    [Fact]
    public void StandsStillWhenThereIsNothingLeftAndNoFallback()
    {
        QuestingBotBase questing = new(Quest(new ProfileStep(StepKind.RunTo, Position: Giver)));

        FakeBotState state = new() { Position = Giver };

        Assert.Equal(RunStatus.Running, questing.Build().Tick(state));
        Assert.Contains("StopMoving", state.Actions);
    }

    [Fact]
    public void RunsACustomBehaviourItHas()
    {
        bool ran = false;

        QuestingBotBase questing = new(
            Quest(new ProfileStep(StepKind.CustomBehavior, BehaviorName: "WaitTimer")),
            behaviors: new Dictionary<string, Node<IBotState>>(StringComparer.OrdinalIgnoreCase)
            {
                ["WaitTimer"] = new Do<IBotState>(_ =>
                {
                    ran = true;
                    return RunStatus.Running;
                }),
            });

        Assert.Equal(RunStatus.Running, questing.Build().Tick(new FakeBotState()));
        Assert.True(ran);
    }

    [Fact]
    public void SkipsACustomBehaviourItDoesNotHaveRatherThanStopping()
    {
        // An imported profile naming behaviours that only the other bot had should still run
        // the rest of its steps.
        QuestingBotBase questing = new(Quest(
            new ProfileStep(StepKind.CustomBehavior, BehaviorName: "SomethingElsesCode")));

        Assert.Equal(RunStatus.Failure, questing.Build().Tick(new FakeBotState()));
    }

    [Fact]
    public void GivesUpOnAStepMovementCannotReach()
    {
        QuestingBotBase questing = new(Quest(
            new ProfileStep(StepKind.PickUp, QuestId: 9001, Entry: 8100, Position: Giver)));

        FakeBotState state = new() { Position = Field, MovementFailed = true };

        Assert.Equal(RunStatus.Failure, questing.Build().Tick(state));
    }

    [Fact]
    public void SaysSoWhenAGiverIsNeitherInSightNorPlaced()
    {
        QuestingBotBase questing = new(Quest(
            new ProfileStep(StepKind.PickUp, QuestId: 9001, Entry: 8100)));

        Assert.Equal(RunStatus.Failure, questing.Build().Tick(new FakeBotState()));
    }
}
