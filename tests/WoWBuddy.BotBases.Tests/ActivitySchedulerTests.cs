using WoWBuddy.BotBases.Activities;
using WoWBuddy.Profiles;
using Xunit;

namespace WoWBuddy.BotBases.Tests;

/// <summary>A character's own facts, set by hand.</summary>
internal sealed class FakeConditionContext : IProfileConditionContext
{
    public int Level { get; set; } = 20;

    public long Money { get; set; }

    public int BagsFullPercent { get; set; }

    public HashSet<uint> Completed { get; } = [];

    public HashSet<uint> InLog { get; } = [];

    public Dictionary<uint, int> Items { get; } = [];

    public bool IsQuestCompleted(uint questId) => Completed.Contains(questId);

    public bool IsQuestInLog(uint questId) => InLog.Contains(questId);

    public bool IsQuestReadyToTurnIn(uint questId) => false;

    public int ItemCount(uint itemId) => Items.TryGetValue(itemId, out int count) ? count : 0;
}

public sealed class ActivitySchedulerTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    private static ProfileCondition Condition(string text)
    {
        Assert.True(ProfileConditionParser.TryParse(text, out ProfileCondition condition, out string error), error);
        return condition;
    }

    private static ActivityPlan Plan(TimeSpan? dwell = null, params Activity[] activities) => new()
    {
        Name = "test",
        Activities = activities,
        MinimumDwell = dwell ?? TimeSpan.FromMinutes(2),
    };

    [Fact]
    public void TheFirstEligibleActivityStarts()
    {
        ActivityPlan plan = Plan(null,
            new Activity("Sell", Conditions: [Condition("BagsFullPercent >= 90")]),
            new Activity("Grind"));

        ActivityScheduler scheduler = new(plan);
        FakeConditionContext context = new();

        Activity? current = scheduler.Update(Start, context);

        Assert.Equal("Grind", current!.Name);
        Assert.Equal(ActivityChange.Started, scheduler.LastChange);
    }

    [Fact]
    public void AListIsAPriorityListRatherThanARota()
    {
        // "Go and sell when the bags are full" is then just an activity with a condition on it,
        // sitting above the others.
        ActivityPlan plan = Plan(null,
            new Activity("Sell", Conditions: [Condition("BagsFullPercent >= 90")]),
            new Activity("Grind"));

        ActivityScheduler scheduler = new(plan);
        FakeConditionContext context = new();

        scheduler.Update(Start, context);

        context.BagsFullPercent = 95;

        // Not immediately: the dwell has to pass first.
        Assert.Equal("Grind", scheduler.Update(Start + TimeSpan.FromSeconds(30), context)!.Name);
        Assert.Equal("Sell", scheduler.Update(Start + TimeSpan.FromMinutes(3), context)!.Name);
        Assert.Equal(ActivityChange.Preempted, scheduler.LastChange);
    }

    [Fact]
    public void AConditionThatStopsHoldingEndsTheActivityAtOnce()
    {
        // A dwell exists to stop dithering between things worth doing, not to keep the
        // character mining with full bags.
        ActivityPlan plan = Plan(TimeSpan.FromMinutes(10),
            new Activity("Gather", Conditions: [Condition("BagsFullPercent < 90")]),
            new Activity("Grind"));

        ActivityScheduler scheduler = new(plan);
        FakeConditionContext context = new();

        Assert.Equal("Gather", scheduler.Update(Start, context)!.Name);

        context.BagsFullPercent = 95;

        Assert.Equal("Grind", scheduler.Update(Start + TimeSpan.FromSeconds(1), context)!.Name);
        Assert.Equal(ActivityChange.NoLongerEligible, scheduler.LastChange);
    }

    [Fact]
    public void AConditionOnItsThresholdDoesNotMakeTheCharacterStandStillSwapping()
    {
        // The single most important behaviour here. Without the dwell, an activity whose
        // condition sits on a boundary is swapped in and out several times a second.
        ActivityPlan plan = Plan(TimeSpan.FromMinutes(2),
            new Activity("Sell", Conditions: [Condition("BagsFullPercent >= 80")]),
            new Activity("Grind"));

        ActivityScheduler scheduler = new(plan);
        FakeConditionContext context = new() { BagsFullPercent = 79 };

        scheduler.Update(Start, context);

        // The bags hover on the boundary for a minute.
        for (int second = 1; second <= 60; second++)
        {
            context.BagsFullPercent = second % 2 == 0 ? 80 : 79;
            scheduler.Update(Start + TimeSpan.FromSeconds(second), context);
        }

        Assert.Equal(1, scheduler.Changes);
        Assert.Equal("Grind", scheduler.Current!.Name);
    }

    [Fact]
    public void AnActivityWithATimeLimitHandsOverWhenItExpires()
    {
        ActivityPlan plan = Plan(TimeSpan.FromSeconds(30),
            new Activity("Quest", TimeSpan.FromHours(1)),
            new Activity("Gather", TimeSpan.FromMinutes(30)));

        ActivityScheduler scheduler = new(plan);
        FakeConditionContext context = new();

        Assert.Equal("Quest", scheduler.Update(Start, context)!.Name);
        Assert.Equal("Quest", scheduler.Update(Start + TimeSpan.FromMinutes(59), context)!.Name);

        Assert.Equal("Gather", scheduler.Update(Start + TimeSpan.FromMinutes(61), context)!.Name);
        Assert.Equal(ActivityChange.Expired, scheduler.LastChange);
    }

    [Fact]
    public void AnHourOfThisThenAnHourOfThatActuallyReachesTheSecondHour()
    {
        // Going back to the top of the list on expiry would re-select the most preferred
        // activity forever, and the second hour would never happen.
        ActivityPlan plan = Plan(TimeSpan.FromSeconds(30),
            new Activity("Quest", TimeSpan.FromHours(1)),
            new Activity("Gather", TimeSpan.FromHours(1)));

        ActivityScheduler scheduler = new(plan);
        FakeConditionContext context = new();

        scheduler.Update(Start, context);

        DateTimeOffset now = Start + TimeSpan.FromMinutes(61);
        Assert.Equal("Gather", scheduler.Update(now, context)!.Name);

        now += TimeSpan.FromMinutes(61);
        Assert.Equal("Quest", scheduler.Update(now, context)!.Name);
    }

    [Fact]
    public void NothingEligibleIsReportedRatherThanGuessedAround()
    {
        ActivityPlan plan = Plan(null,
            new Activity("Sell", Conditions: [Condition("BagsFullPercent >= 90")]));

        ActivityScheduler scheduler = new(plan);

        Assert.Null(scheduler.Update(Start, new FakeConditionContext()));
        Assert.Equal(ActivityChange.Stalled, scheduler.LastChange);
        Assert.Contains("Nothing in the plan", scheduler.Describe(Start), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyPlanSaysSo()
    {
        ActivityScheduler scheduler = new(new ActivityPlan { Name = "empty" });

        Assert.Null(scheduler.Update(Start, new FakeConditionContext()));
        Assert.Contains("no activities", scheduler.Describe(Start), StringComparison.Ordinal);
    }

    [Fact]
    public void AStalledPlanRecoversWhenSomethingBecomesEligible()
    {
        ActivityPlan plan = Plan(null,
            new Activity("Sell", Conditions: [Condition("BagsFullPercent >= 90")]));

        ActivityScheduler scheduler = new(plan);
        FakeConditionContext context = new();

        Assert.Null(scheduler.Update(Start, context));

        context.BagsFullPercent = 95;

        Assert.Equal("Sell", scheduler.Update(Start + TimeSpan.FromSeconds(1), context)!.Name);
    }

    [Fact]
    public void ALessPreferredActivityDoesNotPreemptAMorePreferredOne()
    {
        ActivityPlan plan = Plan(TimeSpan.FromSeconds(1),
            new Activity("Quest"),
            new Activity("Grind"));

        ActivityScheduler scheduler = new(plan);
        FakeConditionContext context = new();

        scheduler.Update(Start, context);

        for (int minute = 1; minute <= 30; minute++)
        {
            scheduler.Update(Start + TimeSpan.FromMinutes(minute), context);
        }

        Assert.Equal("Quest", scheduler.Current!.Name);
        Assert.Equal(1, scheduler.Changes);
    }

    [Fact]
    public void LevelConditionsWorkTheWayAProfileWouldExpect()
    {
        ActivityPlan plan = Plan(TimeSpan.FromSeconds(1),
            new Activity("Dungeon", Conditions: [Condition("Level >= 60")]),
            new Activity("Quest"));

        ActivityScheduler scheduler = new(plan);
        FakeConditionContext context = new() { Level = 58 };

        Assert.Equal("Quest", scheduler.Update(Start, context)!.Name);

        context.Level = 60;

        Assert.Equal("Dungeon", scheduler.Update(Start + TimeSpan.FromMinutes(5), context)!.Name);
    }

    [Fact]
    public void ResettingForgetsEverything()
    {
        ActivityScheduler scheduler = new(Plan(null, new Activity("Grind")));

        scheduler.Update(Start, new FakeConditionContext());
        Assert.NotNull(scheduler.Current);

        scheduler.Reset();

        Assert.Null(scheduler.Current);
        Assert.Equal(0, scheduler.Changes);
    }

    [Fact]
    public void TheDescriptionSaysHowFarThroughATimedActivityItIs()
    {
        ActivityScheduler scheduler = new(Plan(null, new Activity("Quest", TimeSpan.FromMinutes(60))));

        scheduler.Update(Start, new FakeConditionContext());

        Assert.Equal("Quest, 20 of 60 minutes", scheduler.Describe(Start + TimeSpan.FromMinutes(20)));
    }
}
