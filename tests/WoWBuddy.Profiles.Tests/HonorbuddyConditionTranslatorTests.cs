using WoWBuddy.Profiles.Import;
using Xunit;

namespace WoWBuddy.Profiles.Tests;

public sealed class HonorbuddyConditionTranslatorTests
{
    [Theory]
    [InlineData("Me.Level < 10", "Level < 10")]
    [InlineData("Me.Level >= 20", "Level >= 20")]
    [InlineData("Level < 10", "Level < 10")]
    [InlineData("HasQuest(9001)", "QuestInLog(9001)")]
    [InlineData("!HasQuest(9001)", "!QuestInLog(9001)")]
    [InlineData("IsQuestCompleted(9001)", "QuestCompleted(9001)")]
    [InlineData("IsQuestCompleted(9001) == false", "!QuestCompleted(9001)")]
    [InlineData("IsQuestCompleted(9001) != true", "!QuestCompleted(9001)")]
    [InlineData("HasQuest(9001) == true", "QuestInLog(9001)")]
    [InlineData("GetItemCount(8600) >= 5", "ItemCount(8600) >= 5")]
    [InlineData("HasItem(8600)", "ItemCount(8600) >= 1")]
    [InlineData("!HasItem(8600)", "ItemCount(8600) == 0")]
    [InlineData("(Me.Level < 10)", "Level < 10")]
    [InlineData("Me.Level > 5 && !IsQuestCompleted(9001)", "Level > 5 && !QuestCompleted(9001)")]
    public void TranslatesTheFormsThatComeUpConstantly(string honorbuddy, string expected)
    {
        ConditionTranslation translation = HonorbuddyConditionTranslator.Translate(honorbuddy);

        Assert.True(translation.Understood, translation.Problem);
        Assert.Equal(expected, translation.Translated);
    }

    [Fact]
    public void WhatItTranslatesIsAConditionTheLoaderAccepts()
    {
        // The translator emitting text the parser then rejects would be the worst outcome:
        // a conversion that looks clean and produces a profile that will not load.
        foreach (string honorbuddy in (string[])
        [
            "Me.Level < 10",
            "HasQuest(9001)",
            "!HasQuest(9001)",
            "IsQuestCompleted(9001) == false",
            "GetItemCount(8600) >= 5",
            "HasItem(8600)",
            "!HasItem(8600)",
            "Me.Level > 5 && !IsQuestCompleted(9001)",
        ])
        {
            ConditionTranslation translation = HonorbuddyConditionTranslator.Translate(honorbuddy);

            Assert.True(translation.Understood, translation.Problem);
            Assert.True(
                ProfileConditionParser.TryParseAll(translation.Translated, out _, out string error),
                $"'{honorbuddy}' became '{translation.Translated}', which the loader rejects: {error}");
        }
    }

    [Theory]
    [InlineData("Me.Level < 10 || Me.Level > 20", "no 'or'")]
    [InlineData("Me.IsInParty", "not a condition this bot understands")]
    [InlineData("Me.Gold >= 5", "not a condition this bot understands")]
    [InlineData("HasSpell(1234)", "HasSpell")]
    [InlineData("Me.QuestLog.GetQuestById(9001) != null", "GetQuestById")]
    [InlineData("!(Me.Level < 10)", "negates a comparison")]
    public void SaysWhatItCannotTranslateRatherThanGuessing(string honorbuddy, string expected)
    {
        ConditionTranslation translation = HonorbuddyConditionTranslator.Translate(honorbuddy);

        Assert.False(translation.Understood);
        Assert.Empty(translation.Translated);
        Assert.Contains(expected, translation.Problem, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(honorbuddy, translation.Original);
    }

    [Fact]
    public void OneBadClauseLosesTheWholeCondition()
    {
        // Keeping the half it understood would change the meaning: "below 10 and in a party"
        // would become "below 10", which is a different profile.
        ConditionTranslation translation =
            HonorbuddyConditionTranslator.Translate("Me.Level < 10 && Me.IsInParty");

        Assert.False(translation.Understood);
        Assert.Empty(translation.Translated);
    }

    [Fact]
    public void NoConditionIsNotAProblem()
    {
        ConditionTranslation translation = HonorbuddyConditionTranslator.Translate("  ");

        Assert.True(translation.Understood);
        Assert.Empty(translation.Translated);
    }
}
