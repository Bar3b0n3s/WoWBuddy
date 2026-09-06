using WoWBuddy.Common.Scheduling;
using Xunit;

namespace WoWBuddy.Common.Tests;

/// <summary>
/// Plays whole sessions out against an injected clock.
/// </summary>
/// <remarks>
/// An eight-hour session with breaks every ninety minutes takes a few milliseconds to check
/// this way, which is the only reason these conditions get tested at all.
/// </remarks>
public sealed class SessionSchedulerTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A scheduler with jitter switched off, so intervals are exact.</summary>
    private static SessionScheduler Deterministic(SessionSchedule schedule) =>
        new(schedule with { Jitter = 0d });

    [Fact]
    public void RunsIndefinitelyWithNoConditionsSet()
    {
        var scheduler = Deterministic(new SessionSchedule());

        Assert.Equal(SessionState.Running, scheduler.Update(Start, 70));
        Assert.Equal(SessionState.Running, scheduler.Update(Start.AddHours(12), 70));
    }

    [Fact]
    public void StopsAfterTheConfiguredSessionLength()
    {
        var scheduler = Deterministic(new SessionSchedule
        {
            MaximumSessionLength = TimeSpan.FromHours(4),
        });

        scheduler.Update(Start, 70);

        Assert.Equal(SessionState.Running, scheduler.Update(Start.AddHours(3), 70));
        Assert.Equal(SessionState.Stopped, scheduler.Update(Start.AddHours(4), 70));
        Assert.Equal(StopReason.SessionLengthReached, scheduler.StopReason);
    }

    [Fact]
    public void StopsWhenTheCharacterReachesTheTargetLevel()
    {
        var scheduler = Deterministic(new SessionSchedule { StopAtLevel = 60 });

        Assert.Equal(SessionState.Running, scheduler.Update(Start, 59));
        Assert.Equal(SessionState.Stopped, scheduler.Update(Start.AddMinutes(1), 60));
        Assert.Equal(StopReason.LevelReached, scheduler.StopReason);
    }

    [Fact]
    public void StopsAtAWallClockTime()
    {
        var scheduler = Deterministic(new SessionSchedule { StopAt = Start.AddHours(2) });

        scheduler.Update(Start, 70);

        Assert.Equal(SessionState.Stopped, scheduler.Update(Start.AddHours(2), 70));
        Assert.Equal(StopReason.TimeReached, scheduler.StopReason);
    }

    [Fact]
    public void TakesABreakAfterEachWorkInterval()
    {
        var scheduler = Deterministic(new SessionSchedule
        {
            WorkInterval = TimeSpan.FromMinutes(90),
            BreakLength = TimeSpan.FromMinutes(10),
        });

        scheduler.Update(Start, 70);

        Assert.Equal(SessionState.Running, scheduler.Update(Start.AddMinutes(89), 70));
        Assert.Equal(SessionState.OnBreak, scheduler.Update(Start.AddMinutes(90), 70));
        Assert.Equal(1, scheduler.BreaksTaken);
    }

    [Fact]
    public void ResumesByItselfWhenTheBreakEnds()
    {
        var scheduler = Deterministic(new SessionSchedule
        {
            WorkInterval = TimeSpan.FromMinutes(90),
            BreakLength = TimeSpan.FromMinutes(10),
        });

        scheduler.Update(Start, 70);
        scheduler.Update(Start.AddMinutes(90), 70);

        Assert.Equal(SessionState.OnBreak, scheduler.Update(Start.AddMinutes(95), 70));
        Assert.Equal(SessionState.Running, scheduler.Update(Start.AddMinutes(100), 70));
    }

    [Fact]
    public void TakesRepeatedBreaksOverALongSession()
    {
        var scheduler = Deterministic(new SessionSchedule
        {
            WorkInterval = TimeSpan.FromMinutes(60),
            BreakLength = TimeSpan.FromMinutes(10),
        });

        for (int minute = 0; minute <= 480; minute += 5)
        {
            scheduler.Update(Start.AddMinutes(minute), 70);
        }

        // Eight hours of hourly work intervals with ten-minute breaks: about seven cycles.
        Assert.InRange(scheduler.BreaksTaken, 6, 8);
    }

    [Fact]
    public void StoppingBeatsGoingOnABreak()
    {
        // A session that has reached its end should not sit on a break first.
        var scheduler = Deterministic(new SessionSchedule
        {
            MaximumSessionLength = TimeSpan.FromMinutes(90),
            WorkInterval = TimeSpan.FromMinutes(90),
        });

        scheduler.Update(Start, 70);

        Assert.Equal(SessionState.Stopped, scheduler.Update(Start.AddMinutes(90), 70));
    }

    [Fact]
    public void AStoppedSchedulerStaysStopped()
    {
        var scheduler = Deterministic(new SessionSchedule());
        scheduler.Update(Start, 70);
        scheduler.Stop();

        Assert.Equal(SessionState.Stopped, scheduler.Update(Start.AddHours(1), 70));
    }

    [Fact]
    public void ResetStartsAFreshSession()
    {
        var scheduler = Deterministic(new SessionSchedule
        {
            MaximumSessionLength = TimeSpan.FromMinutes(30),
        });

        scheduler.Update(Start, 70);
        scheduler.Update(Start.AddMinutes(30), 70);
        Assert.Equal(SessionState.Stopped, scheduler.State);

        scheduler.Reset();

        Assert.Equal(SessionState.Running, scheduler.Update(Start.AddHours(2), 70));
        Assert.Equal(0, scheduler.BreaksTaken);
    }

    [Fact]
    public void JitterMovesTheBreakOffTheNominalInterval()
    {
        // A break every ninety minutes to the second is a more distinctive pattern than no
        // breaks at all.
        var withJitter = new SessionScheduler(
            new SessionSchedule { WorkInterval = TimeSpan.FromMinutes(90), Jitter = 0.25d },
            new Random(12345));

        withJitter.Update(Start, 70);

        TimeSpan untilBreak = withJitter.NextBreakAt - Start;

        Assert.InRange(untilBreak.TotalMinutes, 67.5, 112.5);
        Assert.NotEqual(90d, untilBreak.TotalMinutes, 3);
    }

    [Fact]
    public void DescribeSaysWhatIsHappening()
    {
        var scheduler = Deterministic(new SessionSchedule
        {
            WorkInterval = TimeSpan.FromMinutes(90),
        });

        scheduler.Update(Start, 70);
        Assert.Contains("next break", scheduler.Describe(Start), StringComparison.OrdinalIgnoreCase);

        scheduler.Stop(StopReason.Requested);
        Assert.Contains("Stopped", scheduler.Describe(Start), StringComparison.Ordinal);
    }
}

public sealed class HumanizerTests
{
    [Fact]
    public void ReactionDelaysFallInTheConfiguredRange()
    {
        var humanizer = new Humanizer(new Random(1));

        for (int i = 0; i < 200; i++)
        {
            TimeSpan delay = humanizer.ReactionDelay();
            Assert.InRange(delay, humanizer.MinimumReaction, humanizer.MaximumReaction);
        }
    }

    [Fact]
    public void ReactionDelaysAreNotAllTheSame()
    {
        // The whole point: a constant delay is as distinctive as no delay.
        var humanizer = new Humanizer(new Random(2));
        var seen = new HashSet<double>();

        for (int i = 0; i < 50; i++)
        {
            seen.Add(humanizer.ReactionDelay().TotalMilliseconds);
        }

        Assert.True(seen.Count > 40);
    }

    [Fact]
    public void DisablingRemovesAllVariation()
    {
        var humanizer = new Humanizer(new Random(3)) { Enabled = false };

        Assert.Equal(TimeSpan.Zero, humanizer.ReactionDelay());
        Assert.Equal(TimeSpan.FromSeconds(5), humanizer.Vary(TimeSpan.FromSeconds(5)));
        Assert.Equal((0f, 0f), humanizer.DestinationScatter());
        Assert.False(humanizer.Chance(1d));
    }

    [Fact]
    public void ScatterStaysInsideItsRadius()
    {
        // Enough to break the pattern of ten bots on one pixel, not enough to put the
        // character somewhere the path did not go.
        var humanizer = new Humanizer(new Random(4));

        for (int i = 0; i < 500; i++)
        {
            (float x, float y) = humanizer.DestinationScatter(radius: 3f);
            Assert.True(MathF.Sqrt((x * x) + (y * y)) <= 3.001f);
        }
    }

    [Fact]
    public void VariedDurationsStayCloseToNominal()
    {
        var humanizer = new Humanizer(new Random(5));

        for (int i = 0; i < 200; i++)
        {
            TimeSpan varied = humanizer.Vary(TimeSpan.FromSeconds(10), fraction: 0.2d);
            Assert.InRange(varied.TotalSeconds, 8d, 12d);
        }
    }
}
