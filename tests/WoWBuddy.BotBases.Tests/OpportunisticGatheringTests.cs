using WoWBuddy.Behavior;
using WoWBuddy.BotBases.Activities;
using Xunit;

namespace WoWBuddy.BotBases.Tests;

public sealed class OpportunisticGatheringTests
{
    private const uint CopperVein = 1731;

    private static OpportunisticSettings Settings(
        float detour = 40f,
        int consecutive = 3,
        TimeSpan? cooldown = null) => new()
    {
        NodeEntries = new HashSet<uint> { CopperVein },
        DetourRange = detour,
        ConsecutiveLimit = consecutive,
        Cooldown = cooldown ?? TimeSpan.FromMinutes(1),
    };

    [Fact]
    public void TakesANodeItWalkedPast()
    {
        FakeBotState state = new();
        state.AddVisible(1, CopperVein, distance: 20f);

        OpportunisticGathering gathering = new(Settings());

        Assert.Equal(RunStatus.Running, gathering.Build().Tick(state));
        Assert.NotEmpty(state.MoveRequests);
    }

    [Fact]
    public void InteractsOnceItIsCloseEnough()
    {
        FakeBotState state = new();
        VisibleObject node = state.AddVisible(1, CopperVein, distance: 2f);

        new OpportunisticGathering(Settings()).Build().Tick(state);

        Assert.Contains($"Interact({node.Guid})", state.Actions);
        Assert.Contains("StopMoving", state.Actions);
    }

    [Fact]
    public void LeavesAloneAnythingFurtherThanTheDetourRange()
    {
        // The point is "there is a vein on the way", not "abandon the quest and go mining".
        FakeBotState state = new();
        state.AddVisible(1, CopperVein, distance: 200f);

        OpportunisticGathering gathering = new(Settings(detour: 40f));

        Assert.Null(gathering.Choose(state));
        Assert.Equal(RunStatus.Failure, gathering.Build().Tick(state));
    }

    [Fact]
    public void FailsWhenThereIsNothingToTakeSoTheMainTaskRuns()
    {
        // What makes this a decorator on someone else's plan rather than a plan of its own.
        FakeBotState state = new();

        Assert.Equal(RunStatus.Failure, new OpportunisticGathering(Settings()).Build().Tick(state));
    }

    [Fact]
    public void NeverStopsToMineMidFight()
    {
        // Stopping to mine while a wolf chews on the character is the single most obvious bot
        // behaviour there is.
        FakeBotState state = new() { IsInCombat = true };
        state.AddVisible(1, CopperVein, distance: 5f);

        Assert.Null(new OpportunisticGathering(Settings()).Choose(state));
    }

    [Fact]
    public void NeverStopsWithSomethingTargeted()
    {
        FakeBotState state = new();
        CandidateTarget enemy = state.AddEnemy(9, distance: 30f);
        state.SetTarget(enemy.Guid);
        state.AddVisible(1, CopperVein, distance: 5f);

        Assert.Null(new OpportunisticGathering(Settings()).Choose(state));
    }

    [Fact]
    public void IgnoresEntriesItWasNotToldAbout()
    {
        // Node ids differ between servers, so nothing is assumed.
        FakeBotState state = new();
        state.AddVisible(1, entry: 9999, distance: 5f);

        Assert.Null(new OpportunisticGathering(Settings()).Choose(state));
    }

    [Fact]
    public void TakesTheNearestFirstBecauseThatIsTheJustification()
    {
        FakeBotState state = new();
        state.AddVisible(1, CopperVein, distance: 30f);
        VisibleObject nearest = state.AddVisible(2, CopperVein, distance: 8f);

        Assert.Equal(nearest.Guid, new OpportunisticGathering(Settings()).Choose(state)!.Value.Guid);
    }

    [Fact]
    public void ADenseFieldDoesNotHoldAQuestingCharacterForever()
    {
        // Every node it walks to puts it within range of the next; the cap is what guarantees
        // the main task makes progress.
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        OpportunisticGathering gathering = new(Settings(consecutive: 2), () => now);

        FakeBotState state = new();
        state.AddVisible(1, CopperVein, distance: 2f);
        state.AddVisible(2, CopperVein, distance: 3f);
        state.AddVisible(3, CopperVein, distance: 3f);

        gathering.Build().Tick(state);
        gathering.Build().Tick(state);

        Assert.True(gathering.IsResting);
        Assert.Equal(RunStatus.Failure, gathering.Build().Tick(state));
    }

    [Fact]
    public void ItStartsLookingAgainAfterTheRest()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        OpportunisticGathering gathering = new(
            Settings(consecutive: 1, cooldown: TimeSpan.FromMinutes(1)),
            () => now);

        FakeBotState state = new();
        state.AddVisible(1, CopperVein, distance: 2f);
        state.AddVisible(2, CopperVein, distance: 3f);

        gathering.Build().Tick(state);
        Assert.True(gathering.IsResting);

        now += TimeSpan.FromMinutes(2);

        Assert.False(gathering.IsResting);
        Assert.NotNull(gathering.Choose(state));
    }

    [Fact]
    public void ANodeItJustTookIsLeftAlone()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        OpportunisticGathering gathering = new(Settings(), () => now);

        FakeBotState state = new();
        state.AddVisible(1, CopperVein, distance: 2f);

        gathering.Build().Tick(state);

        Assert.Equal(1, gathering.NodesOnCooldown);
        Assert.Null(gathering.Choose(state));
    }

    [Fact]
    public void ANodeItCannotReachIsGivenUpOnRatherThanWalkedIntoAllNight()
    {
        FakeBotState state = new() { MovementFailed = true };
        state.AddVisible(1, CopperVein, distance: 20f);

        OpportunisticGathering gathering = new(Settings());

        Assert.Equal(RunStatus.Failure, gathering.Build().Tick(state));
        Assert.Equal(1, gathering.NodesOnCooldown);
        Assert.Null(gathering.Choose(state));
    }

    [Fact]
    public void ACooldownExpiresSoTheNodeCanBeTakenAgain()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        OpportunisticGathering gathering = new(
            new OpportunisticSettings
            {
                NodeEntries = new HashSet<uint> { CopperVein },
                NodeCooldown = TimeSpan.FromMinutes(8),
            },
            () => now);

        FakeBotState state = new();
        state.AddVisible(1, CopperVein, distance: 2f);

        gathering.Build().Tick(state);
        Assert.Null(gathering.Choose(state));

        now += TimeSpan.FromMinutes(9);

        Assert.NotNull(gathering.Choose(state));
        Assert.Equal(0, gathering.NodesOnCooldown);
    }

    [Fact]
    public void SettingsThatCouldNeverReachAnythingAreRefused()
    {
        FakeBotState state = new();
        state.AddVisible(1, CopperVein, distance: 2f);

        OpportunisticGathering gathering = new(new OpportunisticSettings
        {
            NodeEntries = new HashSet<uint> { CopperVein },
            DetourRange = 4f,
            InteractRange = 4f,
        });

        Assert.Null(gathering.Choose(state));
    }

    [Fact]
    public void NothingConfiguredMeansNothingHappens()
    {
        FakeBotState state = new();
        state.AddVisible(1, CopperVein, distance: 2f);

        Assert.Null(new OpportunisticGathering().Choose(state));
    }

    [Fact]
    public void ResettingForgetsTheCooldownsAndTheRest()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        OpportunisticGathering gathering = new(Settings(consecutive: 1), () => now);

        FakeBotState state = new();
        state.AddVisible(1, CopperVein, distance: 2f);

        gathering.Build().Tick(state);
        Assert.True(gathering.IsResting);

        gathering.Reset();

        Assert.False(gathering.IsResting);
        Assert.Equal(0, gathering.NodesOnCooldown);
    }
}
