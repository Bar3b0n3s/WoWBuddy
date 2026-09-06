using WoWBuddy.Behavior;
using WoWBuddy.BotBases;
using WoWBuddy.BotBases.Support;
using Xunit;

namespace WoWBuddy.BotBases.Tests;

public sealed class TalentTests
{
    private static TalentBuild Build(string text)
    {
        Assert.True(TalentBuild.TryParse(text, out TalentBuild build, out string error), error);
        return build;
    }

    [Fact]
    public void ABuildIsReadFromTheShortestThingThatCouldWork()
    {
        TalentBuild build = Build("1:3, 1:3, 2:5");

        Assert.Equal(3, build.Points);
        Assert.Equal(new TalentPick(1, 3), build.Picks[0]);
        Assert.Equal(new TalentPick(2, 5), build.Picks[2]);
        Assert.Equal("1:3, 1:3, 2:5", build.ToString());
    }

    [Theory]
    [InlineData("1:3 1:3 2:5")]
    [InlineData("1:3,1:3,2:5")]
    [InlineData("  1:3 ,  1:3\n2:5  ")]
    public void CommasAndWhitespaceBothSeparate(string text)
    {
        Assert.Equal(3, Build(text).Points);
    }

    [Fact]
    public void NothingConfiguredIsNotAMistake()
    {
        // Most characters are levelled by hand.
        Assert.True(TalentBuild.TryParse(null, out TalentBuild build, out _));
        Assert.False(build.IsUsable);

        Assert.True(TalentBuild.TryParse("   ", out _, out _));
    }

    [Theory]
    [InlineData("1-3", "not a talent")]
    [InlineData("one:three", "not a talent")]
    [InlineData("1:3, oops", "not a talent")]
    [InlineData("4:1", "three trees")]
    [InlineData("0:1", "three trees")]
    public void AnUnreadableBuildSaysWhichPartBrokeIt(string text, string expected)
    {
        Assert.False(TalentBuild.TryParse(text, out _, out string error));
        Assert.Contains(expected, error, StringComparison.Ordinal);
    }

    [Fact]
    public void TheUpperBoundOnATalentIndexIsLeftToTheClient()
    {
        // The number of talents in a tree differs by class and this project has no talent data,
        // so the client is the honest place for that check.
        Assert.True(TalentBuild.TryParse("3:99", out TalentBuild build, out _));
        Assert.Single(build.Picks);
    }

    [Fact]
    public void NothingIsSpentWithoutABuild()
    {
        // Points sitting unspent cost nothing; points spent in the wrong order cost gold.
        FakeBotState state = new();
        state.TalentState.UnspentPoints = 5;

        Assert.Equal(RunStatus.Failure, RootTree.HandleTalents(null).Tick(state));
        Assert.Empty(state.TalentState.Learned);
    }

    [Fact]
    public void APointIsSpentWhenOneIsGoingSpare()
    {
        FakeBotState state = new();
        state.TalentState.UnspentPoints = 3;

        Assert.Equal(RunStatus.Success, RootTree.HandleTalents(Build("1:3, 1:3, 2:5")).Tick(state));
        Assert.Equal(new TalentPick(1, 3), Assert.Single(state.TalentState.Learned));
    }

    [Fact]
    public void PointsAreTakenInTheOrderTheBuildLists()
    {
        FakeBotState state = new();
        state.TalentState.UnspentPoints = 3;

        Node<IBotState> tree = RootTree.HandleTalents(Build("1:3, 1:3, 2:5"));

        tree.Tick(state);
        tree.Tick(state);
        tree.Tick(state);

        Assert.Equal(
            [new TalentPick(1, 3), new TalentPick(1, 3), new TalentPick(2, 5)],
            state.TalentState.Learned);
    }

    [Fact]
    public void OnePointPerTickSoTheNextOneSeesTheTreeAsItIsNow()
    {
        FakeBotState state = new();
        state.TalentState.UnspentPoints = 3;

        RootTree.HandleTalents(Build("1:3, 1:3, 2:5")).Tick(state);

        Assert.Single(state.TalentState.Learned);
        Assert.Equal(2, state.TalentState.UnspentPoints);
    }

    [Fact]
    public void NothingIsSpentInCombat()
    {
        FakeBotState state = new() { IsInCombat = true };
        state.TalentState.UnspentPoints = 3;

        Assert.Equal(RunStatus.Failure, RootTree.HandleTalents(Build("1:3")).Tick(state));
        Assert.Empty(state.TalentState.Learned);
    }

    [Fact]
    public void ARefusedTalentStopsRatherThanRetryingEveryTick()
    {
        // Almost always a prerequisite further up the tree, which means the build is wrong and
        // repeating it would fill the log without ever succeeding.
        FakeBotState state = new();
        state.TalentState.UnspentPoints = 2;
        state.TalentState.Refuse.Add(new TalentPick(2, 5));

        Node<IBotState> tree = RootTree.HandleTalents(Build("2:5, 1:3"));

        Assert.Equal(RunStatus.Failure, tree.Tick(state));
        Assert.Empty(state.TalentState.Learned);
        Assert.Equal(2, state.TalentState.UnspentPoints);
    }

    [Fact]
    public void MorePointsThanTheBuildAccountsForAreLeftAlone()
    {
        // The build has run out and nothing here knows what should come next.
        FakeBotState state = new();
        state.TalentState.UnspentPoints = 10;

        Assert.Equal(RunStatus.Failure, RootTree.HandleTalents(Build("1:3")).Tick(state));
        Assert.Empty(state.TalentState.Learned);
    }

    [Fact]
    public void TalentsAreSpentBelowCombatAndLootingInTheRootTree()
    {
        // A point going spare is never a reason to stop fighting or leave a corpse.
        FakeBotState state = new() { IsInCombat = true };
        state.TalentState.UnspentPoints = 1;
        state.Target = state.AddEnemy(1, targetingMe: true);

        RootTree.Build(
            new Do<IBotState>(_ => RunStatus.Success),
            talents: Build("1:3"))
            .Tick(state);

        Assert.Empty(state.TalentState.Learned);
        Assert.Contains("Combat", state.RecordingRoutine.Calls);
    }
}
