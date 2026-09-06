using WoWBuddy.Common.Logging;

namespace WoWBuddy.Common.Scheduling;

/// <summary>
/// Decides when the bot works, breaks and stops.
/// </summary>
/// <remarks>
/// <para>
/// A small state machine rather than a timer, driven by <see cref="Update"/> from the same
/// tick as everything else. That keeps it interruptible and, more usefully, testable: an
/// eight-hour session with breaks every ninety minutes can be played out in a few
/// milliseconds by advancing an injected clock.
/// </para>
/// <para>
/// Breaks are taken where the character stands. Walking somewhere safe first would be better
/// and is not attempted here, because "somewhere safe" needs a notion of safety the bot does
/// not have yet; a character standing still in the open is at least a state a person could
/// plausibly be in.
/// </para>
/// </remarks>
public sealed class SessionScheduler
{
    private readonly SessionSchedule _schedule;
    private readonly Random _random;

    private DateTimeOffset _sessionStart;
    private DateTimeOffset _nextBreakAt;
    private DateTimeOffset _breakEndsAt;
    private bool _started;

    /// <param name="schedule">What the user asked for.</param>
    /// <param name="random">
    /// Source of jitter. Injected so a test can make the intervals deterministic; in normal
    /// use the shared instance is right.
    /// </param>
    public SessionScheduler(SessionSchedule schedule, Random? random = null)
    {
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        _random = random ?? Random.Shared;
    }

    /// <summary>What the bot should be doing.</summary>
    public SessionState State { get; private set; } = SessionState.Running;

    /// <summary>Why it stopped, if it has.</summary>
    public StopReason StopReason { get; private set; } = StopReason.None;

    /// <summary>When the current break ends. Meaningless unless on a break.</summary>
    public DateTimeOffset BreakEndsAt => _breakEndsAt;

    /// <summary>When the next break is due.</summary>
    public DateTimeOffset NextBreakAt => _nextBreakAt;

    /// <summary>How long the session has been running, breaks included.</summary>
    public TimeSpan Elapsed { get; private set; }

    /// <summary>Number of breaks taken so far.</summary>
    public int BreaksTaken { get; private set; }

    /// <summary>
    /// Reassesses what the bot should be doing.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="characterLevel">The character's level, for the stop-at-level condition.</param>
    public SessionState Update(DateTimeOffset now, int characterLevel)
    {
        if (!_started)
        {
            _sessionStart = now;
            _started = true;
            ScheduleNextBreak(now);
        }

        Elapsed = now - _sessionStart;

        if (State == SessionState.Stopped)
        {
            return State;
        }

        // Stop conditions are checked before break handling: a session that has reached its
        // end should not sit on a break first.
        if (CheckStopConditions(now, characterLevel))
        {
            return State;
        }

        if (State == SessionState.OnBreak)
        {
            if (now >= _breakEndsAt)
            {
                State = SessionState.Running;
                ScheduleNextBreak(now);
                Log.For<SessionScheduler>().Information(
                    "Break over; next one at {Next:HH:mm}", _nextBreakAt);
            }

            return State;
        }

        if (_schedule.WorkInterval is not null && now >= _nextBreakAt)
        {
            _breakEndsAt = now + Jittered(_schedule.BreakLength);
            State = SessionState.OnBreak;
            BreaksTaken++;

            Log.For<SessionScheduler>().Information(
                "Taking a break until {Until:HH:mm} ({Count} so far)", _breakEndsAt, BreaksTaken);
        }

        return State;
    }

    /// <summary>Stops the session immediately.</summary>
    public void Stop(StopReason reason = StopReason.Requested)
    {
        State = SessionState.Stopped;
        StopReason = reason;
        Log.For<SessionScheduler>().Information("Session stopped: {Reason}", reason);
    }

    /// <summary>Starts a fresh session, clearing elapsed time and break counts.</summary>
    public void Reset()
    {
        _started = false;
        State = SessionState.Running;
        StopReason = StopReason.None;
        Elapsed = TimeSpan.Zero;
        BreaksTaken = 0;
    }

    /// <summary>A sentence describing what the scheduler is doing, for the status bar.</summary>
    public string Describe(DateTimeOffset now) => State switch
    {
        SessionState.Stopped => $"Stopped: {StopReason}",
        SessionState.OnBreak => $"On a break for another {(_breakEndsAt - now).TotalMinutes:F0} min",
        _ when _schedule.WorkInterval is not null =>
            $"Running; next break in {(_nextBreakAt - now).TotalMinutes:F0} min",
        _ => "Running",
    };

    private bool CheckStopConditions(DateTimeOffset now, int characterLevel)
    {
        if (_schedule.StopAtLevel is { } level && characterLevel >= level)
        {
            Stop(StopReason.LevelReached);
            return true;
        }

        if (_schedule.StopAt is { } stopAt && now >= stopAt)
        {
            Stop(StopReason.TimeReached);
            return true;
        }

        if (_schedule.MaximumSessionLength is { } maximum && Elapsed >= maximum)
        {
            Stop(StopReason.SessionLengthReached);
            return true;
        }

        return false;
    }

    private void ScheduleNextBreak(DateTimeOffset now)
    {
        _nextBreakAt = _schedule.WorkInterval is { } interval
            ? now + Jittered(interval)
            : DateTimeOffset.MaxValue;
    }

    /// <summary>Varies a duration by the configured jitter, never below a tenth of nominal.</summary>
    private TimeSpan Jittered(TimeSpan nominal)
    {
        if (_schedule.Jitter <= 0d)
        {
            return nominal;
        }

        double factor = 1d + ((_random.NextDouble() * 2d) - 1d) * _schedule.Jitter;
        return nominal * Math.Max(0.1d, factor);
    }
}
