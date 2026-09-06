using WoWBuddy.Behavior;
using WoWBuddy.BotBases.Battlegrounds;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Objects;
using WoWBuddy.Profiles;
using Xunit;

namespace WoWBuddy.BotBases.Tests;

public sealed class BattlegroundBotBaseTests
{
    private static readonly Vector3 First = new(900f, 1400f, 340f);
    private static readonly Vector3 Second = new(1000f, 1400f, 340f);

    private static BattlegroundSettings Settings(params BattlegroundPost[] posts) => new()
    {
        Queue = "Warsong Gulch",
        Plan = new BattlegroundPlan { Posts = posts, ChaseRange = 30f },
    };

    [Fact]
    public void QueuesWhenItIsNotQueuedForAnything()
    {
        FakeBotState state = new();

        Assert.Equal(RunStatus.Success, new BattlegroundBotBase(Settings()).Build().Tick(state));
        Assert.Contains("Queue(Warsong Gulch)", state.BattlegroundState.Actions);
    }

    [Fact]
    public void AcceptsTheInvitationWhenOneIsWaiting()
    {
        FakeBotState state = new();
        state.BattlegroundState.Status = BattlegroundStatus.Confirmed;

        Assert.Equal(RunStatus.Success, new BattlegroundBotBase(Settings()).Build().Tick(state));
        Assert.Contains("AcceptInvitation", state.BattlegroundState.Actions);
    }

    [Fact]
    public void WaitingInTheQueueLetsTheRestOfTheTreeGetOnWithThings()
    {
        // Failing rather than running: eating, repairing and mailing should all still happen
        // while the character sits in a queue.
        FakeBotState state = new();
        state.BattlegroundState.Status = BattlegroundStatus.Queued;

        Assert.Equal(RunStatus.Failure, new BattlegroundBotBase(Settings()).Build().Tick(state));
    }

    [Fact]
    public void SaysSoWhenNoBattlegroundWasNamed()
    {
        FakeBotState state = new();

        Assert.Equal(RunStatus.Failure, new BattlegroundBotBase().Build().Tick(state));
    }

    [Fact]
    public void MovesBehindTheGatesRatherThanStandingPerfectlyStill()
    {
        // Standing still is one of the few things a battleground actually watches for.
        FakeBotState state = new() { Position = new Vector3(0f, 0f, 340f) };
        state.BattlegroundState.Playing();
        state.BattlegroundState.HasStarted = false;

        BattlegroundBotBase bot = new(Settings(new BattlegroundPost(First)));

        Assert.Equal(RunStatus.Running, bot.Build().Tick(state));
        Assert.Contains(First, state.MoveRequests);
    }

    [Fact]
    public void GoesToTheFirstPostOnceTheGatesOpen()
    {
        FakeBotState state = new() { Position = new Vector3(0f, 0f, 340f) };
        state.BattlegroundState.Playing();

        BattlegroundBotBase bot = new(Settings(new BattlegroundPost(First), new BattlegroundPost(Second)));

        Assert.Equal(RunStatus.Running, bot.Build().Tick(state));
        Assert.Contains(First, state.MoveRequests);
    }

    [Fact]
    public void ClicksTheFlagOnAPostThatHasOne()
    {
        // The one piece of battleground behaviour that generalises: capturing a base, taking a
        // flag and returning one are all "walk to a thing and click it".
        FakeBotState state = new() { Position = First };
        state.BattlegroundState.Playing();
        VisibleObject flag = state.AddVisible(1, entry: 179830, distance: 2f);

        BattlegroundBotBase bot = new(Settings(new BattlegroundPost(First, Entry: 179830, Name: "Flag")));

        Assert.Equal(RunStatus.Running, bot.Build().Tick(state));
        Assert.Contains($"Interact({flag.Guid})", state.Actions);
    }

    [Fact]
    public void WalksTheLastFewYardsToAFlagItCanSee()
    {
        FakeBotState state = new() { Position = First };
        state.BattlegroundState.Playing();
        VisibleObject flag = state.AddVisible(1, entry: 179830, distance: 15f);

        BattlegroundBotBase bot = new(Settings(new BattlegroundPost(First, Entry: 179830)));

        bot.Build().Tick(state);

        Assert.Contains(flag.Position, state.MoveRequests);
    }

    [Fact]
    public void MovesOnFromAPostWithNothingToDoOnIt()
    {
        FakeBotState state = new() { Position = First };
        state.BattlegroundState.Playing();

        BattlegroundBotBase bot = new(Settings(new BattlegroundPost(First), new BattlegroundPost(Second)));

        Assert.Equal(RunStatus.Running, bot.Build().Tick(state));
        Assert.Contains(Second, state.MoveRequests);
    }

    [Fact]
    public void FightsWhoeverIsAlreadyHittingIt()
    {
        FakeBotState state = new() { Position = First };
        state.BattlegroundState.Playing();

        state.AddEnemy(1, distance: 10f, healthPercent: 20d);
        CandidateTarget attacker = state.AddEnemy(2, distance: 20f, targetingMe: true);

        BattlegroundBotBase bot = new(Settings(new BattlegroundPost(First)));
        bot.Build().Tick(state);

        Assert.Equal(attacker.Guid, state.Target!.Value.Guid);
    }

    [Fact]
    public void OtherwisePrefersTheWeakest()
    {
        // Kills are shared in a battleground, so finishing someone else's work is worth more
        // than starting fresh.
        FakeBotState state = new() { Position = First };
        state.BattlegroundState.Playing();

        state.AddEnemy(1, distance: 5f, healthPercent: 100d);
        CandidateTarget weakest = state.AddEnemy(2, distance: 20f, healthPercent: 15d);

        BattlegroundBotBase bot = new(Settings(new BattlegroundPost(First)));
        bot.Build().Tick(state);

        Assert.Equal(weakest.Guid, state.Target!.Value.Guid);
    }

    [Fact]
    public void WillNotChaseSomeoneAcrossTheMap()
    {
        // The classic battleground bot failure: chasing one runner to the other end of the map
        // while the objective it was standing on changes hands behind it.
        FakeBotState state = new() { Position = First };
        state.BattlegroundState.Playing();
        state.AddEnemy(1, distance: 35f, healthPercent: 5d);

        BattlegroundBotBase bot = new(Settings(new BattlegroundPost(First)));
        bot.Build().Tick(state);

        Assert.Null(state.Target);
    }

    [Fact]
    public void LeavesWhenTheMatchIsOverAndStartsAgainFromTheTop()
    {
        FakeBotState state = new() { Position = Second };
        state.BattlegroundState.Playing();
        state.BattlegroundState.IsFinished = true;

        BattlegroundBotBase bot = new(Settings(new BattlegroundPost(First), new BattlegroundPost(Second)));
        bot.AdvancePost();

        Assert.Equal(RunStatus.Success, bot.Build().Tick(state));
        Assert.Contains("Leave", state.BattlegroundState.Actions);
        Assert.Equal(First, bot.CurrentPost!.Value.Position);
    }

    [Fact]
    public void StaysPutWhenItIsToldNotToLeave()
    {
        FakeBotState state = new();
        state.BattlegroundState.Playing();
        state.BattlegroundState.IsFinished = true;

        BattlegroundBotBase bot = new(new BattlegroundSettings
        {
            Plan = new BattlegroundPlan { Posts = [new BattlegroundPost(First)] },
            LeaveWhenFinished = false,
        });

        Assert.Equal(RunStatus.Running, bot.Build().Tick(state));
        Assert.DoesNotContain("Leave", state.BattlegroundState.Actions);
    }

    [Fact]
    public void SaysSoWhenThePlanNamesNowhereToGo()
    {
        // Battleground layouts are game data this project does not ship.
        FakeBotState state = new();
        state.BattlegroundState.Playing();

        Assert.Equal(RunStatus.Failure, new BattlegroundBotBase(Settings()).Build().Tick(state));
    }

    [Fact]
    public void TriesTheNextPostWhenOneCannotBeReached()
    {
        FakeBotState state = new() { Position = new Vector3(0f, 0f, 340f), MovementFailed = true };
        state.BattlegroundState.Playing();

        BattlegroundBotBase bot = new(Settings(new BattlegroundPost(First), new BattlegroundPost(Second)));

        Assert.Equal(RunStatus.Failure, bot.Build().Tick(state));
        Assert.Equal(Second, bot.CurrentPost!.Value.Position);
    }

    [Fact]
    public void APlanCanBeReadOutOfAProfile()
    {
        // One file format for users rather than a second one invented for battlegrounds.
        Profile profile = new()
        {
            Name = "Warsong Gulch",
            Steps =
            [
                new ProfileStep(StepKind.RunTo, Position: First, Radius: 25f),
                new ProfileStep(StepKind.Objective, Entry: 179830, Position: Second, QuestName: "Flag"),
                new ProfileStep(StepKind.Grind, Hotspots: [First, Second]),
                new ProfileStep(StepKind.TurnIn, QuestId: 1, Entry: 8100),
            ],
            AvoidMobs = new HashSet<uint> { 42 },
            Blackspots = [new ProfileBlackspot(Second, 10f, 489)],
        };

        BattlegroundPlan plan = BattlegroundPlan.FromProfile(profile);

        Assert.Equal("Warsong Gulch", plan.Name);

        // Four posts: two positions and two hotspots. The turn-in names no position, and a
        // battleground plan is only ever about where to stand.
        Assert.Equal(4, plan.Posts.Count);
        Assert.Equal(25f, plan.Posts[0].Radius);
        Assert.Equal(179830u, plan.Posts[1].Entry);
        Assert.True(plan.Posts[1].HasObjective);
        Assert.False(plan.Posts[0].HasObjective);

        Assert.Contains(42u, plan.AvoidEntries);
        Assert.True(plan.IsBlacklisted(489, Second));
        Assert.False(plan.IsBlacklisted(0, Second));
    }
}
