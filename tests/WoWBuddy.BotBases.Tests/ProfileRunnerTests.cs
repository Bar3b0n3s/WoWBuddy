using WoWBuddy.BotBases.Questing;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Profiles;
using Xunit;

namespace WoWBuddy.BotBases.Tests;

public sealed class ProfileRunnerTests
{
    private static readonly Vector3 Giver = new(1500f, -2500f, 60f);
    private static readonly Vector3 Field = new(1560f, -2460f, 60f);

    private static Profile ThreeStepQuest() => new()
    {
        Name = "test",
        Steps =
        [
            new ProfileStep(StepKind.PickUp, QuestId: 9001, Entry: 8100, Position: Giver, LineNumber: 1),
            new ProfileStep(StepKind.Objective, QuestId: 9001, Entry: 8200, Position: Field,
                Objective: ObjectiveKind.Kill, Count: 8, LineNumber: 2),
            new ProfileStep(StepKind.TurnIn, QuestId: 9001, Entry: 8100, Position: Giver, LineNumber: 3),
        ],
    };

    [Fact]
    public void WalksThePickUpWorkTurnInCycle()
    {
        ProfileRunner runner = new(ThreeStepQuest());
        FakeBotState state = new();

        Assert.Equal(StepKind.PickUp, runner.Choose(state).Step!.Kind);

        state.QuestLog.Add(9001);
        Assert.Equal(StepKind.Objective, runner.Choose(state).Step!.Kind);

        state.QuestLog.Add(9001, complete: true);
        Assert.Equal(StepKind.TurnIn, runner.Choose(state).Step!.Kind);

        state.QuestLog.MarkCompleted(9001);
        Assert.False(runner.Choose(state).HasStep);
        Assert.True(runner.IsFinished(state));
    }

    [Fact]
    public void AQuestAlreadyDoneIsSkippedEntirely()
    {
        // The case that matters for picking a profile up on a character with history: none of
        // the three steps should be chosen.
        ProfileRunner runner = new(ThreeStepQuest());
        FakeBotState state = new();
        state.QuestLog.Completed(9001);

        Assert.False(runner.Choose(state).HasStep);
    }

    [Fact]
    public void AnObjectiveIsSkippedWhenItsQuestIsNotInTheLog()
    {
        // Not in the log and not handed in: the pick-up above is what needs to run, and this
        // is what lets the runner reach it.
        Profile profile = new()
        {
            Steps =
            [
                new ProfileStep(StepKind.Objective, QuestId: 9001, Position: Field, LineNumber: 1),
                new ProfileStep(StepKind.RunTo, Position: Giver, LineNumber: 2),
            ],
        };

        FakeBotState state = new() { Position = Field };

        Assert.Equal(StepKind.RunTo, new ProfileRunner(profile).Choose(state).Step!.Kind);
    }

    [Fact]
    public void AnIndexedObjectiveWatchesThatObjectiveOnly()
    {
        Profile profile = new()
        {
            Steps =
            [
                new ProfileStep(StepKind.Objective, QuestId: 9001, Position: Field,
                    Objective: ObjectiveKind.Kill, ObjectiveIndex: 2, LineNumber: 1),
            ],
        };

        ProfileRunner runner = new(profile);
        FakeBotState state = new();

        state.QuestLog.Add(
            9001,
            complete: false,
            new QuestObjective("wolves", 8, 8, IsDone: true),
            new QuestObjective("bears", 1, 8, IsDone: false));

        Assert.True(runner.Choose(state).HasStep);

        state.QuestLog.Add(
            9001,
            complete: false,
            new QuestObjective("wolves", 8, 8, IsDone: true),
            new QuestObjective("bears", 8, 8, IsDone: true));

        Assert.False(runner.Choose(state).HasStep);
    }

    [Fact]
    public void ACollectObjectiveIsDoneOnceTheItemsAreCarried()
    {
        Profile profile = new()
        {
            Steps =
            [
                new ProfileStep(StepKind.Objective, QuestId: 9001, Position: Field,
                    Objective: ObjectiveKind.Collect, ItemId: 8600, Count: 6, LineNumber: 1),
            ],
        };

        ProfileRunner runner = new(profile);
        FakeBotState state = new();
        state.QuestLog.Add(9001);
        state.Items[8600] = 5;

        Assert.True(runner.Choose(state).HasStep);

        state.Items[8600] = 6;
        Assert.False(runner.Choose(state).HasStep);
    }

    [Fact]
    public void ConditionsDecideWhetherAStepApplies()
    {
        ProfileCondition atLeastFive = Parse("Level >= 5");

        Profile profile = new()
        {
            Steps =
            [
                new ProfileStep(StepKind.RunTo, Position: Giver, Conditions: [atLeastFive], LineNumber: 1),
                new ProfileStep(StepKind.RunTo, Position: Field, LineNumber: 2),
            ],
        };

        ProfileRunner runner = new(profile);
        FakeBotState state = new() { Level = 4, Position = new Vector3(1f, 1f, 1f) };

        Assert.Equal(Field, runner.Choose(state).Step!.Position);

        state.Level = 5;
        Assert.Equal(Giver, runner.Choose(state).Step!.Position);
    }

    [Fact]
    public void EveryConditionTermReachesTheGameState()
    {
        // A term that parsed but read nothing would let a profile pass validation and then
        // quietly do the wrong thing, so each one is checked against a state set to match.
        FakeBotState state = new()
        {
            Level = 20,
            Inventory = new Support.InventoryState(5, 20, 100d, 5000),
        };

        state.QuestLog.Completed(1);
        state.QuestLog.Add(2);
        state.QuestLog.Add(3, complete: true);
        state.Items[4] = 7;

        ProfileConditionContext context = new(state);

        Assert.Equal(20, context.Level);
        Assert.Equal(5000, context.Money);
        Assert.Equal(75, context.BagsFullPercent);
        Assert.True(context.IsQuestCompleted(1));
        Assert.True(context.IsQuestInLog(2));
        Assert.True(context.IsQuestReadyToTurnIn(3));
        Assert.False(context.IsQuestReadyToTurnIn(2));
        Assert.Equal(7, context.ItemCount(4));
    }

    [Fact]
    public void ARepeatRunsItsChildrenWhileItsConditionHolds()
    {
        Profile profile = new()
        {
            Steps =
            [
                new ProfileStep(
                    StepKind.Repeat,
                    Conditions: [Parse("Level < 10")],
                    Children:
                    [
                        new ProfileStep(StepKind.Grind, Position: Field, Radius: 80f, LineNumber: 2),
                    ],
                    LineNumber: 1),
                new ProfileStep(StepKind.RunTo, Position: Giver, LineNumber: 3),
            ],
        };

        ProfileRunner runner = new(profile);
        FakeBotState state = new() { Level = 5, Position = new Vector3(1f, 1f, 1f) };

        Assert.Equal(StepKind.Grind, runner.Choose(state).Step!.Kind);

        state.Level = 10;
        Assert.Equal(StepKind.RunTo, runner.Choose(state).Step!.Kind);
    }

    [Fact]
    public void ARepeatWhoseChildrenAreAllDoneIsMovedPastRatherThanSpun()
    {
        Profile profile = new()
        {
            Steps =
            [
                new ProfileStep(
                    StepKind.Repeat,
                    Conditions: [Parse("Level < 10")],
                    Children: [new ProfileStep(StepKind.RunTo, Position: Field, LineNumber: 2)],
                    LineNumber: 1),
                new ProfileStep(StepKind.RunTo, Position: Giver, LineNumber: 3),
            ],
        };

        ProfileRunner runner = new(profile);

        // Standing on the repeat's only step means it is already done, while the condition
        // still holds. Going round again would do nothing, so the runner moves past it.
        FakeBotState state = new() { Level = 5, Position = Field };

        Assert.Equal(Giver, runner.Choose(state).Step!.Position);
    }

    [Fact]
    public void AStepBecomingUndoneIsPickedUpAgain()
    {
        // A cursor would have moved past this. Re-deciding from the top every tick is what
        // makes a quest abandoned by hand, or a session resumed a day later, recover.
        ProfileRunner runner = new(ThreeStepQuest());
        FakeBotState state = new();

        state.QuestLog.Add(9001);
        Assert.Equal(StepKind.Objective, runner.Choose(state).Step!.Kind);

        state.QuestLog.Abandon(9001);
        Assert.Equal(StepKind.PickUp, runner.Choose(state).Step!.Kind);
    }

    [Fact]
    public void ARunToIsDoneOnceTheCharacterIsThere()
    {
        Profile profile = new()
        {
            Steps = [new ProfileStep(StepKind.RunTo, Position: Giver, LineNumber: 1)],
        };

        ProfileRunner runner = new(profile);

        Assert.True(runner.Choose(new FakeBotState { Position = Field }).HasStep);
        Assert.False(runner.Choose(new FakeBotState { Position = Giver }).HasStep);
    }

    [Fact]
    public void AnEmptyProfileHasNothingToDoAndSaysWhy()
    {
        StepChoice choice = new ProfileRunner(new Profile()).Choose(new FakeBotState());

        Assert.False(choice.HasStep);
        Assert.Contains("done or waiting", choice.Reason, StringComparison.Ordinal);
    }

    private static ProfileCondition Parse(string text)
    {
        Assert.True(ProfileConditionParser.TryParse(text, out ProfileCondition condition, out string error), error);
        return condition;
    }
}
