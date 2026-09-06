using WoWBuddy.Common.Scheduling;
using Xunit;

namespace WoWBuddy.Common.Tests;

/// <summary>A character-select screen driven by hand.</summary>
internal sealed class FakeWorldEntry : IWorldEntry
{
    public bool CanEnterWorld { get; set; } = true;

    /// <summary>Whether the client actually goes in when asked.</summary>
    public bool Works { get; set; } = true;

    /// <summary>Which slots the bot asked for, in order.</summary>
    public List<int> Entered { get; } = [];

    public bool EnterWorld(int slot)
    {
        Entered.Add(slot);
        return Works;
    }
}

public sealed class ReconnectWatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static SessionScheduler Running()
    {
        SessionScheduler session = new(new SessionSchedule());
        session.Update(Now, characterLevel: 20);
        return session;
    }

    [Fact]
    public void ALoadingScreenIsNotADisconnection()
    {
        // Zoning, a portal and a crash all look identical for the first few seconds. Only the
        // length of them tells the three apart, which is why the wait exists.
        FakeWorldEntry entry = new();
        ReconnectWatch watch = new(new ReconnectSettings { WaitBefore = TimeSpan.FromSeconds(30) });

        Assert.False(watch.WhileAway(entry, Running(), Now));
        Assert.False(watch.WhileAway(entry, Running(), Now.AddSeconds(20)));
        Assert.Empty(entry.Entered);
    }

    [Fact]
    public void BeingGoneLongEnoughGetsTheCharacterBackIn()
    {
        FakeWorldEntry entry = new();
        SessionScheduler session = Running();
        ReconnectWatch watch = new(new ReconnectSettings { WaitBefore = TimeSpan.FromSeconds(30) });

        watch.WhileAway(entry, session, Now);

        Assert.True(watch.WhileAway(entry, session, Now.AddSeconds(31)));
        Assert.Equal([0], entry.Entered);
        Assert.Equal(1, watch.Attempts);
    }

    [Fact]
    public void ItEntersAsWhoeverIsAlreadyChosenUnlessToldOtherwise()
    {
        // Character select opens on the character that was last played, which is the one that
        // just fell out of the world — so naming a slot is the exception, not the rule.
        FakeWorldEntry entry = new();
        SessionScheduler session = Running();

        ReconnectWatch watch = new(new ReconnectSettings
        {
            WaitBefore = TimeSpan.Zero,
            CharacterSlot = 3,
        });

        watch.WhileAway(entry, session, Now);

        Assert.Equal([3], entry.Entered);
    }

    [Fact]
    public void ItWaitsBetweenAttemptsRatherThanHammeringTheScreen()
    {
        FakeWorldEntry entry = new() { Works = false };
        SessionScheduler session = Running();

        ReconnectWatch watch = new(new ReconnectSettings
        {
            WaitBefore = TimeSpan.Zero,
            BetweenAttempts = TimeSpan.FromSeconds(30),
        });

        watch.WhileAway(entry, session, Now);
        watch.WhileAway(entry, session, Now.AddSeconds(5));
        watch.WhileAway(entry, session, Now.AddSeconds(10));

        Assert.Single(entry.Entered);
    }

    [Fact]
    public void ItGivesUpAndStopsRatherThanClickingAllNight()
    {
        // A character that has not come back after a few tries is not coming back for a reason
        // the bot can do anything about, and something clicking at a screen for six hours is
        // worse than something that stopped and said why.
        FakeWorldEntry entry = new() { Works = false };
        SessionScheduler session = Running();

        ReconnectWatch watch = new(new ReconnectSettings
        {
            WaitBefore = TimeSpan.Zero,
            BetweenAttempts = TimeSpan.Zero,
            MaxAttempts = 2,
        });

        for (int tick = 0; tick < 10; tick++)
        {
            watch.WhileAway(entry, session, Now.AddSeconds(tick));
        }

        Assert.Equal(2, entry.Entered.Count);
        Assert.Equal(SessionState.Stopped, session.State);
        Assert.Equal(StopReason.Disconnected, session.StopReason);
    }

    [Fact]
    public void ALoginScreenNeedsAPersonAndTheBotSaysSoOnce()
    {
        // The line this project will not cross: it stores no credentials, so it cannot answer a
        // login screen, and pretending otherwise would be a retry loop that never ends.
        FakeWorldEntry entry = new() { CanEnterWorld = false };
        SessionScheduler session = Running();

        ReconnectWatch watch = new(new ReconnectSettings { WaitBefore = TimeSpan.Zero });

        watch.WhileAway(entry, session, Now);

        Assert.Empty(entry.Entered);
        Assert.Equal(SessionState.Stopped, session.State);
        Assert.Equal(StopReason.Disconnected, session.StopReason);
    }

    [Fact]
    public void ComingBackClearsTheCountSoTheNextDropStartsFresh()
    {
        FakeWorldEntry entry = new();
        SessionScheduler session = Running();

        ReconnectWatch watch = new(new ReconnectSettings
        {
            WaitBefore = TimeSpan.Zero,
            BetweenAttempts = TimeSpan.Zero,
        });

        watch.WhileAway(entry, session, Now);
        Assert.Equal(1, watch.Attempts);

        watch.Back();

        Assert.Equal(0, watch.Attempts);
        Assert.False(watch.IsWaiting);

        // And an hour later, a second drop is treated as its own.
        watch.WhileAway(entry, session, Now.AddHours(1));
        Assert.Equal(1, watch.Attempts);
    }

    [Fact]
    public void TurningItOffLeavesTheCharacterWhereItFell()
    {
        FakeWorldEntry entry = new();
        SessionScheduler session = Running();

        ReconnectWatch watch = new(new ReconnectSettings
        {
            Enabled = false,
            WaitBefore = TimeSpan.Zero,
        });

        Assert.False(watch.WhileAway(entry, session, Now));
        Assert.Empty(entry.Entered);
        Assert.Equal(SessionState.Running, session.State);
    }

    [Fact]
    public void AStoppedSessionIsNotDraggedBackIn()
    {
        FakeWorldEntry entry = new();
        SessionScheduler session = Running();
        session.Stop();

        ReconnectWatch watch = new(new ReconnectSettings { WaitBefore = TimeSpan.Zero });

        Assert.False(watch.WhileAway(entry, session, Now));
        Assert.Empty(entry.Entered);
    }
}
