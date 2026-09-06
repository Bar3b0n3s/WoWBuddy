using WoWBuddy.WorldData;

namespace WoWBuddy.BotBases.Support;

/// <summary>What the bot refuses to pick a fight with.</summary>
public sealed record TargetFilterSettings
{
    /// <summary>Leave elites alone.</summary>
    /// <remarks>
    /// On by default. An elite of the character's own level kills a solo character, every
    /// time, and the fight looks winnable until it is not.
    /// </remarks>
    public bool AvoidElites { get; init; } = true;

    /// <summary>How many levels above the character is still worth attacking.</summary>
    /// <remarks>
    /// Three is about where a fight stops being reliable for most classes. This is a floor on
    /// recklessness rather than a tuning knob: a bot fighting things five levels up dies
    /// overnight even when each individual fight looks close.
    /// </remarks>
    public int LevelsAboveToAllow { get; init; } = 3;
}

/// <summary>
/// Deciding what not to attack, using what the server's own tables say.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the thing world data buys that nothing else can.</b> Whether a creature is an
/// elite is not in its descriptors and cannot be seen from memory: the bot finds out by pulling
/// it and dying. The database knows before the fight starts, and one column closes the single
/// most expensive gap in unattended grinding.
/// </para>
/// <para>
/// Without world data it answers "do not avoid", which leaves the bot exactly where it was.
/// A filter that refused everything it could not look up would stop a bot that had been working
/// perfectly well for the want of an export.
/// </para>
/// </remarks>
public sealed class TargetFilter(
    TargetFilterSettings? settings = null,
    Func<uint, CreatureTemplate?>? lookup = null)
{
    private readonly TargetFilterSettings _settings = settings ?? new TargetFilterSettings();

    /// <summary>What it refuses to attack.</summary>
    public TargetFilterSettings Settings => _settings;

    /// <summary>True when the bot has the data to make this judgement at all.</summary>
    public bool HasData => lookup is not null;

    /// <summary>Why a target was rejected, or empty when it was not.</summary>
    public string Reject(uint entry, int targetLevel, int myLevel)
    {
        if (lookup?.Invoke(entry) is not { } template)
        {
            // Nothing known. The level the client reports is still worth checking, because it
            // comes from the descriptors rather than the export.
            return TooHigh(targetLevel, myLevel) ? "it is too far above the character" : string.Empty;
        }

        if (template.IsBoss)
        {
            // Never, regardless of settings. A boss is not a thing a grinding character
            // recovers from mistaking for a mob.
            return $"{template.Name} is a boss";
        }

        if (_settings.AvoidElites && template.IsElite)
        {
            return $"{template.Name} is an elite";
        }

        // The export's level range is a better answer than the one unit in front of the
        // character, because a spawn can roll anywhere within it.
        int level = template.HasLevels ? template.MaxLevel : targetLevel;

        return TooHigh(level, myLevel)
            ? $"{template.Name} can be level {level}, which is too far above the character"
            : string.Empty;
    }

    /// <summary>True when this is something to leave alone.</summary>
    public bool ShouldAvoid(uint entry, int targetLevel, int myLevel) =>
        Reject(entry, targetLevel, myLevel).Length > 0;

    private bool TooHigh(int level, int myLevel) =>
        level > 0 && myLevel > 0 && level > myLevel + _settings.LevelsAboveToAllow;
}
