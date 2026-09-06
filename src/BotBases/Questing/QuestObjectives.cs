using WoWBuddy.Profiles;
using WoWBuddy.WorldData;

namespace WoWBuddy.BotBases.Questing;

/// <summary>
/// What a quest is actually asking for.
/// </summary>
/// <remarks>
/// <para>
/// The gap this closes is narrow and expensive. The client will say how far along an objective
/// is — "3/8" — but the words next to it are in whatever language the client is in, so the bot
/// can see that something is three eighths done and not what the something is. A profile can
/// say, and a good one does; an imported profile often does not, and then an objective step
/// falls back to killing whatever is nearby and hoping.
/// </para>
/// <para>
/// The user's own database export knows: <c>quest_template</c> lists the creatures and items
/// each quest wants, by id and by count. That turns "kill things here" into "kill these things
/// here", which is the difference between a step that finishes and one that grinds a hillside
/// until the profile's condition happens to change.
/// </para>
/// <para>
/// <b>Everything here is optional.</b> With no export the answers are empty and every caller
/// carries on exactly as it did before — the profile's own entry, or the log's completion flag.
/// </para>
/// </remarks>
public sealed class QuestObjectives
{
    private readonly Func<uint, QuestTemplate?>? _lookup;

    /// <summary>Builds a lookup over the user's world data, or over nothing.</summary>
    /// <param name="lookup">
    /// Finds a quest by id, usually <see cref="WorldDataSet.QuestFor"/>. Null when there is no
    /// export, which switches every answer here off rather than approximating one.
    /// </param>
    public QuestObjectives(Func<uint, QuestTemplate?>? lookup = null)
    {
        _lookup = lookup;
    }

    /// <summary>An objectives lookup that knows nothing, for when there is no export.</summary>
    public static QuestObjectives None { get; } = new();

    /// <summary>True when there is anything to look quests up in.</summary>
    public bool HasData => _lookup is not null;

    /// <summary>What the quest asks for, or null when it is not in the export.</summary>
    public QuestTemplate? For(uint questId) =>
        questId == 0 ? null : _lookup?.Invoke(questId);

    /// <summary>
    /// The creatures and objects a step should be working towards.
    /// </summary>
    /// <remarks>
    /// The profile wins when it names one: it was written by someone looking at the quest, and
    /// a quest that wants "any beast in the valley" is written as an entry the database cannot
    /// derive. This only answers when the profile said nothing.
    /// </remarks>
    public IReadOnlySet<uint> KillsFor(ProfileStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        if (step.Entry != 0)
        {
            return new HashSet<uint> { step.Entry };
        }

        if (For(step.QuestId) is not { } quest)
        {
            return new HashSet<uint>();
        }

        if (Single(quest, step) is { } only)
        {
            return only.IsItem ? new HashSet<uint>() : new HashSet<uint> { only.Entry };
        }

        return new HashSet<uint>(quest.Kills.Select(requirement => requirement.Entry));
    }

    /// <summary>
    /// The item a collect step is gathering, and how many, or null when nothing says.
    /// </summary>
    /// <remarks>
    /// Fills in what the profile left out. A step that names its own item and count keeps them:
    /// a profile can be describing one stage of a quest that wants several things, and the
    /// database has no way to know which stage the author meant.
    /// </remarks>
    public QuestRequirement? CollectionFor(ProfileStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        if (step is { ItemId: > 0, Count: > 0 })
        {
            return new QuestRequirement(step.ItemId, step.Count, IsItem: true);
        }

        if (For(step.QuestId) is not { } quest)
        {
            return null;
        }

        if (Single(quest, step) is { IsItem: true } only)
        {
            return only;
        }

        // With no objective index there is only an answer when the quest wants exactly one
        // thing. Guessing which of three collections a step meant would send the character
        // after the wrong one, and the step would never finish.
        QuestRequirement[] collections = [.. quest.Collections];

        return step.ObjectiveIndex == 0 && collections.Length == 1 ? collections[0] : null;
    }

    /// <summary>How many of what the step is after, or zero when nothing says.</summary>
    public int CountFor(ProfileStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        if (step.Count > 0)
        {
            return step.Count;
        }

        if (For(step.QuestId) is not { } quest)
        {
            return 0;
        }

        if (Single(quest, step) is { } only)
        {
            return only.Count;
        }

        return quest.Requirements.Count == 1 ? quest.Requirements[0].Count : 0;
    }

    /// <summary>What the quest wants, in a form a log line can carry.</summary>
    public string Describe(ProfileStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        if (For(step.QuestId) is not { HasRequirements: true } quest)
        {
            return string.Empty;
        }

        return string.Join(
            ", ",
            quest.Requirements.Select(requirement =>
                $"{requirement.Count} x {(requirement.IsItem ? "item" : "creature")} "
                + requirement.Entry));
    }

    /// <summary>
    /// The one requirement a step's objective index points at, when it points at one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The index is the client's own numbering of a quest's objectives, and the server stores
    /// them in the same order: the creatures it wants killed, then the items it wants brought.
    /// Lining the two up is what lets a step that says "objective 2" mean something specific.
    /// </para>
    /// <para>
    /// <c>// TODO: verify</c> — that the client's <c>GetQuestLogLeaderBoard</c> order matches
    /// the database's column order on 12340. To check: take a quest that wants both a kill and
    /// an item, and compare the log's second line against the export's second requirement. An
    /// index past the end simply falls through to using all of them, so being wrong here costs
    /// precision rather than correctness.
    /// </para>
    /// </remarks>
    private static QuestRequirement? Single(QuestTemplate quest, ProfileStep step)
    {
        int index = step.ObjectiveIndex - 1;

        return index >= 0 && index < quest.Requirements.Count ? quest.Requirements[index] : null;
    }
}
