using WoWBuddy.Behavior;
using WoWBuddy.BotBases;
using WoWBuddy.BotBases.Support;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Objects;
using WoWBuddy.Profiles;
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

    /// <summary>What each recipe is made from, by recipe name.</summary>
    public Dictionary<string, List<TradeSkillReagent>> Reagents { get; } = [];

    public IReadOnlyList<TradeSkillReagent> ReagentsFor(TradeSkillRecipe recipe) =>
        Reagents.TryGetValue(recipe.Name, out List<TradeSkillReagent>? reagents) ? reagents : [];

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

    /// <summary>Says what a recipe is made from.</summary>
    public FakeTradeSkills MadeFrom(string recipe, uint itemId, string name, int needed, int have)
    {
        if (!Reagents.TryGetValue(recipe, out List<TradeSkillReagent>? reagents))
        {
            reagents = [];
            Reagents[recipe] = reagents;
        }

        reagents.Add(new TradeSkillReagent(itemId, name, needed, have));
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

    // ---- shopping -------------------------------------------------------------------------

    private const uint FluxId = 2880;

    private static readonly Vector3 Anvil = new(100f, 100f, 10f);
    private static readonly Vector3 Shop = new(120f, 100f, 10f);

    /// <summary>A blacksmith with a recipe it has no materials for.</summary>
    private static FakeTradeSkills OutOfFlux()
    {
        FakeTradeSkills skills = new();
        skills.Line = new TradeSkillLine("Blacksmithing", 50, 150);
        skills.With("Rough Grinding Stone", RecipeDifficulty.Optimal, available: 0);
        skills.MadeFrom("Rough Grinding Stone", FluxId, "Weak Flux", needed: 2, have: 0);
        return skills;
    }

    private static FakeBotState AtTheAnvil()
    {
        FakeBotState state = new() { Position = Anvil };

        state.VisibleObjects =
        [
            new VisibleObject(new WoWGuid(0x40), 1234, Shop, HasPosition: true, Distance: 1f),
        ];

        state.VendorState.Selling(FluxId, "Weak Flux", price: 10);
        return state;
    }

    private static SupplyRun ShopAt(Vector3 where) =>
        new(new SupplySettings
        {
            Batches = 1,
            FindVendor = (_, _, _) => new ProfileVendor("Smith Argus", 1234, 0, where),
        });

    [Fact]
    public void WithNowhereToShopItStopsExactlyAsItAlwaysDid()
    {
        // The behaviour before any of the buying existed, and still the right one when the user
        // has no world data: stop, with a reason, rather than pretend.
        CraftBotBase craft = new(Settings());
        Node<IBotState> tree = craft.Build(OutOfFlux());

        Assert.Equal(RunStatus.Failure, tree.Tick(AtTheAnvil()));
        Assert.Equal(CraftStop.OutOfMaterials, craft.Stopped);
    }

    [Fact]
    public void RunningOutOfMaterialsSendsItShoppingInsteadOfStopping()
    {
        CraftBotBase craft = new(Settings(), ShopAt(Shop));
        Node<IBotState> tree = craft.Build(OutOfFlux());
        FakeBotState state = AtTheAnvil();

        Assert.Equal(RunStatus.Running, tree.Tick(state));
        Assert.Equal(CraftStop.None, craft.Stopped);
        Assert.Equal(1, craft.SupplyRuns);

        // On its way on the next tick, rather than standing at the anvil casting the profession
        // at nothing.
        Assert.Equal(RunStatus.Running, tree.Tick(state));
        Assert.Contains("MoveTo", state.Actions);
    }

    [Fact]
    public void WhileShoppingItDoesNotTryToReopenTheProfession()
    {
        // The window closes as soon as the character walks away, and casting the profession
        // again would cancel the journey every tick.
        FakeTradeSkills skills = OutOfFlux();
        CraftBotBase craft = new(Settings(), ShopAt(Shop));
        Node<IBotState> tree = craft.Build(skills);
        FakeBotState state = AtTheAnvil();

        tree.Tick(state);

        skills.Line = default;
        skills.Actions.Clear();

        tree.Tick(state);

        Assert.DoesNotContain(
            skills.Actions,
            action => action.StartsWith("Open(", StringComparison.Ordinal));
    }

    [Fact]
    public void ItComesBackAndCarriesOn()
    {
        FakeTradeSkills skills = OutOfFlux();
        CraftBotBase craft = new(Settings(), ShopAt(Shop));
        Node<IBotState> tree = craft.Build(skills);
        FakeBotState state = AtTheAnvil();

        for (int tick = 0; tick < 20 && craft.Stopped == CraftStop.None; tick++)
        {
            tree.Tick(state);

            if (state.MoveRequests.Count > 0)
            {
                state.Position = state.MoveRequests[^1];
                state.MoveRequests.Clear();
            }

            if (state.Actions.Exists(a => a.StartsWith("Interact(", StringComparison.Ordinal)))
            {
                state.VendorState.IsMerchantOpen = true;
            }

            // What the client would do once the flux is in the bags.
            if (state.VendorState.Actions.Exists(a => a.StartsWith("Buy(", StringComparison.Ordinal)))
            {
                skills.Known[0] = skills.Known[0] with { Available = 2 };
            }
        }

        Assert.Equal(CraftStop.None, craft.Stopped);
        Assert.Equal(Anvil, state.Position);
        Assert.Contains(skills.Actions, action => action.StartsWith("Craft(", StringComparison.Ordinal));
    }

    [Fact]
    public void ATripThatDoesNotFixItStopsRatherThanWalkingBackAndForthAllEvening()
    {
        // A trip that comes back and leaves the recipe still unmakeable means the bot has
        // misunderstood something, and a third trip would misunderstand it again.
        FakeTradeSkills skills = OutOfFlux();
        CraftBotBase craft = new(Settings(), ShopAt(Shop));
        Node<IBotState> tree = craft.Build(skills);
        FakeBotState state = AtTheAnvil();

        for (int tick = 0; tick < 60 && craft.Stopped == CraftStop.None; tick++)
        {
            tree.Tick(state);

            if (state.MoveRequests.Count > 0)
            {
                state.Position = state.MoveRequests[^1];
                state.MoveRequests.Clear();
            }

            if (state.Actions.Exists(a => a.StartsWith("Interact(", StringComparison.Ordinal)))
            {
                state.VendorState.IsMerchantOpen = true;
            }
        }

        Assert.Equal(CraftStop.OutOfMaterials, craft.Stopped);
        Assert.Equal(CraftBotBase.MaxSupplyRuns, craft.SupplyRuns);
    }

    [Fact]
    public void ANamedRecipeItCannotMakeIsStillReportedAsUnknownRatherThanShoppedFor()
    {
        // Shopping cannot fix not knowing the recipe, and setting off for a shop would hide the
        // one thing the user needs to be told.
        CraftBotBase craft = new(Settings("Truesilver Bar"), ShopAt(Shop));
        Node<IBotState> tree = craft.Build(OutOfFlux());

        Assert.Equal(RunStatus.Failure, tree.Tick(AtTheAnvil()));
        Assert.Equal(CraftStop.UnknownRecipe, craft.Stopped);
    }
}
