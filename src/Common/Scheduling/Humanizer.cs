namespace WoWBuddy.Common.Scheduling;

/// <summary>
/// Adds variation to the timing of what the bot does.
/// </summary>
/// <remarks>
/// <para>
/// A bot that reacts in exactly 0ms and loots in exactly 500ms produces timing histograms
/// that no person generates. Spreading those intervals costs almost nothing and removes the
/// most trivially detectable property of the whole program.
/// </para>
/// <para>
/// <b>This is not a defence against detection.</b> It removes an obvious signal, not the
/// underlying one: a bot still plays perfectly, forever, without eating or sleeping. It is
/// here because Honorbuddy had it and because a bot that switches target in zero milliseconds
/// is conspicuous to a person watching, which is the more likely way an account is reported.
/// </para>
/// <para>
/// Delays are produced as values rather than slept on. The behaviour tree is not allowed to
/// block, so a caller schedules a wait rather than stopping the thread.
/// </para>
/// </remarks>
public sealed class Humanizer
{
    private readonly Random _random;

    /// <param name="random">Injected so behaviour can be made deterministic in a test.</param>
    public Humanizer(Random? random = null)
    {
        _random = random ?? Random.Shared;
    }

    /// <summary>Whether variation is applied at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Shortest reaction to something appearing.</summary>
    public TimeSpan MinimumReaction { get; set; } = TimeSpan.FromMilliseconds(120);

    /// <summary>Longest reaction to something appearing.</summary>
    public TimeSpan MaximumReaction { get; set; } = TimeSpan.FromMilliseconds(450);

    /// <summary>
    /// A plausible pause before acting on something that has just become true.
    /// </summary>
    /// <remarks>
    /// Used before selecting a new target, starting to loot, or beginning a pull. Human
    /// reaction to an expected event is roughly 150 to 400 milliseconds, and the bot has no
    /// reason to be quicker.
    /// </remarks>
    public TimeSpan ReactionDelay()
    {
        if (!Enabled)
        {
            return TimeSpan.Zero;
        }

        double milliseconds = _random.NextDouble()
            * (MaximumReaction.TotalMilliseconds - MinimumReaction.TotalMilliseconds)
            + MinimumReaction.TotalMilliseconds;

        return TimeSpan.FromMilliseconds(milliseconds);
    }

    /// <summary>Varies a duration by up to <paramref name="fraction"/> either way.</summary>
    public TimeSpan Vary(TimeSpan nominal, double fraction = 0.2d)
    {
        if (!Enabled || fraction <= 0d)
        {
            return nominal;
        }

        double factor = 1d + ((_random.NextDouble() * 2d) - 1d) * fraction;
        return nominal * Math.Max(0.1d, factor);
    }

    /// <summary>
    /// A small offset to add to a destination so the bot does not stand on exactly the same
    /// spot every time it visits one.
    /// </summary>
    /// <remarks>
    /// Ten bots pathing to identical coordinates is a pattern visible from across the zone.
    /// The scatter is deliberately small: enough to break the pattern, not enough to put the
    /// character somewhere the path did not go.
    /// </remarks>
    public (float X, float Y) DestinationScatter(float radius = 2f)
    {
        if (!Enabled || radius <= 0f)
        {
            return (0f, 0f);
        }

        double angle = _random.NextDouble() * Math.PI * 2d;

        // Square-rooted so points spread evenly over the disc rather than clustering
        // in the middle, which is what a naive uniform radius produces.
        double distance = Math.Sqrt(_random.NextDouble()) * radius;

        return ((float)(Math.Cos(angle) * distance), (float)(Math.Sin(angle) * distance));
    }

    /// <summary>True with the given probability, for occasional harmless idiosyncrasies.</summary>
    public bool Chance(double probability) => Enabled && _random.NextDouble() < probability;
}
