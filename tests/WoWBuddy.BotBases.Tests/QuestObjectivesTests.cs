using WoWBuddy.BotBases.Questing;
using WoWBuddy.Profiles;
using WoWBuddy.WorldData;
using Xunit;

namespace WoWBuddy.BotBases.Tests;

public sealed class QuestObjectivesTests
{
    private const uint KoboldVermin = 1412;
    private const uint KoboldLaborer = 1395;
    private const uint MalachiteId = 774;
    private const uint LinenId = 2589;

    /// <summary>A quest wanting two kinds of kobold dead.</summary>
    private static QuestTemplate TwoKills => new(
        1234,
        "Kobold Camp Cleanup",
        MinLevel: 5,
        QuestLevel: 6,
        [
            new QuestRequirement(KoboldVermin, 8, IsItem: false),
            new QuestRequirement(KoboldLaborer, 4, IsItem: false),
        ]);

    /// <summary>A quest wanting one thing killed and one thing brought.</summary>
    private static QuestTemplate OneOfEach => new(
        5678,
        "Ore and Ire",
        MinLevel: 8,
        QuestLevel: 10,
        [
            new QuestRequirement(KoboldVermin, 6, IsItem: false),
            new QuestRequirement(MalachiteId, 3, IsItem: true),
        ]);

    private static QuestObjectives Knowing(params QuestTemplate[] quests)
    {
        Dictionary<uint, QuestTemplate> table = quests.ToDictionary(quest => quest.Id);

        return new QuestObjectives(id =>
            table.TryGetValue(id, out QuestTemplate quest) ? quest : null);
    }

    [Fact]
    public void WithNoExportItKnowsNothingAndSaysSo()
    {
        // Everything degrades to what the profile named, which is what happened before there
        // was any world data to read.
        ProfileStep step = new(StepKind.Objective, QuestId: 1234, Objective: ObjectiveKind.Kill);

        Assert.False(QuestObjectives.None.HasData);
        Assert.Empty(QuestObjectives.None.KillsFor(step));
        Assert.Null(QuestObjectives.None.CollectionFor(step));
        Assert.Equal(string.Empty, QuestObjectives.None.Describe(step));
    }

    [Fact]
    public void TheProfileWinsWhenItNamesACreature()
    {
        // A profile was written by someone looking at the quest. A quest that wants "any beast
        // in the valley" is written as an entry the database cannot derive.
        ProfileStep step = new(
            StepKind.Objective, QuestId: 1234, Entry: 9999, Objective: ObjectiveKind.Kill);

        Assert.Equal([9999u], Knowing(TwoKills).KillsFor(step));
    }

    [Fact]
    public void WithNoEntryItUsesEveryCreatureTheQuestAsksFor()
    {
        // The whole point: an objective step that would otherwise kill whatever walked past.
        ProfileStep step = new(StepKind.Objective, QuestId: 1234, Objective: ObjectiveKind.Kill);

        IReadOnlySet<uint> wanted = Knowing(TwoKills).KillsFor(step);

        Assert.Equal(2, wanted.Count);
        Assert.Contains(KoboldVermin, wanted);
        Assert.Contains(KoboldLaborer, wanted);
    }

    [Fact]
    public void AnObjectiveIndexNarrowsItToOne()
    {
        ProfileStep step = new(
            StepKind.Objective,
            QuestId: 1234,
            Objective: ObjectiveKind.Kill,
            ObjectiveIndex: 2);

        Assert.Equal([KoboldLaborer], Knowing(TwoKills).KillsFor(step));
    }

    [Fact]
    public void AnIndexPastTheEndFallsBackToAllOfThem()
    {
        // Being wrong about the client's objective order costs precision, not correctness.
        ProfileStep step = new(
            StepKind.Objective,
            QuestId: 1234,
            Objective: ObjectiveKind.Kill,
            ObjectiveIndex: 9);

        Assert.Equal(2, Knowing(TwoKills).KillsFor(step).Count);
    }

    [Fact]
    public void AnIndexPointingAtAnItemAsksForNoKills()
    {
        // Objective two of this quest is a collection, so there is nothing to fight for it.
        ProfileStep step = new(
            StepKind.Objective,
            QuestId: 5678,
            Objective: ObjectiveKind.Collect,
            ObjectiveIndex: 2);

        Assert.Empty(Knowing(OneOfEach).KillsFor(step));
        Assert.Equal(MalachiteId, Knowing(OneOfEach).CollectionFor(step)?.Entry);
        Assert.Equal(3, Knowing(OneOfEach).CountFor(step));
    }

    [Fact]
    public void AQuestWantingOneThingFillsInTheItemAndCount()
    {
        QuestTemplate collect = new(
            42, "Linen for the Guard", 5, 6, [new QuestRequirement(LinenId, 10, IsItem: true)]);

        ProfileStep step = new(StepKind.Objective, QuestId: 42, Objective: ObjectiveKind.Collect);

        QuestRequirement requirement = Assert.NotNull(Knowing(collect).CollectionFor(step));

        Assert.Equal(LinenId, requirement.Entry);
        Assert.Equal(10, requirement.Count);
    }

    [Fact]
    public void AQuestWantingSeveralThingsIsNotGuessedAt()
    {
        // Sending the character after the wrong one of three collections means the step never
        // finishes, which is worse than not knowing.
        QuestTemplate several = new(
            43,
            "Everything",
            5,
            6,
            [
                new QuestRequirement(LinenId, 10, IsItem: true),
                new QuestRequirement(MalachiteId, 4, IsItem: true),
            ]);

        ProfileStep step = new(StepKind.Objective, QuestId: 43, Objective: ObjectiveKind.Collect);

        Assert.Null(Knowing(several).CollectionFor(step));
    }

    [Fact]
    public void AProfileThatNamesItsOwnItemKeepsIt()
    {
        // The profile may be describing one stage of a quest that wants several things, and the
        // database has no way to know which stage the author meant.
        ProfileStep step = new(
            StepKind.Objective,
            QuestId: 5678,
            Objective: ObjectiveKind.Collect,
            ItemId: LinenId,
            Count: 2);

        QuestRequirement requirement = Assert.NotNull(Knowing(OneOfEach).CollectionFor(step));

        Assert.Equal(LinenId, requirement.Entry);
        Assert.Equal(2, requirement.Count);
    }

    [Fact]
    public void AQuestTheExportDoesNotHaveAnswersNothing()
    {
        ProfileStep step = new(StepKind.Objective, QuestId: 9999, Objective: ObjectiveKind.Kill);

        Assert.Empty(Knowing(TwoKills).KillsFor(step));
        Assert.Null(Knowing(TwoKills).For(9999));
    }
}
