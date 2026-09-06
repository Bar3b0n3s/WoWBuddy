using WoWBuddy.BotBases.Support;
using WoWBuddy.WorldData;
using Xunit;

namespace WoWBuddy.BotBases.Tests;

public sealed class TargetFilterTests
{
    private const uint Boar = 100;
    private const uint Elite = 200;
    private const uint Boss = 300;
    private const uint Rare = 400;

    private static TargetFilter Build(TargetFilterSettings? settings = null)
    {
        Dictionary<uint, CreatureTemplate> world = new()
        {
            [Boar] = new CreatureTemplate(Boar, "Mangy Boar", 0, MinLevel: 8, MaxLevel: 9),
            [Elite] = new CreatureTemplate(Elite, "Hogger", 0, MinLevel: 11, MaxLevel: 11,
                Rank: CreatureTemplate.EliteRank),
            [Boss] = new CreatureTemplate(Boss, "Onyxia", 0, MinLevel: 63, MaxLevel: 63,
                Rank: CreatureTemplate.BossRank),
            [Rare] = new CreatureTemplate(Rare, "Rare Boar", 0, MinLevel: 9, MaxLevel: 9,
                Rank: CreatureTemplate.RareRank),
        };

        return new TargetFilter(
            settings,
            entry => world.TryGetValue(entry, out CreatureTemplate template) ? template : null);
    }

    [Fact]
    public void AnOrdinaryMobOfTheRightLevelIsFineToFight()
    {
        Assert.False(Build().ShouldAvoid(Boar, targetLevel: 9, myLevel: 10));
    }

    [Fact]
    public void AnEliteIsLeftAlone()
    {
        // This is the thing world data buys that nothing else can: whether a creature is an
        // elite is not in its descriptors, so without the export the bot finds out by dying.
        TargetFilter filter = Build();

        Assert.True(filter.ShouldAvoid(Elite, targetLevel: 11, myLevel: 12));
        Assert.Contains("elite", filter.Reject(Elite, 11, 12), StringComparison.Ordinal);
    }

    [Fact]
    public void ABossIsLeftAloneWhateverTheSettingsSay()
    {
        // Not a thing a grinding character recovers from mistaking for a mob.
        TargetFilter filter = Build(new TargetFilterSettings { AvoidElites = false });

        Assert.True(filter.ShouldAvoid(Boss, targetLevel: 63, myLevel: 80));
        Assert.Contains("boss", filter.Reject(Boss, 63, 80), StringComparison.Ordinal);
    }

    [Fact]
    public void ARareThatIsNotEliteIsStillWorthFighting()
    {
        Assert.False(Build().ShouldAvoid(Rare, targetLevel: 9, myLevel: 10));
    }

    [Fact]
    public void ElitesCanBeAllowedForACharacterThatCanHandleThem()
    {
        TargetFilter filter = Build(new TargetFilterSettings { AvoidElites = false });

        Assert.False(filter.ShouldAvoid(Elite, targetLevel: 11, myLevel: 40));
    }

    [Fact]
    public void AnythingFarAboveTheCharacterIsLeftAlone()
    {
        Assert.True(Build().ShouldAvoid(Boar, targetLevel: 9, myLevel: 4));
        Assert.False(Build().ShouldAvoid(Boar, targetLevel: 9, myLevel: 6));
    }

    [Fact]
    public void TheExportsLevelRangeBeatsTheOneUnitInFrontOfTheCharacter()
    {
        // A spawn can roll anywhere within its range, so the top of the range is what matters:
        // the level-eight boar standing here has a level-nine sibling behind it.
        TargetFilter filter = Build(new TargetFilterSettings { LevelsAboveToAllow = 0 });

        Assert.True(filter.ShouldAvoid(Boar, targetLevel: 8, myLevel: 8));
    }

    [Fact]
    public void WithoutWorldDataNothingIsRejectedOnItsRank()
    {
        // A filter that refused everything it could not look up would stop a bot that had been
        // working perfectly well, for the want of an export.
        TargetFilter filter = new();

        Assert.False(filter.HasData);
        Assert.False(filter.ShouldAvoid(Elite, targetLevel: 11, myLevel: 12));
    }

    [Fact]
    public void TheLevelCheckStillWorksWithoutWorldData()
    {
        // It comes from the descriptors rather than the export.
        TargetFilter filter = new();

        Assert.True(filter.ShouldAvoid(Elite, targetLevel: 40, myLevel: 10));
    }

    [Fact]
    public void ACreatureMissingFromTheExportIsJudgedOnWhatTheClientSays()
    {
        TargetFilter filter = Build();

        Assert.False(filter.ShouldAvoid(9999, targetLevel: 9, myLevel: 10));
        Assert.True(filter.ShouldAvoid(9999, targetLevel: 40, myLevel: 10));
    }

    [Fact]
    public void AnUnknownLevelIsNotTreatedAsDangerous()
    {
        // Zero means the client did not say, not that the creature is level zero.
        Assert.False(Build().ShouldAvoid(9999, targetLevel: 0, myLevel: 10));
        Assert.False(Build().ShouldAvoid(9999, targetLevel: 40, myLevel: 0));
    }

    [Fact]
    public void TheGrindBaseLeavesElitesAlone()
    {
        FakeBotState state = new() { Level = 12 };
        state.AddEnemy(1, distance: 10f, entry: Elite, level: 11);
        CandidateTarget boar = state.AddEnemy(2, distance: 20f, entry: Boar, level: 9);

        GrindBotBase grind = new(new GrindSettings
        {
            MinimumLevel = 1,
            MaximumLevel = 80,
            Filter = Build(),
        });

        Assert.Equal(boar.Guid, grind.SelectTarget(state)!.Value.Guid);
    }
}
