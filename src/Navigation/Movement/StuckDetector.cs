using WoWBuddy.Common.Geometry;

namespace WoWBuddy.Navigation.Movement;

/// <summary>How stuck the character appears to be.</summary>
public enum StuckSeverity
{
    /// <summary>Moving as expected.</summary>
    Moving = 0,

    /// <summary>Not making progress, but not for long enough to act on.</summary>
    Suspected = 1,

    /// <summary>Definitely not making progress.</summary>
    Stuck = 2,

    /// <summary>Repeatedly stuck despite attempts to recover.</summary>
    Persistent = 3,
}

/// <summary>
/// Notices when the character has stopped making progress.
/// </summary>
/// <remarks>
/// <para>
/// Being stuck is the normal state of an unattended bot in a world full of fences, rocks and
/// doorways, and detecting it is what separates a bot that runs overnight from one that spends
/// six hours facing a tree. Detection is by observation rather than by asking the client:
/// position over time is the ground truth, and it catches every cause at once — geometry, a
/// closed door, a failed path, an unwalkable destination.
/// </para>
/// <para>
/// The detector is deliberately time-based rather than tick-based. Frame rate varies with
/// what is on screen, and a threshold in ticks would fire at different real-world intervals
/// in a city and in an empty field.
/// </para>
/// <para>
/// It knows nothing about how to get unstuck. That belongs to whatever is doing the moving,
/// which is the only thing that knows what it was trying to do.
/// </para>
/// </remarks>
public sealed class StuckDetector
{
    /// <summary>Distance the character must cover to count as making progress.</summary>
    /// <remarks>
    /// Comfortably above the jitter of a standing character and well below a walking
    /// character's speed, which is about seven yards a second.
    /// </remarks>
    public const float ProgressThreshold = 1.5f;

    /// <summary>How long without progress before the character is suspected of being stuck.</summary>
    public static readonly TimeSpan SuspicionWindow = TimeSpan.FromSeconds(1.5);

    /// <summary>How long without progress before it is treated as certain.</summary>
    public static readonly TimeSpan StuckWindow = TimeSpan.FromSeconds(4);

    /// <summary>Consecutive stuck episodes before the situation is treated as persistent.</summary>
    public const int PersistentEpisodeCount = 3;

    private Vector3 _referencePosition;
    private DateTimeOffset _referenceTime;
    private bool _started;

    /// <summary>How many times the character has been found stuck since the last reset.</summary>
    public int Episodes { get; private set; }

    /// <summary>The most recent assessment.</summary>
    public StuckSeverity Severity { get; private set; } = StuckSeverity.Moving;

    /// <summary>How long the character has been failing to make progress.</summary>
    public TimeSpan TimeWithoutProgress { get; private set; }

    /// <summary>
    /// Records the character's position and reassesses.
    /// </summary>
    /// <param name="position">Where the character is now.</param>
    /// <param name="now">The current time. Injected so the detector can be tested.</param>
    public StuckSeverity Update(Vector3 position, DateTimeOffset now)
    {
        if (!_started)
        {
            _referencePosition = position;
            _referenceTime = now;
            _started = true;
            Severity = StuckSeverity.Moving;
            return Severity;
        }

        if (position.Distance(_referencePosition) >= ProgressThreshold)
        {
            // Progress. Reset the clock but keep the episode count: a character that gets
            // stuck, wriggles free and gets stuck again is in a worse position than one that
            // has been stuck once, and the escalation should reflect that.
            _referencePosition = position;
            _referenceTime = now;
            TimeWithoutProgress = TimeSpan.Zero;
            Severity = StuckSeverity.Moving;
            return Severity;
        }

        TimeWithoutProgress = now - _referenceTime;

        StuckSeverity previous = Severity;

        Severity = TimeWithoutProgress switch
        {
            var t when t >= StuckWindow && Episodes >= PersistentEpisodeCount => StuckSeverity.Persistent,
            var t when t >= StuckWindow => StuckSeverity.Stuck,
            var t when t >= SuspicionWindow => StuckSeverity.Suspected,
            _ => StuckSeverity.Moving,
        };

        // Count the transition, not every tick spent stuck.
        if (Severity >= StuckSeverity.Stuck && previous < StuckSeverity.Stuck)
        {
            Episodes++;

            if (Episodes >= PersistentEpisodeCount)
            {
                Severity = StuckSeverity.Persistent;
            }
        }

        return Severity;
    }

    /// <summary>
    /// Clears the progress clock but keeps the episode count.
    /// </summary>
    /// <remarks>
    /// Call after attempting to get unstuck. The attempt should be given a fresh window to
    /// work in, but it must not erase the fact that it was needed, or a character wedged in
    /// scenery would loop forever between "stuck" and "just tried something".
    /// </remarks>
    public void NoteRecoveryAttempt(Vector3 position, DateTimeOffset now)
    {
        _referencePosition = position;
        _referenceTime = now;
        TimeWithoutProgress = TimeSpan.Zero;
        Severity = StuckSeverity.Moving;
        _started = true;
    }

    /// <summary>Forgets everything, including the episode count.</summary>
    /// <remarks>Call when starting a new path: past trouble says nothing about a new route.</remarks>
    public void Reset()
    {
        _started = false;
        Episodes = 0;
        Severity = StuckSeverity.Moving;
        TimeWithoutProgress = TimeSpan.Zero;
    }
}
