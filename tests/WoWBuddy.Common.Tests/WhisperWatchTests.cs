using WoWBuddy.Common.Scheduling;
using Xunit;

namespace WoWBuddy.Common.Tests;

/// <summary>Whispers handed over by the test rather than by a client.</summary>
internal sealed class FakeWhispers : IWhisperSource
{
    private readonly Queue<Whisper[]> _batches = new();

    /// <summary>How many times the bot emptied the buffer.</summary>
    public int Drains { get; private set; }

    public IReadOnlyList<Whisper> Drain()
    {
        Drains++;
        return _batches.Count > 0 ? _batches.Dequeue() : [];
    }

    /// <summary>Queues what the next drain finds.</summary>
    public FakeWhispers Then(params Whisper[] whispers)
    {
        _batches.Enqueue(whispers);
        return this;
    }
}

public sealed class WhisperWatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static Whisper From(string who, string what = "hey") => new(who, what, Now);

    private static SessionScheduler Running()
    {
        SessionScheduler session = new(new SessionSchedule());
        session.Update(Now, characterLevel: 20);
        return session;
    }

    [Fact]
    public void BeingSpokenToStandsTheCharacterStill()
    {
        // The whole point. A character that keeps killing boars while a game master asks it a
        // question has answered the question.
        SessionScheduler session = Running();
        WhisperWatch watch = new();

        Assert.True(watch.Consider(new FakeWhispers().Then(From("Gamemaster")), session, Now));
        Assert.Equal(SessionState.OnBreak, session.State);
        Assert.Equal("Gamemaster", watch.Acted?.From);
    }

    [Fact]
    public void ItStandsStillForAsLongAsItWasTold()
    {
        SessionScheduler session = Running();

        WhisperWatch watch = new(new WhisperSettings { PauseFor = TimeSpan.FromMinutes(30) });
        watch.Consider(new FakeWhispers().Then(From("Someone")), session, Now);

        Assert.Equal(Now.AddMinutes(30), session.BreakEndsAt);
    }

    [Fact]
    public void AConversationKeepsItStillRatherThanRestartingTheClock()
    {
        // Ten minutes from the first line would have the character start playing again in the
        // middle of the conversation, which is worse than never having stopped.
        SessionScheduler session = Running();
        WhisperWatch watch = new(new WhisperSettings { PauseFor = TimeSpan.FromMinutes(10) });

        watch.Consider(new FakeWhispers().Then(From("Someone")), session, Now);

        DateTimeOffset later = Now.AddMinutes(5);
        watch.Consider(new FakeWhispers().Then(From("Someone")), session, later);

        Assert.Equal(later.AddMinutes(10), session.BreakEndsAt);
    }

    [Fact]
    public void ASecondWhisperNeverShortensTheQuiet()
    {
        // The scheduler's rule, checked from here because getting it backwards would have a
        // second interruption cut the first one short.
        SessionScheduler session = Running();
        WhisperWatch watch = new(new WhisperSettings { PauseFor = TimeSpan.FromMinutes(10) });

        watch.Consider(new FakeWhispers().Then(From("Someone")), session, Now);
        watch.Consider(new FakeWhispers().Then(From("Someone")), session, Now.AddMinutes(-5));

        Assert.Equal(Now.AddMinutes(10), session.BreakEndsAt);
    }

    [Fact]
    public void StoppingIsForGoodAndNothingAfterItMatters()
    {
        SessionScheduler session = Running();
        WhisperWatch watch = new(new WhisperSettings { Response = WhisperResponse.Stop });

        watch.Consider(new FakeWhispers().Then(From("Gamemaster")), session, Now);

        Assert.Equal(SessionState.Stopped, session.State);
        Assert.Equal(StopReason.Whispered, session.StopReason);

        // And the session does not quietly resume when the scheduler is next asked.
        Assert.Equal(SessionState.Stopped, session.Update(Now.AddHours(1), characterLevel: 20));
    }

    [Fact]
    public void SomeoneOnTheIgnoreListDoesNotStopIt()
    {
        // Without this the feature is unusable in a guild: one "afk?" and the session is over.
        SessionScheduler session = Running();

        WhisperWatch watch = new(new WhisperSettings
        {
            Ignore = new HashSet<string>(["Myalt"], StringComparer.OrdinalIgnoreCase),
        });

        Assert.False(watch.Consider(new FakeWhispers().Then(From("myalt")), session, Now));
        Assert.Equal(SessionState.Running, session.State);

        // Still recorded, because the window should show it was said.
        Assert.Equal(1, watch.Count);
        Assert.Null(watch.Acted);
    }

    [Fact]
    public void IgnoringWhispersEntirelyStillNoticesThem()
    {
        // A user who wants to be told without being stopped, which is a reasonable thing to
        // want on a private server they run themselves.
        SessionScheduler session = Running();
        WhisperWatch watch = new(new WhisperSettings { Response = WhisperResponse.Ignore });

        Assert.False(watch.Consider(new FakeWhispers().Then(From("Someone")), session, Now));
        Assert.Equal(SessionState.Running, session.State);
        Assert.Single(watch.Seen);
    }

    [Fact]
    public void OneOfSeveralWhispersInATickIsEnough()
    {
        SessionScheduler session = Running();
        WhisperWatch watch = new();

        watch.Consider(
            new FakeWhispers().Then(From("Alice"), From("Bob"), From("Carol")),
            session,
            Now);

        Assert.Equal(3, watch.Count);
        Assert.Equal("Carol", watch.Acted?.From);
        Assert.Equal(SessionState.OnBreak, session.State);
    }

    [Fact]
    public void OnlySoManyAreKeptForTheWindow()
    {
        SessionScheduler session = Running();
        WhisperWatch watch = new(new WhisperSettings { Remember = 2 });

        watch.Consider(
            new FakeWhispers().Then(From("Alice"), From("Bob"), From("Carol")),
            session,
            Now);

        Assert.Equal(2, watch.Seen.Count);
        Assert.Equal("Carol", watch.Seen[^1].From);
    }

    [Fact]
    public void NothingSaidIsNothingDone()
    {
        SessionScheduler session = Running();
        WhisperWatch watch = new();

        Assert.False(watch.Consider(new FakeWhispers(), session, Now));
        Assert.Equal(SessionState.Running, session.State);
    }

    [Fact]
    public void APausedSessionCarriesOnByItselfWhenTheQuietIsOver()
    {
        // Pausing is a break, and a break ends. A pause that needed a person to clear it would
        // be a stop wearing a different name.
        SessionScheduler session = Running();
        WhisperWatch watch = new(new WhisperSettings { PauseFor = TimeSpan.FromMinutes(10) });

        watch.Consider(new FakeWhispers().Then(From("Someone")), session, Now);

        Assert.Equal(SessionState.Running, session.Update(Now.AddMinutes(11), characterLevel: 20));
    }
}
