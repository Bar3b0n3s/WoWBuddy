using System.Text.RegularExpressions;

namespace WoWBuddy.Profiles.Import;

/// <summary>What translating one Honorbuddy condition produced.</summary>
/// <param name="Translated">The condition in this bot's language, when it could be written.</param>
/// <param name="Original">What the profile said.</param>
/// <param name="Understood">True when every clause was translated.</param>
/// <param name="Problem">Which clause could not be translated, and why.</param>
public readonly record struct ConditionTranslation(
    string Translated,
    string Original,
    bool Understood,
    string Problem);

/// <summary>
/// Rewrites Honorbuddy conditions into this bot's condition language, where it can.
/// </summary>
/// <remarks>
/// <para>
/// This is the part of an Honorbuddy profile that translates worst, and it is worth being
/// blunt about why. Honorbuddy conditions are C# expressions compiled and run by the bot: they
/// can call anything its API exposes, and in practice profiles use a long tail of that. This
/// bot's conditions are a fixed vocabulary of seven terms on purpose — see
/// <see cref="ProfileCondition"/> — so there is no general translation, only a mapping of the
/// handful of forms that come up constantly.
/// </para>
/// <para>
/// Anything outside that handful is reported with the original text rather than guessed at. A
/// condition silently dropped would turn "only do this below level 20" into "always do this",
/// which is exactly the sort of change that looks fine for an hour and then is not.
/// </para>
/// </remarks>
public static partial class HonorbuddyConditionTranslator
{
    /// <summary>Translates a whole condition, which may be several clauses joined by &amp;&amp;.</summary>
    /// <param name="condition">The condition as the profile wrote it.</param>
    /// <returns>What could be made of it.</returns>
    public static ConditionTranslation Translate(string? condition)
    {
        string original = (condition ?? string.Empty).Trim();

        if (original.Length == 0)
        {
            return new ConditionTranslation(string.Empty, original, true, string.Empty);
        }

        if (original.Contains("||", StringComparison.Ordinal))
        {
            return new ConditionTranslation(
                string.Empty,
                original,
                false,
                "it uses ||, and this bot's conditions have no 'or'. Split the step in two.");
        }

        List<string> clauses = [];

        foreach (string clause in original.Split("&&", StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = Unwrap(clause);

            if (trimmed.Length == 0)
            {
                continue;
            }

            if (!TryTranslateClause(trimmed, out string translated, out string problem))
            {
                return new ConditionTranslation(string.Empty, original, false, problem);
            }

            clauses.Add(translated);
        }

        if (clauses.Count == 0)
        {
            return new ConditionTranslation(
                string.Empty,
                original,
                false,
                "there was nothing in it to translate.");
        }

        return new ConditionTranslation(string.Join(" && ", clauses), original, true, string.Empty);
    }

    private static bool TryTranslateClause(string clause, out string translated, out string problem)
    {
        translated = string.Empty;
        problem = string.Empty;

        string text = clause;
        bool negated = false;

        // "X == false" and "X != true" are the same as "!X", and both turn up.
        Match equality = BooleanComparison().Match(text);
        if (equality.Success)
        {
            bool wantsTrue = equality.Groups["value"].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
            bool isEquals = equality.Groups["op"].Value == "==";

            negated = wantsTrue != isEquals;
            text = Unwrap(equality.Groups["left"].Value);
        }

        while (text.StartsWith('!') && !text.StartsWith("!=", StringComparison.Ordinal))
        {
            negated = !negated;
            text = Unwrap(text[1..]);
        }

        Match level = LevelComparison().Match(text);
        if (level.Success)
        {
            if (negated)
            {
                problem = $"'{clause}' negates a comparison. Write the opposite comparison instead.";
                return false;
            }

            translated = $"Level {level.Groups["op"].Value} {level.Groups["value"].Value}";
            return true;
        }

        Match itemCount = ItemCountComparison().Match(text);
        if (itemCount.Success)
        {
            if (negated)
            {
                problem = $"'{clause}' negates a comparison. Write the opposite comparison instead.";
                return false;
            }

            translated = $"ItemCount({itemCount.Groups["id"].Value}) "
                + $"{itemCount.Groups["op"].Value} {itemCount.Groups["value"].Value}";
            return true;
        }

        Match call = KnownCall().Match(text);
        if (call.Success)
        {
            string name = call.Groups["name"].Value;
            string id = call.Groups["id"].Value;

            string? term = name.ToUpperInvariant() switch
            {
                "HASQUEST" => "QuestInLog",
                "ISQUESTCOMPLETED" => "QuestCompleted",
                "HASITEM" => null,
                _ => "?",
            };

            if (term == "?")
            {
                problem = Unsupported(clause, name);
                return false;
            }

            if (term is null)
            {
                // HasItem is a count in disguise: carrying one or more.
                translated = negated
                    ? $"ItemCount({id}) == 0"
                    : $"ItemCount({id}) >= 1";
                return true;
            }

            translated = negated ? $"!{term}({id})" : $"{term}({id})";
            return true;
        }

        Match unknownCall = AnyCall().Match(text);
        problem = unknownCall.Success
            ? Unsupported(clause, unknownCall.Groups["name"].Value)
            : $"'{clause}' is not a condition this bot understands. "
                + $"It knows: {string.Join(", ", Enum.GetNames<ConditionTerm>())}.";

        return false;
    }

    private static string Unsupported(string clause, string name) =>
        $"'{clause}' calls {name}, which this bot has no equivalent for. "
        + $"Its conditions are: {string.Join(", ", Enum.GetNames<ConditionTerm>())}.";

    /// <summary>Strips whitespace and any brackets wrapping a whole expression.</summary>
    private static string Unwrap(string text)
    {
        string trimmed = text.Trim();

        while (trimmed.Length > 2 && trimmed[0] == '(' && trimmed[^1] == ')' && IsWholeExpression(trimmed))
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return trimmed;
    }

    /// <summary>True when the outermost brackets wrap everything, not just the first term.</summary>
    private static bool IsWholeExpression(string text)
    {
        int depth = 0;

        for (int index = 0; index < text.Length; index++)
        {
            depth += text[index] switch
            {
                '(' => 1,
                ')' => -1,
                _ => 0,
            };

            if (depth == 0 && index < text.Length - 1)
            {
                return false;
            }
        }

        return depth == 0;
    }

    // Me.Level, Me.Level, Me .Level and the bare Level all turn up.
    [GeneratedRegex(@"^(?:Me\s*\.\s*)?Level\s*(?<op>==|!=|>=|<=|>|<)\s*(?<value>\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex LevelComparison();

    [GeneratedRegex(
        @"^(?:Me\s*\.\s*)?(?:GetItemCount|ItemCount)\s*\(\s*(?<id>\d+)\s*\)\s*(?<op>==|!=|>=|<=|>|<)\s*(?<value>\d+)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex ItemCountComparison();

    [GeneratedRegex(
        @"^(?<name>HasQuest|IsQuestCompleted|HasItem)\s*\(\s*(?<id>\d+)\s*\)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex KnownCall();

    [GeneratedRegex(@"(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s*\(")]
    private static partial Regex AnyCall();

    [GeneratedRegex(
        @"^(?<left>.+?)\s*(?<op>==|!=)\s*(?<value>true|false)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex BooleanComparison();
}
