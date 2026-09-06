using WoWBuddy.Behavior;
using WoWBuddy.BotBases;
using WoWBuddy.BotBases.Support;
using Xunit;

namespace WoWBuddy.BotBases.Tests;

/// <summary>A profession window driven by hand.</summary>
public sealed class FakeTradeSkills : ITradeSkills
{
    public TradeSkillLine Line { get; set; }

    public List<TradeSkillRecipe> Known { get; } = [];

    public IReadOnlyList<TradeSkillRecipe> Recipes => Known;

    public List<string> Actions { get; } = [];

    /// <summary>Whether opening the window works.</summary>
    public bool CanOpen { get; set; } = true;

    public bool Open(string profession)
    {
        Actions.Add($"Open({profession})");

        if (CanOpen)
        {
            Line = new TradeSkillLine(profession, 100, 150);
        }

        return CanOpen;
    }

    public TradeSkillRecipe? BestForSkillUp()
    {
        TradeSkillRecipe? best = null;

        foreach (TradeSkillRecipe recipe in Known)
        {
            if (recipe is { CanMake: true, RaisesSkill: true }
                && (best is null || recipe.Difficulty < best.Value.Difficulty))
            {
                best = recipe;
            }
        }

        return best;
    }

    public int Craft(TradeSkillRecipe recipe, int count = 1)
    {
        int made = Math.Min(count, recipe.Available);
        Actions.Add($"Craft({recipe.Name}, {made})");
        return made;
    }

    public bool Close()
    {
        Actions.Add("Close");
        return true;
    }

    /// <summary>Teaches the character a recipe.</summary>
    public FakeTradeSkills With(string name, RecipeDifficulty difficulty, int available)
    {
        Known.Add(new TradeSkillRecipe(Known.Count + 1, name, difficulty, available));
        return this;
    }
}

public sealed class CraftBotBaseTests
{
    private static CraftSettings Settings(string recipe = "", int batch = 5) => new()
    {
        Profession = "Cooking",
        Recipe = recipe,
        BatchSize = batch,
    };

    [Fact]
    public void OpensTheProfessionBeforeAnythingElse()
    {
        FakeTradeSkills skills = new();
        CraftBotBase bot = new(Settings());

        Assert.Equal(RunStatus.Running, bot.Build(skills).Tick(new FakeBotState()));
        Assert.Contains("Open(Cooking)", skills.Actions);
    }

    [Fact]
    public void StopsWhenTheWindowWillNotOpen()
    {
        // Usually a wrong profession name, and there is no list here to check it against.
        FakeTradeSkills skills = new() { CanOpen = false };
        CraftBotBase bot = new(Settings());

        Assert.Equal(RunStatus.Failure, bot.Build(skills).Tick(new FakeBotState()));
        Assert.Equal(CraftStop.NoWindow, bot.Stopped);
    }

    [Fact]
    public void SaysSoWhenNoProfessionWasNamed()
    {
        CraftBotBase bot = new(new CraftSettings());

        Assert.Equal(RunStatus.Failure, bot.Build(new FakeTradeSkills()).Tick(new FakeBotState()));
        Assert.Equal(CraftStop.NotConfigured, bot.Stopped);
    }

    [Fact]
    public void MakesWhateverRaisesTheSkillFastest()
    {
        FakeTradeSkills skills = new();
        skills.Line = new TradeSkillLine("Cooking", 100, 150);
        skills.With("Trivial Thing", RecipeDifficulty.Trivial, available: 99)
            .With("Good Thing", RecipeDifficulty.Optimal, available: 4)
            .With("Fine Thing", RecipeDifficulty.Medium, available: 20);

        CraftBotBase bot = new(Settings(batch: 5));

        bot.Build(skills).Tick(new FakeBotState());

        // Orange first, and only as many as the materials allow.
        Assert.Contains("Craft(Good Thing, 4)", skills.Actions);
        Assert.Equal(4, bot.Crafted);
    }

    [Fact]
    public void MakesTheNamedRecipeWhenThereIsOne()
    {
        FakeTradeSkills skills = new();
        skills.Line = new TradeSkillLine("Cooking", 100, 150);
        skills.With("Good Thing", RecipeDifficulty.Optimal, available: 10)
            .With("Spiced Wolf Meat", RecipeDifficulty.Easy, available: 10);

        CraftBotBase bot = new(Settings(recipe: "Spiced Wolf Meat", batch: 3));

        bot.Build(skills).Tick(new FakeBotState());

        Assert.Contains("Craft(Spiced Wolf Meat, 3)", skills.Actions);
    }

    [Fact]
    public void SaysSoWhenTheCharacterDoesNotKnowTheNamedRecipe()
    {
        FakeTradeSkills skills = new();
        skills.Line = new TradeSkillLine("Cooking", 100, 150);
        skills.With("Something Else", RecipeDifficulty.Optimal, available: 10);

        CraftBotBase bot = new(Settings(recipe: "Dragonbreath Chili"));

        Assert.Equal(RunStatus.Failure, bot.Build(skills).Tick(new FakeBotState()));
        Assert.Equal(CraftStop.UnknownRecipe, bot.Stopped);
    }

    [Fact]
    public void StopsWhenTheMaterialsRunOut()
    {
        // It does not buy any: that would take item data this project does not ship.
        FakeTradeSkills skills = new();
        skills.Line = new TradeSkillLine("Cooking", 100, 150);
        skills.With("Good Thing", RecipeDifficulty.Optimal, available: 0);

        CraftBotBase bot = new(Settings());

        Assert.Equal(RunStatus.Failure, bot.Build(skills).Tick(new FakeBotState()));
        Assert.Equal(CraftStop.OutOfMaterials, bot.Stopped);
    }

    [Fact]
    public void StopsAtTheTrainingCapRatherThanBurningMaterialsForNothing()
    {
        FakeTradeSkills skills = new();
        skills.Line = new TradeSkillLine("Cooking", 150, 150);
        skills.With("Good Thing", RecipeDifficulty.Optimal, available: 99);

        CraftBotBase bot = new(Settings());

        Assert.Equal(RunStatus.Failure, bot.Build(skills).Tick(new FakeBotState()));
        Assert.Equal(CraftStop.Capped, bot.Stopped);
        Assert.Empty(skills.Actions);
    }

    [Fact]
    public void ACapCanBeIgnoredWhenTheUserWantsTheItemsRatherThanTheSkill()
    {
        FakeTradeSkills skills = new();
        skills.Line = new TradeSkillLine("Cooking", 150, 150);
        skills.With("Good Thing", RecipeDifficulty.Trivial, available: 99);

        CraftBotBase bot = new(new CraftSettings
        {
            Profession = "Cooking",
            Recipe = "Good Thing",
            StopAtCap = false,
        });

        Assert.Equal(RunStatus.Running, bot.Build(skills).Tick(new FakeBotState()));
        Assert.Contains(skills.Actions, a => a.StartsWith("Craft(Good Thing", StringComparison.Ordinal));
    }

    [Fact]
    public void NothingIsQueuedOnTopOfACastInProgress()
    {
        FakeTradeSkills skills = new();
        skills.Line = new TradeSkillLine("Cooking", 100, 150);
        skills.With("Good Thing", RecipeDifficulty.Optimal, available: 99);

        FakeBotState state = new();
        state.CombatContext.IsCasting = true;

        CraftBotBase bot = new(Settings());

        Assert.Equal(RunStatus.Running, bot.Build(skills).Tick(state));
        Assert.Empty(skills.Actions);
    }

    [Fact]
    public void TheBestRecipeIsChosenFreshEachBatchBecauseTheSkillGoesUp()
    {
        FakeTradeSkills skills = new();
        skills.Line = new TradeSkillLine("Cooking", 100, 150);
        skills.With("Orange", RecipeDifficulty.Optimal, available: 2)
            .With("Yellow", RecipeDifficulty.Medium, available: 99);

        CraftBotBase bot = new(Settings(batch: 2));
        FakeBotState state = new();

        bot.Build(skills).Tick(state);

        // The orange one is used up; the next batch moves down a colour.
        skills.Known[0] = skills.Known[0] with { Available = 0 };

        bot.Build(skills).Tick(state);

        Assert.Contains("Craft(Orange, 2)", skills.Actions);
        Assert.Contains("Craft(Yellow, 2)", skills.Actions);
    }

    [Fact]
    public void OnceStoppedItStaysStoppedUntilReset()
    {
        // A crafting session that quietly restarts itself would burn a night's materials.
        FakeTradeSkills skills = new() { CanOpen = false };
        CraftBotBase bot = new(Settings());
        FakeBotState state = new();

        bot.Build(skills).Tick(state);
        bot.Build(skills).Tick(state);
        bot.Build(skills).Tick(state);

        Assert.Single(skills.Actions);

        bot.Reset();
        Assert.Equal(CraftStop.None, bot.Stopped);
    }
}
