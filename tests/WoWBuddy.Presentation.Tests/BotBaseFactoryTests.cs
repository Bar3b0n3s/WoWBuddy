using WoWBuddy.Behavior;
using WoWBuddy.BotBases;
using WoWBuddy.BotBases.Group;
using WoWBuddy.BotBases.Support;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Presentation;
using WoWBuddy.Profiles;
using WoWBuddy.WorldData;
using Xunit;

namespace WoWBuddy.Presentation.Tests;

public sealed class BotBaseFactoryTests
{
    private static readonly Vector3 Somewhere = new(1500f, -2500f, 60f);

    private static readonly FishingHooks Hooks = new(
        _ => null,
        _ => true,
        _ => true,
        _ => true);

    private static Profile WithSteps(params ProfileStep[] steps) => new()
    {
        Name = "test",
        Steps = steps,
    };

    [Fact]
    public void BuildsAGrindBaseWithOrWithoutAProfile()
    {
        Assert.True(BotBaseFactory.Create("Grind").Success);

        BotBaseBuild built = BotBaseFactory.Create(
            "Grind",
            WithSteps(new ProfileStep(StepKind.Grind, Position: Somewhere)));

        Assert.True(built.Success);
        Assert.Contains("1 spot", built.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesToGatherWithoutAProfile()
    {
        // Starting and quietly achieving nothing for an hour is worse than not starting.
        BotBaseBuild built = BotBaseFactory.Create("Gather");

        Assert.False(built.Success);
        Assert.Contains("needs a profile", built.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesToGatherAProfileThatNamesNoNodes()
    {
        // Node ids differ between servers, so nothing is assumed — and the message says how to
        // find them rather than leaving the user to guess.
        BotBaseBuild built = BotBaseFactory.Create(
            "Gather",
            WithSteps(new ProfileStep(StepKind.RunTo, Position: Somewhere)));

        Assert.False(built.Success);
        Assert.Contains("names no nodes", built.Message, StringComparison.Ordinal);
        Assert.Contains("inspector", built.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GathersTheNodesAProfileNames()
    {
        BotBaseBuild built = BotBaseFactory.Create(
            "Gather",
            WithSteps(
                new ProfileStep(StepKind.Objective, Entry: 1617, Position: Somewhere),
                new ProfileStep(StepKind.Objective, Entry: 1618, Position: Somewhere)));

        Assert.True(built.Success);
        Assert.Contains("2 node type", built.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesToFishBeforeAttaching()
    {
        // Fishing is the one base that cannot be built from settings alone: it reads the
        // bobber's state byte out of the client.
        BotBaseBuild built = BotBaseFactory.Create("Fish");

        Assert.False(built.Success);
        Assert.Contains("attaching first", built.Message, StringComparison.Ordinal);

        Assert.True(BotBaseFactory.Create("Fish", fishing: Hooks).Success);
    }

    [Fact]
    public void RefusesToQuestWithoutAProfile()
    {
        BotBaseBuild built = BotBaseFactory.Create("Questing");

        Assert.False(built.Success);
        Assert.Contains("needs a profile", built.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AQuestingBaseFallsBackToGrindingTheSameProfile()
    {
        // Running out of quests is what success looks like for a levelling profile.
        BotBaseBuild built = BotBaseFactory.Create(
            "Questing",
            WithSteps(new ProfileStep(StepKind.RunTo, Position: Somewhere)));

        Assert.True(built.Success);
        Assert.Contains("step(s)", built.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesADungeonWithoutARole()
    {
        // Guessing wrong means tanking in cloth.
        BotBaseBuild built = BotBaseFactory.Create("Dungeon");

        Assert.False(built.Success);
        Assert.Contains("Choose a role", built.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATankWithoutARouteIsToldItWillFollow()
    {
        BotBaseBuild built = BotBaseFactory.Create("Dungeon", role: PartyRole.Tank);

        Assert.True(built.Success);
        Assert.Contains("follow instead of leading", built.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADamageDealerNeedsNothingButARole()
    {
        BotBaseBuild built = BotBaseFactory.Create("Dungeon", role: PartyRole.Damage);

        Assert.True(built.Success);
        Assert.Contains("Damage", built.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesABattlegroundWithoutAPlan()
    {
        BotBaseBuild built = BotBaseFactory.Create("Battleground");

        Assert.False(built.Success);
        Assert.Contains("does not ship", built.Message, StringComparison.Ordinal);

        BotBaseBuild empty = BotBaseFactory.Create("Battleground", WithSteps(
            new ProfileStep(StepKind.TurnIn, QuestId: 1, Entry: 8100)));

        Assert.False(empty.Success);
        Assert.Contains("nowhere to go", empty.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildsABattlegroundFromAProfilesPlaces()
    {
        BotBaseBuild built = BotBaseFactory.Create(
            "Battleground",
            WithSteps(new ProfileStep(StepKind.RunTo, Position: Somewhere)));

        Assert.True(built.Success);
        Assert.Contains("1 post", built.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SaysSoWhenItDoesNotKnowTheBotBase()
    {
        BotBaseBuild built = BotBaseFactory.Create("Archaeology");

        Assert.False(built.Success);
        Assert.Contains("Archaeology", built.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesToCraftBeforeAttaching()
    {
        // The recipe list is the character's own spellbook, which needs a client.
        BotBaseBuild built = BotBaseFactory.Create("Craft");

        Assert.False(built.Success);
        Assert.Contains("enabling execution first", built.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesToCraftWithoutAProfession()
    {
        BotBaseBuild built = BotBaseFactory.Create("Craft", tradeSkills: new StubTradeSkills());

        Assert.False(built.Success);
        Assert.Contains("Name a profession", built.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildsACraftingBaseWhenItHasBoth()
    {
        BotBaseBuild built = BotBaseFactory.Create(
            "Craft",
            tradeSkills: new StubTradeSkills(),
            crafting: new CraftSettings { Profession = "Cooking" });

        Assert.True(built.Success);
        Assert.Contains("Cooking", built.Message, StringComparison.Ordinal);
    }


    // ---- world data ------------------------------------------------------------------------

    /// <summary>An export with one vendor selling flux and one quest wanting kobolds dead.</summary>
    private static WorldDataSet Exported()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "wowbuddy-factory", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        void Write(string file, params string[] lines) =>
            File.WriteAllText(Path.Combine(directory, file), string.Join('\n', lines));

        Write(WorldDataSet.FileNames.CreatureSpawns,
            "guid\tentry\tmap\tx\ty\tz",
            "10\t555\t0\t-8890\t500\t90");

        Write(WorldDataSet.FileNames.CreatureTemplates,
            "entry\tname\tnpcflag",
            "555\tGeneral Goods\t128");

        Write(WorldDataSet.FileNames.VendorItems, "entry\titem", "555\t2880");

        Write(WorldDataSet.FileNames.QuestTemplates,
            "id\ttitle\tminlevel\tquestlevel\tnpc1\tnpccount1\tnpc2\tnpccount2\tnpc3\tnpccount3\tnpc4\tnpccount4\titem1\titemcount1\titem2\titemcount2\titem3\titemcount3\titem4\titemcount4\titem5\titemcount5\titem6\titemcount6",
            "9001\tKobold Camp Cleanup\t5\t6\t1412\t8\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0");

        WorldDataSet set = new();
        set.LoadFrom(directory);
        return set;
    }

    [Fact]
    public void WithoutWorldDataTheCraftingBaseSaysItCannotBuyMore()
    {
        // A bot that appears to run and quietly achieves nothing is worse than one that says
        // what it cannot do.
        BotBaseBuild built = BotBaseFactory.Create(
            "Craft",
            tradeSkills: new StubTradeSkills(),
            crafting: new CraftSettings { Profession = "Blacksmithing" });

        Assert.True(built.Success);
        Assert.Contains("cannot buy more", built.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithWorldDataTheCraftingBaseCanRestock()
    {
        BotBaseBuild built = BotBaseFactory.Create(
            "Craft",
            tradeSkills: new StubTradeSkills(),
            crafting: new CraftSettings { Profession = "Blacksmithing" },
            world: Exported());

        Assert.True(built.Success);
        Assert.Contains("buying materials", built.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutQuestDataTheQuestingBaseSaysItWorksFromTheProfile()
    {
        Profile profile = WithSteps(
            new ProfileStep(StepKind.Objective, QuestId: 9001, Position: Somewhere));

        BotBaseBuild built = BotBaseFactory.Create("Questing", profile);

        Assert.True(built.Success);
        Assert.Contains("No quest data", built.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithQuestDataTheQuestingBaseSaysNothingExtra()
    {
        Profile profile = WithSteps(
            new ProfileStep(StepKind.Objective, QuestId: 9001, Position: Somewhere));

        BotBaseBuild built = BotBaseFactory.Create("Questing", profile, world: Exported());

        Assert.True(built.Success);
        Assert.DoesNotContain("No quest data", built.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryBotBaseTheWindowOffersCanBeBuilt()
    {
        // A window offering a choice the factory does not recognise is a dead menu entry.
        Profile profile = WithSteps(
            new ProfileStep(StepKind.Objective, Entry: 1617, Position: Somewhere));

        foreach (BotBaseOption option in BotBaseOption.All)
        {
            BotBaseBuild built = BotBaseFactory.Create(
                option.Name,
                profile,
                role: PartyRole.Damage,
                fishing: Hooks,
                tradeSkills: new StubTradeSkills(),
                crafting: new CraftSettings { Profession = "Cooking" });

            Assert.True(built.Success, $"{option.Name} could not be built: {built.Message}");
            Assert.NotNull(built.Tree);
        }
    }
}

/// <summary>A profession window with nothing in it.</summary>
internal sealed class StubTradeSkills : ITradeSkills
{
    public TradeSkillLine Line => default;

    public IReadOnlyList<TradeSkillRecipe> Recipes => [];

    public bool Open(string profession) => true;

    public TradeSkillRecipe? BestForSkillUp() => null;

    public IReadOnlyList<TradeSkillReagent> ReagentsFor(TradeSkillRecipe recipe) => [];

    public int Craft(TradeSkillRecipe recipe, int count = 1) => 0;

    public bool Close() => true;
}
