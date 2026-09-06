using Xunit;

namespace WoWBuddy.Profiles.Tests;

public sealed class ProfileConditionParserTests
{
    [Theory]
    [InlineData("Level >= 12", ConditionTerm.Level, 0u, ComparisonOperator.GreaterOrEqual, 12L)]
    [InlineData("Level>=12", ConditionTerm.Level, 0u, ComparisonOperator.GreaterOrEqual, 12L)]
    [InlineData("level < 5", ConditionTerm.Level, 0u, ComparisonOperator.Less, 5L)]
    [InlineData("Money >= 1000", ConditionTerm.Money, 0u, ComparisonOperator.GreaterOrEqual, 1000L)]
    [InlineData("ItemCount(2589) >= 5", ConditionTerm.ItemCount, 2589u, ComparisonOperator.GreaterOrEqual, 5L)]
    [InlineData("BagsFullPercent > 90", ConditionTerm.BagsFullPercent, 0u, ComparisonOperator.Greater, 90L)]
    public void ParsesComparisons(
        string text,
        ConditionTerm term,
        uint argument,
        ComparisonOperator comparison,
        long value)
    {
        Assert.True(ProfileConditionParser.TryParse(text, out ProfileCondition condition, out string error), error);

        Assert.Equal(term, condition.Term);
        Assert.Equal(argument, condition.Argument);
        Assert.Equal(comparison, condition.Operator);
        Assert.Equal(value, condition.Value);
    }

    [Fact]
    public void ABareQuestTermMeansItIsTrue()
    {
        Assert.True(ProfileConditionParser.TryParse("QuestCompleted(1234)", out ProfileCondition condition, out _));

        Assert.Equal(ConditionTerm.QuestCompleted, condition.Term);
        Assert.Equal(1234u, condition.Argument);
        Assert.Equal(ComparisonOperator.NotEqual, condition.Operator);
        Assert.Equal(0L, condition.Value);
    }

    [Fact]
    public void ANegatedQuestTermMeansItIsFalse()
    {
        Assert.True(ProfileConditionParser.TryParse("!QuestInLog(1234)", out ProfileCondition condition, out _));

        Assert.Equal(ComparisonOperator.Equal, condition.Operator);
        Assert.Equal(0L, condition.Value);

        FakeConditionContext context = new();
        Assert.True(condition.Evaluate(context));

        context.QuestsInLog.Add(1234);
        Assert.False(condition.Evaluate(context));
    }

    [Fact]
    public void NotEqualIsNotReadAsNegation()
    {
        Assert.True(ProfileConditionParser.TryParse("Level != 3", out ProfileCondition condition, out string error), error);

        Assert.Equal(ComparisonOperator.NotEqual, condition.Operator);
        Assert.Equal(3L, condition.Value);
    }

    [Fact]
    public void LessOrEqualIsNotReadAsLessThan()
    {
        Assert.True(ProfileConditionParser.TryParse("Level <= 10", out ProfileCondition condition, out _));

        Assert.Equal(ComparisonOperator.LessOrEqual, condition.Operator);
        Assert.Equal(10L, condition.Value);
    }

    [Theory]
    [InlineData("Reputation >= 100", "does not know")]
    [InlineData("Level", "needs a comparison")]
    [InlineData("Level(5) >= 1", "takes no id")]
    [InlineData("QuestCompleted >= 1", "needs an id in brackets")]
    [InlineData("ItemCount(abc) >= 1", "not an id")]
    [InlineData("Level >= many", "not a whole number")]
    [InlineData("QuestCompleted(1234", "missing a closing bracket")]
    [InlineData("!Level >= 5", "negates a comparison")]
    [InlineData("", "empty")]
    public void RefusesWhatItCannotUnderstand(string text, string expected)
    {
        Assert.False(ProfileConditionParser.TryParse(text, out _, out string error));
        Assert.Contains(expected, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryClauseOfAnAndMustParse()
    {
        Assert.True(
            ProfileConditionParser.TryParseAll(
                "Level >= 5 && !QuestCompleted(1234)",
                out IReadOnlyList<ProfileCondition> conditions,
                out string error),
            error);

        Assert.Equal(2, conditions.Count);

        Assert.False(ProfileConditionParser.TryParseAll("Level >= 5 && Nonsense", out _, out error));
        Assert.Contains("Nonsense", error, StringComparison.Ordinal);
    }

    [Fact]
    public void NoConditionTextMeansNoConditions()
    {
        Assert.True(ProfileConditionParser.TryParseAll("   ", out IReadOnlyList<ProfileCondition> conditions, out _));
        Assert.Empty(conditions);
    }

    [Fact]
    public void EveryTermCanBeEvaluated()
    {
        FakeConditionContext context = new()
        {
            Level = 20,
            Money = 5000,
            BagsFullPercent = 75,
        };

        context.CompletedQuests.Add(1);
        context.QuestsInLog.Add(2);
        context.QuestsReadyToTurnIn.Add(3);
        context.Items[4] = 7;

        // Every term the language offers must reach a real value on the context, or a profile
        // could pass validation and then silently do nothing.
        Assert.True(Evaluate("Level == 20", context));
        Assert.True(Evaluate("Money == 5000", context));
        Assert.True(Evaluate("BagsFullPercent == 75", context));
        Assert.True(Evaluate("QuestCompleted(1)", context));
        Assert.True(Evaluate("QuestInLog(2)", context));
        Assert.True(Evaluate("QuestReadyToTurnIn(3)", context));
        Assert.True(Evaluate("ItemCount(4) == 7", context));

        Assert.Equal(
            Enum.GetValues<ConditionTerm>().Length,
            new[] { "Level", "Money", "BagsFullPercent", "QuestCompleted", "QuestInLog", "QuestReadyToTurnIn", "ItemCount" }.Length);
    }

    [Theory]
    [InlineData("Level == 20", true)]
    [InlineData("Level != 20", false)]
    [InlineData("Level < 20", false)]
    [InlineData("Level <= 20", true)]
    [InlineData("Level > 20", false)]
    [InlineData("Level >= 20", true)]
    public void EveryComparisonWorks(string text, bool expected)
    {
        FakeConditionContext context = new() { Level = 20 };

        Assert.Equal(expected, Evaluate(text, context));
    }

    private static bool Evaluate(string text, IProfileConditionContext context)
    {
        Assert.True(ProfileConditionParser.TryParse(text, out ProfileCondition condition, out string error), error);
        return condition.Evaluate(context);
    }
}
