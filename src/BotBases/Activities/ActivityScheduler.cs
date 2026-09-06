using WoWBuddy.Common.Logging;
using WoWBuddy.Profiles;

namespace WoWBuddy.BotBases.Activities;

/// <summary>Why the current activity changed.</summary>
public enum ActivityChange
{
    /// <summary>It did not.</summary>
    None,

    /// <summary>Nothing was running and something now is.</summary>
    Started,

    /// <summary>Its time was up.</summary>
    Expired,

    /// <summary>Its conditions stopped holding.</summary>
    NoLongerEligible,

    /// <summary>Something the plan prefers became eligible.</summary>
    Preempted,

    /// <summary>Nothing is eligible.</summary>
    Stalled,
}

/// <summary>
/// Decides which activity the character should be doing.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a pure decision over plain values: given a plan, a clock and what the character
/// can see about itself, which activity is current. That makes "does this plan actually do what
/// its author meant" a question a test can answer, rather than something you find out nine
/// hours into a session.
/// </para>
/// <para>
/// Two rules keep it from thrashing. An activity runs for at least the plan's minimum dwell
/// before anything may displace it, and a preferred activity only pre-empts a running one at a
/// dwell boundary. Without both, a condition sitting on its threshold swaps activities several
/// times a second and the character does neither.
/// </para>
/// </remarks>
public sealed class ActivityScheduler
{
    private readonly ActivityPlan _plan;

    private Activity? _current;
    private DateTimeOffset _startedAt;
    private bool _warnedAboutStall;

    public ActivityScheduler(ActivityPlan plan)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
    }

    /// <summary>What the character should be doing, or null when nothing can be.</summary>
    public Activity? Current => _current;

    /// <summary>When the current activity started.</summary>
    public DateTimeOffset StartedAt => _startedAt;

    /// <summary>Why it last changed.</summary>
    public ActivityChange LastChange { get; private set; } = ActivityChange.None;

    /// <summary>How many times the activity has changed.</summary>
    public int Changes { get; private set; }

    /// <summary>
    /// Works out what should be running now.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="context">What the character knows about itself.</param>
    /// <returns>The activity to run, or null when none is eligible.</returns>
    public Activity? Update(DateTimeOffset now, IProfileConditionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Activity? best = FirstEligible(context);

        if (_current is null)
        {
            return Switch(best, now, best is null ? ActivityChange.Stalled : ActivityChange.Started);
        }

        // Conditions that stop holding end an activity immediately, dwell or no dwell. A dwell
        // is there to stop dithering between things worth doing, not to keep the character
        // mining with full bags.
        if (!_current.IsEligible(context))
        {
            return Switch(best, now, best is null ? ActivityChange.Stalled : ActivityChange.NoLongerEligible);
        }

        bool dwelt = now - _startedAt >= _plan.MinimumDwell;

        if (_current.Duration > TimeSpan.Zero && now - _startedAt >= _current.Duration)
        {
            return Switch(NextAfter(_current, context) ?? best, now, ActivityChange.Expired);
        }

        // A preferred activity waits for the dwell boundary. Letting it cut in the moment it
        // becomes eligible is exactly how a condition on a threshold produces a character that
        // stands still swapping between two things.
        if (dwelt && best is not null && !ReferenceEquals(best, _current) && IsPreferred(best, _current))
        {
            return Switch(best, now, ActivityChange.Preempted);
        }

        LastChange = ActivityChange.None;
        return _current;
    }

    /// <summary>Forgets what was running, for a new session.</summary>
    public void Reset()
    {
        _current = null;
        _startedAt = default;
        LastChange = ActivityChange.None;
        Changes = 0;
        _warnedAboutStall = false;
    }

    /// <summary>A sentence for the status bar.</summary>
    public string Describe(DateTimeOffset now)
    {
        if (_current is null)
        {
            return _plan.IsUsable
                ? "Nothing in the plan can run at the moment."
                : "The plan has no activities in it.";
        }

        TimeSpan spent = now - _startedAt;

        return _current.Duration > TimeSpan.Zero
            ? $"{_current.Name}, {spent.TotalMinutes:F0} of {_current.Duration.TotalMinutes:F0} minutes"
            : $"{_current.Name}, {spent.TotalMinutes:F0} minutes";
    }

    private Activity? FirstEligible(IProfileConditionContext context)
    {
        foreach (Activity activity in _plan.Activities)
        {
            if (activity.IsEligible(context))
            {
                return activity;
            }
        }

        return null;
    }

    /// <summary>
    /// The next eligible activity after this one, wrapping.
    /// </summary>
    /// <remarks>
    /// Used when an activity's time is up. Going back to the top of the list would re-select
    /// the same one whenever it is the most preferred and still eligible, so a plan of "an hour
    /// of this, an hour of that" would never reach the second hour.
    /// </remarks>
    private Activity? NextAfter(Activity current, IProfileConditionContext context)
    {
        int index = -1;

        for (int i = 0; i < _plan.Activities.Count; i++)
        {
            if (ReferenceEquals(_plan.Activities[i], current))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return null;
        }

        for (int step = 1; step <= _plan.Activities.Count; step++)
        {
            Activity candidate = _plan.Activities[(index + step) % _plan.Activities.Count];

            if (candidate.IsEligible(context))
            {
                return candidate;
            }
        }

        return null;
    }

    private bool IsPreferred(Activity candidate, Activity running) =>
        IndexOf(candidate) < IndexOf(running);

    private int IndexOf(Activity activity)
    {
        for (int i = 0; i < _plan.Activities.Count; i++)
        {
            if (ReferenceEquals(_plan.Activities[i], activity))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private Activity? Switch(Activity? next, DateTimeOffset now, ActivityChange why)
    {
        if (next is null)
        {
            if (_current is not null || !_warnedAboutStall)
            {
                _warnedAboutStall = true;
                Log.For<ActivityScheduler>().Warning(
                    "Nothing in the plan '{Plan}' can run at the moment. The character will "
                    + "stand still until something becomes eligible.", _plan.Name);
            }

            _current = null;
            LastChange = ActivityChange.Stalled;
            return null;
        }

        if (ReferenceEquals(next, _current))
        {
            LastChange = ActivityChange.None;
            return _current;
        }

        Log.For<ActivityScheduler>().Information(
            "Switching to {Activity} ({Why})", next.Name, why);

        _current = next;
        _startedAt = now;
        LastChange = why;
        Changes++;
        _warnedAboutStall = false;

        return _current;
    }
}
