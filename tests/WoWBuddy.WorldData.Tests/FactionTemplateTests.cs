using WoWBuddy.WorldData.Dbc;
using Xunit;

namespace WoWBuddy.WorldData.Tests;

public sealed class FactionTemplateTests
{
    private const int AllianceGroup = 0x2;
    private const int HordeGroup = 0x4;
    private const int MonsterGroup = 0x8;

    /// <summary>Builds a row in the 3.3.5a layout.</summary>
    private static int[] Row(
        int id,
        int faction,
        int enemyGroup,
        int ownGroup,
        int friendGroup,
        int[]? enemies = null,
        int[]? friends = null)
    {
        enemies ??= [0, 0, 0, 0];
        friends ??= [0, 0, 0, 0];

        return
        [
            id, faction, enemyGroup, ownGroup, friendGroup,
            enemies[0], enemies[1], enemies[2], enemies[3],
            friends[0], friends[1], friends[2], friends[3],
            0,
        ];
    }

    private static FactionTemplates Build(params int[][] rows)
    {
        DbcBuilder builder = new(FactionTemplates.ExpectedFieldCount);

        foreach (int[] row in rows)
        {
            builder.WithRecord(row);
        }

        Assert.True(DbcFile.TryRead(builder.Build(), out DbcFile? dbc, out string error), error);
        Assert.True(FactionTemplates.TryRead(dbc!, out FactionTemplates templates, out error), error);

        return templates;
    }

    [Fact]
    public void AMonsterThatAttacksEveryoneIsHostileToTheCharacter()
    {
        FactionTemplates templates = Build(
            Row(id: 7, faction: 7, enemyGroup: AllianceGroup | HordeGroup, ownGroup: MonsterGroup, friendGroup: MonsterGroup),
            Row(id: 1, faction: 1, enemyGroup: 0, ownGroup: AllianceGroup, friendGroup: AllianceGroup));

        Assert.True(templates.IsHostile(creatureTemplateId: 7, myTemplateId: 1));
    }

    [Fact]
    public void SomethingOnTheCharactersOwnSideIsNot()
    {
        FactionTemplates templates = Build(
            Row(id: 11, faction: 1, enemyGroup: HordeGroup, ownGroup: AllianceGroup, friendGroup: AllianceGroup),
            Row(id: 1, faction: 1, enemyGroup: HordeGroup, ownGroup: AllianceGroup, friendGroup: AllianceGroup));

        Assert.False(templates.IsHostile(11, 1));
    }

    [Fact]
    public void ANamedFriendBeatsAHostileGroupMask()
    {
        // The order that is easy to get backwards, and getting it wrong makes the bot attack
        // things it should not: guards are hostile to the other side in general and friendly to
        // specific neutral factions within it.
        FactionTemplates templates = Build(
            Row(
                id: 7,
                faction: 7,
                enemyGroup: AllianceGroup,
                ownGroup: MonsterGroup,
                friendGroup: 0,
                friends: [1, 0, 0, 0]),
            Row(id: 1, faction: 1, enemyGroup: 0, ownGroup: AllianceGroup, friendGroup: 0));

        Assert.False(templates.IsHostile(7, 1));
    }

    [Fact]
    public void ANamedEnemyMakesSomethingHostileWithoutAnyMask()
    {
        FactionTemplates templates = Build(
            Row(id: 7, faction: 7, enemyGroup: 0, ownGroup: MonsterGroup, friendGroup: 0, enemies: [1, 0, 0, 0]),
            Row(id: 1, faction: 1, enemyGroup: 0, ownGroup: AllianceGroup, friendGroup: 0));

        Assert.True(templates.IsHostile(7, 1));
    }

    [Fact]
    public void AFriendlyGroupMaskBeatsAHostileOne()
    {
        FactionTemplates templates = Build(
            Row(id: 7, faction: 7, enemyGroup: AllianceGroup, ownGroup: MonsterGroup, friendGroup: AllianceGroup),
            Row(id: 1, faction: 1, enemyGroup: 0, ownGroup: AllianceGroup, friendGroup: 0));

        Assert.False(templates.IsHostile(7, 1));
    }

    [Fact]
    public void AZeroFactionIsNotTreatedAsANamedMatch()
    {
        // Empty enemy and friend slots are zero, and every template with no faction would
        // otherwise match all four of them.
        FactionTemplates templates = Build(
            Row(id: 7, faction: 7, enemyGroup: AllianceGroup, ownGroup: MonsterGroup, friendGroup: 0),
            Row(id: 1, faction: 0, enemyGroup: 0, ownGroup: AllianceGroup, friendGroup: 0));

        Assert.True(templates.IsHostile(7, 1));
    }

    [Fact]
    public void SomethingTheFileDoesNotDescribeIsNotAttacked()
    {
        // Not something to attack on the strength of a failed lookup.
        FactionTemplates templates = Build(
            Row(id: 1, faction: 1, enemyGroup: 0, ownGroup: AllianceGroup, friendGroup: 0));

        Assert.False(templates.IsHostile(creatureTemplateId: 999, myTemplateId: 1));
        Assert.False(templates.IsHostile(creatureTemplateId: 1, myTemplateId: 999));
    }

    [Fact]
    public void AFileWithTheWrongNumberOfColumnsIsRefused()
    {
        // Reading the wrong columns would produce a hostility table that looks entirely
        // reasonable and is wrong.
        DbcBuilder builder = new(10);
        builder.WithRecord(1, 2, 3, 4, 5, 6, 7, 8, 9, 10);

        Assert.True(DbcFile.TryRead(builder.Build(), out DbcFile? dbc, out _));

        Assert.False(FactionTemplates.TryRead(dbc!, out FactionTemplates templates, out string error));
        Assert.Contains("3.3.5a one has 14", error, StringComparison.Ordinal);
        Assert.False(templates.IsLoaded);
    }

    [Fact]
    public void TheTemplatesAreIndexedByTheIdACreatureCarries()
    {
        FactionTemplates templates = Build(
            Row(id: 7, faction: 42, enemyGroup: 0, ownGroup: MonsterGroup, friendGroup: 0));

        Assert.Equal(1, templates.Count);
        Assert.True(templates.IsLoaded);
        Assert.Equal(42, templates.Find(7)!.Value.Faction);
        Assert.Null(templates.Find(8));
    }
}
