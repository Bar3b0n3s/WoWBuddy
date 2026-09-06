namespace WoWBuddy.Common.Scheduling;

/// <summary>
/// When the bot should run, pause and stop.
/// </summary>
/// <remarks>
/// <para>
/// Honorbuddy had these and so does this. They exist because the alternative — a bot that
/// runs flat out for eighteen hours and stops the instant it is switched off — is both the
/// most conspicuous thing an account can do and the most likely to lose it.
/// </para>
/// <para>
/// <b>They are not a defence against detection</b> and are not presented as one. A server
/// that inspects behaviour will see a bot regardless. These make a session look less unlike a
/// person, which is a smaller claim.
/// </para>
/// </remarks>
public sealed record SessionSchedule
{
    /// <summary>How long the bot runs before stopping for good. Null means no limit.</summary>
    public TimeSpan? MaximumSessionLength { get; init; }

    /// <summary>Roughly how long the bot runs between breaks. Null disables breaks.</summary>
    public TimeSpan? WorkInterval { get; init; }

    /// <summary>Roughly how long a break lasts.</summary>
    public TimeSpan BreakLength { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How much the work and break intervals are varied, as a fraction of themselves.
    /// </summary>
    /// <remarks>
    /// A break every ninety minutes to the second is a more distinctive pattern than no
    /// breaks at all. A quarter means the interval lands anywhere from 75 per cent to 125 per
    /// cent of nominal.
    /// </remarks>
    public double Jitter { get; init; } = 0.25d;

    /// <summary>Stop once the character reaches this level. Null means never.</summary>
    public int? StopAtLevel { get; init; }

    /// <summary>Stop at this wall-clock time. Null means never.</summary>
    public DateTimeOffset? StopAt { get; init; }

    /// <summary>Whether to log the character out when stopping rather than leaving it standing.</summary>
    public bool LogOutOnStop { get; init; }
}

/// <summary>What the scheduler thinks the bot should be doing.</summary>
public enum SessionState
{
    /// <summary>Carry on.</summary>
    Running,

    /// <summary>Pause where it is; the bot will resume by itself.</summary>
    OnBreak,

    /// <summary>Stop for good.</summary>
    Stopped,
}

/// <summary>Why the scheduler stopped the session.</summary>
public enum StopReason
{
    /// <summary>Not stopped.</summary>
    None,

    /// <summary>The session ran for its full length.</summary>
    SessionLengthReached,

    /// <summary>The character reached the configured level.</summary>
    LevelReached,

    /// <summary>The configured wall-clock time passed.</summary>
    TimeReached,

    /// <summary>Something asked the scheduler to stop.</summary>
    Requested,

    /// <summary>Someone whispered the character.</summary>
    Whispered,

    /// <summary>The character fell out of the world and could not be got back in.</summary>
    Disconnected,
}
