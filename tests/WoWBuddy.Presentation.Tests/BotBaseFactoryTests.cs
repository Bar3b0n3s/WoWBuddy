using WoWBuddy.Behavior;
using WoWBuddy.BotBases;
using WoWBuddy.BotBases.Group;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Presentation;
using WoWBuddy.Profiles;
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
    public void EveryBotBaseTheWindowOffersCanBeBuilt()
    {
        // A window offering a choice the factory does not recognise is a dead menu entry.
        Profile profile = WithSteps(
            new ProfileStep(StepKind.Objective, Entry: 1617, Position: Somewhere));

        foreach (BotBaseOption option in BotBaseOption.All)
        {
            BotBaseBuild built = BotBaseFactory.Create(
                option.Name, profile, role: PartyRole.Damage, fishing: Hooks);

            Assert.True(built.Success, $"{option.Name} could not be built: {built.Message}");
            Assert.NotNull(built.Tree);
        }
    }
}
