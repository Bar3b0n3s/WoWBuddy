using WoWBuddy.Profiles;

namespace WoWBuddy.BotBases.Activities;

/// <summary>One thing the character does for a while.</summary>
/// <param name="Name">What to call it, and the bot base it runs.</param>
/// <param name="Duration">How long to spend on it before moving on. Zero means no limit.</param>
/// <param name="Conditions">Every one of these must hold for the activity to be eligible.</param>
/// <param name="ProfilePath">The profile this activity uses, where it needs one.</param>
public sealed record Activity(
    string Name,
    TimeSpan Duration = default,
    IReadOnlyList<ProfileCondition>? Conditions = null,
    string ProfilePath = "")
{
    /// <summary>Every condition that must hold.</summary>
    public IReadOnlyList<ProfileCondition> When => Conditions ?? [];

    /// <summary>True when the activity could run right now.</summary>
    public bool IsEligible(IProfileConditionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (ProfileCondition condition in When)
        {
            if (!condition.Evaluate(context))
            {
                return false;
            }
        }

        return true;
    }

    public override string ToString() => Duration > TimeSpan.Zero
        ? $"{Name} for {Duration.TotalMinutes:F0} minute(s)"
        : Name;
}

/// <summary>
/// A night's work: several activities, and the rules for moving between them.
/// </summary>
/// <remarks>
/// <para>
/// The thing a mixed plan is really for is not variety for its own sake. It is that the
/// profitable thing to be doing changes during a session — bags fill, a zone's nodes get
/// picked over, a level is reached — and a character that grinds the same spot for nine hours
/// is both less useful and considerably more obvious than one that does not.
/// </para>
/// <para>
/// Activities are tried in order and the first eligible one wins, which makes the list a
/// priority list rather than a rota. "Go and sell when the bags are full" is then just an
/// activity with a condition on it, sitting above the others.
/// </para>
/// </remarks>
public sealed record ActivityPlan
{
    /// <summary>Its name, for logs.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The activities, most preferred first.</summary>
    public IReadOnlyList<Activity> Activities { get; init; } = [];

    /// <summary>
    /// The shortest time an activity runs before anything may displace it.
    /// </summary>
    /// <remarks>
    /// The single most important setting here. Without it, an activity whose condition sits on
    /// a boundary — bags exactly at the threshold, health hovering at a limit — is swapped in
    /// and out several times a second, and the character stands still doing neither. A minimum
    /// dwell is what turns a set of conditions into behaviour.
    /// </remarks>
    public TimeSpan MinimumDwell { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>True when the plan has anything to do.</summary>
    public bool IsUsable => Activities.Count > 0;
}
