using System.Globalization;

namespace WoWBuddy.Profiles;

/// <summary>
/// Turns the text of a profile's <c>Condition</c> attribute into <see cref="ProfileCondition"/>s.
/// </summary>
/// <remarks>
/// Clauses are separated by <c>&amp;&amp;</c> and every one of them must hold. There is no
/// <c>||</c>: a profile that needs alternatives can write two steps. Keeping the grammar this
/// small means a profile either loads with every condition understood or reports exactly which
/// clause was not, which is the whole point of validating at load time.
/// </remarks>
public static class ProfileConditionParser
{
    private static readonly (string Text, ComparisonOperator Operator)[] Operators =
    [
        // Longest first: "<=" must not be read as "<".
        (">=", ComparisonOperator.GreaterOrEqual),
        ("<=", ComparisonOperator.LessOrEqual),
        ("==", ComparisonOperator.Equal),
        ("!=", ComparisonOperator.NotEqual),
        ("=", ComparisonOperator.Equal),
        (">", ComparisonOperator.Greater),
        ("<", ComparisonOperator.Less),
    ];

    private static readonly HashSet<ConditionTerm> TermsTakingAnArgument =
    [
        ConditionTerm.QuestCompleted,
        ConditionTerm.QuestInLog,
        ConditionTerm.QuestReadyToTurnIn,
        ConditionTerm.ItemCount,
    ];

    /// <summary>
    /// Parses every clause of <paramref name="text"/>.
    /// </summary>
    /// <param name="text">The attribute's value. Empty or whitespace yields no conditions.</param>
    /// <param name="conditions">The parsed clauses, in written order.</param>
    /// <param name="error">Why parsing failed, when it did.</param>
    /// <returns>True when every clause parsed.</returns>
    public static bool TryParseAll(
        string? text,
        out IReadOnlyList<ProfileCondition> conditions,
        out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            conditions = [];
            return true;
        }

        List<ProfileCondition> parsed = [];

        foreach (string clause in text.Split("&&", StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.IsNullOrWhiteSpace(clause))
            {
                continue;
            }

            if (!TryParse(clause, out ProfileCondition? condition, out error))
            {
                conditions = [];
                return false;
            }

            parsed.Add(condition);
        }

        if (parsed.Count == 0)
        {
            conditions = [];
            error = $"'{text}' contains no condition.";
            return false;
        }

        conditions = parsed;
        return true;
    }

    /// <summary>Parses a single clause.</summary>
    /// <param name="text">The clause.</param>
    /// <param name="condition">The parsed clause.</param>
    /// <param name="error">Why parsing failed, when it did.</param>
    /// <returns>True when the clause parsed.</returns>
    public static bool TryParse(
        string? text,
        out ProfileCondition condition,
        out string error)
    {
        condition = null!;
        error = string.Empty;

        string source = (text ?? string.Empty).Trim();

        if (source.Length == 0)
        {
            error = "The condition is empty.";
            return false;
        }

        string remaining = source;
        bool negated = false;

        while (remaining.StartsWith('!') && !remaining.StartsWith("!=", StringComparison.Ordinal))
        {
            negated = !negated;
            remaining = remaining[1..].TrimStart();
        }

        string left = remaining;
        ComparisonOperator comparison = ComparisonOperator.NotEqual;
        long value = 0;
        bool hasComparison = false;

        foreach ((string opText, ComparisonOperator op) in Operators)
        {
            int index = remaining.IndexOf(opText, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            left = remaining[..index];
            string rightText = remaining[(index + opText.Length)..].Trim();

            if (!long.TryParse(rightText, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                error = $"'{source}' compares against '{rightText}', which is not a whole number.";
                return false;
            }

            comparison = op;
            hasComparison = true;
            break;
        }

        if (!TryParseTerm(left, source, out ConditionTerm term, out uint argument, out error))
        {
            return false;
        }

        if (!hasComparison && term is not (ConditionTerm.QuestCompleted
            or ConditionTerm.QuestInLog
            or ConditionTerm.QuestReadyToTurnIn))
        {
            error = $"'{source}' needs a comparison, for example '{term} >= 1'.";
            return false;
        }

        if (negated)
        {
            if (hasComparison)
            {
                error = $"'{source}' negates a comparison. Write the opposite comparison instead.";
                return false;
            }

            comparison = ComparisonOperator.Equal;
            value = 0;
        }

        condition = new ProfileCondition(term, argument, comparison, value, source);
        return true;
    }

    private static bool TryParseTerm(
        string text,
        string source,
        out ConditionTerm term,
        out uint argument,
        out string error)
    {
        term = default;
        argument = 0;
        error = string.Empty;

        string name = text.Trim();

        int open = name.IndexOf('(');
        if (open >= 0)
        {
            if (!name.EndsWith(')'))
            {
                error = $"'{source}' is missing a closing bracket.";
                return false;
            }

            string inner = name[(open + 1)..^1].Trim();
            if (!uint.TryParse(inner, NumberStyles.Integer, CultureInfo.InvariantCulture, out argument))
            {
                error = $"'{source}' has '{inner}' in brackets, which is not an id.";
                return false;
            }

            name = name[..open].Trim();
        }

        if (!Enum.TryParse(name, ignoreCase: true, out term) || !Enum.IsDefined(term))
        {
            error = $"'{source}' asks about '{name}', which the bot does not know. "
                + $"Known terms: {string.Join(", ", Enum.GetNames<ConditionTerm>())}.";
            return false;
        }

        bool needsArgument = TermsTakingAnArgument.Contains(term);

        if (needsArgument && open < 0)
        {
            error = $"'{source}' needs an id in brackets, for example '{term}(1234)'.";
            return false;
        }

        if (!needsArgument && open >= 0)
        {
            error = $"'{source}' takes no id in brackets.";
            return false;
        }

        return true;
    }
}
