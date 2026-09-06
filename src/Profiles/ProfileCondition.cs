namespace WoWBuddy.Profiles;

/// <summary>The thing a profile condition asks about.</summary>
public enum ConditionTerm
{
    /// <summary>The character's level. Takes no argument.</summary>
    Level,

    /// <summary>How much money the character has, in copper. Takes no argument.</summary>
    Money,

    /// <summary>Whether a quest has been completed and turned in. Takes a quest id.</summary>
    QuestCompleted,

    /// <summary>Whether a quest is currently in the quest log. Takes a quest id.</summary>
    QuestInLog,

    /// <summary>Whether a quest in the log is ready to hand in. Takes a quest id.</summary>
    QuestReadyToTurnIn,

    /// <summary>How many of an item the character is carrying. Takes an item id.</summary>
    ItemCount,

    /// <summary>How full the character's bags are, as a percentage. Takes no argument.</summary>
    BagsFullPercent,
}

/// <summary>How a condition's two sides are compared.</summary>
public enum ComparisonOperator
{
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
}

/// <summary>
/// What a profile condition is allowed to ask the bot about.
/// </summary>
/// <remarks>
/// Every member is a plain value or a plain lookup, so conditions can be exercised in tests
/// without a game client attached. See <see cref="ProfileCondition"/> for why the condition
/// language is this small.
/// </remarks>
public interface IProfileConditionContext
{
    /// <summary>The character's level.</summary>
    int Level { get; }

    /// <summary>Money carried, in copper.</summary>
    long Money { get; }

    /// <summary>How full the bags are, 0 to 100.</summary>
    int BagsFullPercent { get; }

    /// <summary>True when the quest has been handed in.</summary>
    bool IsQuestCompleted(uint questId);

    /// <summary>True when the quest is in the log, finished or not.</summary>
    bool IsQuestInLog(uint questId);

    /// <summary>True when the quest is in the log and all its objectives are done.</summary>
    bool IsQuestReadyToTurnIn(uint questId);

    /// <summary>How many of an item the character is carrying, across all bags.</summary>
    int ItemCount(uint itemId);
}

/// <summary>
/// A single test a profile can put on a step.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a tiny language rather than a scripting one: a term the bot knows, an
/// optional id, a comparison and a number. Profiles are files downloaded from strangers and
/// run unattended against someone's account, so a profile that could execute arbitrary code
/// would be a far larger promise than this project wants to make. Anything the parser does
/// not understand is reported when the profile is loaded rather than when the step is
/// reached, which for a questing profile could be an hour of play later.
/// </para>
/// <para>
/// Written forms, all equivalent to a comparison against a number:
/// <c>Level &gt;= 12</c>, <c>ItemCount(2589) &gt;= 5</c>, <c>QuestCompleted(1234)</c> (true when
/// non-zero), <c>!QuestInLog(1234)</c> (true when zero).
/// </para>
/// </remarks>
/// <param name="Term">What is being asked about.</param>
/// <param name="Argument">The quest or item id, where the term takes one.</param>
/// <param name="Operator">How the two sides are compared.</param>
/// <param name="Value">The number compared against.</param>
/// <param name="Source">The text this was parsed from, for messages.</param>
public sealed record ProfileCondition(
    ConditionTerm Term,
    uint Argument,
    ComparisonOperator Operator,
    long Value,
    string Source)
{
    /// <summary>True when the condition holds for <paramref name="context"/>.</summary>
    public bool Evaluate(IProfileConditionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        long left = Term switch
        {
            ConditionTerm.Level => context.Level,
            ConditionTerm.Money => context.Money,
            ConditionTerm.BagsFullPercent => context.BagsFullPercent,
            ConditionTerm.QuestCompleted => context.IsQuestCompleted(Argument) ? 1 : 0,
            ConditionTerm.QuestInLog => context.IsQuestInLog(Argument) ? 1 : 0,
            ConditionTerm.QuestReadyToTurnIn => context.IsQuestReadyToTurnIn(Argument) ? 1 : 0,
            ConditionTerm.ItemCount => context.ItemCount(Argument),
            _ => 0,
        };

        return Operator switch
        {
            ComparisonOperator.Equal => left == Value,
            ComparisonOperator.NotEqual => left != Value,
            ComparisonOperator.Less => left < Value,
            ComparisonOperator.LessOrEqual => left <= Value,
            ComparisonOperator.Greater => left > Value,
            ComparisonOperator.GreaterOrEqual => left >= Value,
            _ => false,
        };
    }

    public override string ToString() => Source;
}
