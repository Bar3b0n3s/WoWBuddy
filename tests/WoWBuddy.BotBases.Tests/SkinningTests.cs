using WoWBuddy.Behavior;
using WoWBuddy.BotBases;
using WoWBuddy.Common.Geometry;
using Xunit;

namespace WoWBuddy.BotBases.Tests;

public sealed class SkinningTests
{
    private static FakeBotState WithSkinnable(float distance)
    {
        FakeBotState state = new();
        CandidateTarget corpse = state.AddEnemy(1, distance: distance, alive: false);

        state.SkinnableCorpses = [corpse];
        state.NearbyEnemies = [];

        return state;
    }

    [Fact]
    public void WalksToACorpseWorthSkinning()
    {
        FakeBotState state = WithSkinnable(distance: 20f);

        Assert.Equal(RunStatus.Running, RootTree.HandleSkinning().Tick(state));
        Assert.NotEmpty(state.MoveRequests);
    }

    [Fact]
    public void SkinsItOnceItIsStandingThere()
    {
        FakeBotState state = WithSkinnable(distance: 2f);

        Assert.Equal(RunStatus.Running, RootTree.HandleSkinning().Tick(state));
        Assert.Contains(state.Actions, a => a.StartsWith("Interact", StringComparison.Ordinal));
        Assert.Contains("StopMoving", state.Actions);
    }

    [Fact]
    public void ACharacterThatCannotSkinDoesNothing()
    {
        // The live view reports an empty list when skinning is off, so the branch never fires.
        FakeBotState state = new();
        state.AddEnemy(1, distance: 2f, alive: false);

        Assert.Equal(RunStatus.Failure, RootTree.HandleSkinning().Tick(state));
    }

    [Fact]
    public void NothingIsSkinnedMidFight()
    {
        FakeBotState state = WithSkinnable(distance: 2f);
        state.IsInCombat = true;

        Assert.Equal(RunStatus.Failure, RootTree.HandleSkinning().Tick(state));
        Assert.DoesNotContain(state.Actions, a => a.StartsWith("Interact", StringComparison.Ordinal));
    }

    [Fact]
    public void ACorpseItCannotReachIsLeftRatherThanWalkedIntoUntilItDecays()
    {
        FakeBotState state = WithSkinnable(distance: 20f);
        state.MovementFailed = true;

        Assert.Equal(RunStatus.Failure, RootTree.HandleSkinning().Tick(state));
    }

    [Fact]
    public void SkinningComesAfterLootingSoTheCharacterWalksToACorpseOnce()
    {
        // A corpse only becomes skinnable once its loot has been taken. Skinning first would
        // mean two trips to the same body.
        FakeBotState state = new();
        CandidateTarget lootable = state.AddEnemy(1, distance: 2f, alive: false);

        state.LootableCorpses = [lootable];
        state.SkinnableCorpses = [lootable];
        state.NearbyEnemies = [];

        RootTree.Build(new Do<IBotState>(_ => RunStatus.Success)).Tick(state);

        Assert.Contains(state.Actions, a => a.StartsWith("Loot", StringComparison.Ordinal));
    }

    [Fact]
    public void RestingWaitsForTheSkinBecauseACorpseIsOnATimerAndHealthIsNot()
    {
        FakeBotState state = WithSkinnable(distance: 2f);
        state.RecordingRoutine.Ready = false;
        state.RecordingRoutine.RestTicksRemaining = 5;

        RootTree.Build(new Do<IBotState>(_ => RunStatus.Success)).Tick(state);

        Assert.Contains(state.Actions, a => a.StartsWith("Interact", StringComparison.Ordinal));
        Assert.DoesNotContain("Rest", state.RecordingRoutine.Calls);
    }
}
