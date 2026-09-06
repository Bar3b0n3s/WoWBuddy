using WoWBuddy.BotBases.Support;
using Xunit;

namespace WoWBuddy.BotBases.Tests;

/// <summary>Builds items without restating every field.</summary>
internal static class Items
{
    public static ItemInfo Make(
        int id = 1,
        string name = "Thing",
        ItemQuality quality = ItemQuality.Common,
        int itemLevel = 10,
        int requiredLevel = 1,
        string itemClass = "Miscellaneous",
        string subClass = "Junk",
        string equipSlot = "",
        int sellPrice = 100) =>
        new(id, name, quality, itemLevel, requiredLevel, itemClass, subClass, equipSlot, 20, sellPrice);
}

public sealed class LootRulesTests
{
    private static LootRules Rules(LootSettings? settings = null) => new(settings ?? new LootSettings());

    [Fact]
    public void KeepsItemsAtOrAboveTheKeepQuality()
    {
        LootRules rules = Rules(new LootSettings { KeepQuality = ItemQuality.Uncommon });

        Assert.Equal(LootAction.Keep,
            rules.Decide(Items.Make(quality: ItemQuality.Rare), freeSlots: 10));
    }

    [Fact]
    public void TreatsCheapItemsAsVendorFodder()
    {
        LootRules rules = Rules(new LootSettings { KeepQuality = ItemQuality.Uncommon });

        Assert.Equal(LootAction.Sell,
            rules.Decide(Items.Make(quality: ItemQuality.Poor, sellPrice: 50), freeSlots: 10));
    }

    [Fact]
    public void LeavesVendorFodderOnceBagsAreNearlyFull()
    {
        // The reserve exists so a rare drop still has somewhere to go.
        LootRules rules = Rules(new LootSettings { ReservedSlots = 2 });

        Assert.Equal(LootAction.Leave,
            rules.Decide(Items.Make(quality: ItemQuality.Poor, sellPrice: 50), freeSlots: 2));
    }

    [Fact]
    public void StillTakesGoodItemsWhenBagsAreNearlyFull()
    {
        LootRules rules = Rules(new LootSettings { ReservedSlots = 2 });

        Assert.Equal(LootAction.Keep,
            rules.Decide(Items.Make(quality: ItemQuality.Epic), freeSlots: 1));
    }

    [Fact]
    public void AlwaysTakesQuestItems()
    {
        // Leaving one behind can strand a quest the bot then retries forever.
        LootRules rules = Rules(new LootSettings { MinimumQuality = ItemQuality.Rare });

        Assert.Equal(LootAction.Keep,
            rules.Decide(
                Items.Make(quality: ItemQuality.Poor, itemClass: "Quest", sellPrice: 0),
                freeSlots: 0));
    }

    [Fact]
    public void ExplicitListsOverrideTheGeneralRules()
    {
        LootRules rules = Rules(new LootSettings
        {
            AlwaysKeep = new HashSet<int> { 42 },
            NeverLoot = new HashSet<int> { 99 },
            MinimumQuality = ItemQuality.Epic,
        });

        Assert.Equal(LootAction.Keep, rules.Decide(Items.Make(id: 42, quality: ItemQuality.Poor), 10));
        Assert.Equal(LootAction.Leave, rules.Decide(Items.Make(id: 99, quality: ItemQuality.Legendary), 10));
    }

    [Fact]
    public void SkipsWorthlessItemsWhenAskedTo()
    {
        LootRules rules = Rules(new LootSettings { SkipWorthlessItems = true });

        Assert.Equal(LootAction.Leave,
            rules.Decide(Items.Make(quality: ItemQuality.Common, sellPrice: 0), freeSlots: 10));
    }

    [Fact]
    public void SellsOnlyThingsWorthSelling()
    {
        LootRules rules = Rules(new LootSettings { KeepQuality = ItemQuality.Uncommon });

        List<BagSlot> bags =
        [
            new(0, 1, Items.Make(id: 1, quality: ItemQuality.Poor, sellPrice: 50), 5),
            new(0, 2, Items.Make(id: 2, quality: ItemQuality.Epic, sellPrice: 9000), 1),
            new(0, 3, Items.Make(id: 3, itemClass: "Quest", sellPrice: 0), 1),
            new(0, 4, Items.Make(id: 4, quality: ItemQuality.Common, sellPrice: 0), 1),
        ];

        IReadOnlyList<BagSlot> forSale = rules.SelectForSale(bags);

        Assert.Single(forSale);
        Assert.Equal(1, forSale[0].Item.ItemId);
    }

    [Fact]
    public void DestroyingIsOffUnlessAskedFor()
    {
        // Destroying the wrong thing cannot be undone; a vendor trip only costs time.
        LootRules rules = Rules(new LootSettings { DestroyJunkWhenFull = false });

        Assert.Null(rules.SelectForDestruction(
            [new BagSlot(0, 1, Items.Make(quality: ItemQuality.Poor, sellPrice: 1), 1)]));
    }

    [Fact]
    public void DestroysTheCheapestJunkAndNothingBetter()
    {
        LootRules rules = Rules(new LootSettings { DestroyJunkWhenFull = true });

        List<BagSlot> bags =
        [
            new(0, 1, Items.Make(id: 1, quality: ItemQuality.Poor, sellPrice: 500), 1),
            new(0, 2, Items.Make(id: 2, quality: ItemQuality.Poor, sellPrice: 10), 1),
            new(0, 3, Items.Make(id: 3, quality: ItemQuality.Epic, sellPrice: 1), 1),
            new(0, 4, Items.Make(id: 4, itemClass: "Quest", sellPrice: 0), 1),
        ];

        BagSlot? doomed = rules.SelectForDestruction(bags);

        Assert.Equal(2, doomed?.Item.ItemId);
    }
}

public sealed class GearEvaluatorTests
{
    private static readonly StatWeights PlateDps = new()
    {
        Name = "Fury Warrior",
        Weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["Strength"] = 2.0,
            ["Stamina"] = 0.5,
            ["Critical Strike"] = 1.2,
            ["Intellect"] = 0.0,
        },
        AllowedArmour = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Plate", "Mail" },
        AllowedWeapons = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "One-Handed Axes", "Two-Handed Axes" },
    };

    private static GearCandidate Candidate(
        ItemInfo item, params (string Stat, int Amount)[] stats) =>
        new(item, stats.ToDictionary(s => s.Stat, s => s.Amount, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void EquipsAnythingUsableIntoAnEmptySlot()
    {
        var evaluator = new GearEvaluator(PlateDps);
        GearCandidate found = Candidate(
            Items.Make(itemClass: "Armor", subClass: "Plate", equipSlot: "INVTYPE_HEAD"),
            ("Stamina", 5));

        Assert.True(evaluator.Evaluate(found, equipped: null, characterLevel: 40).IsUpgrade);
    }

    [Fact]
    public void PrefersTheItemThatScoresHigher()
    {
        var evaluator = new GearEvaluator(PlateDps);
        ItemInfo template = Items.Make(itemClass: "Armor", subClass: "Plate", equipSlot: "INVTYPE_HEAD");

        GearCandidate better = Candidate(template, ("Strength", 20));
        GearCandidate worse = Candidate(template, ("Strength", 5));

        Assert.True(evaluator.Evaluate(better, worse, 40).IsUpgrade);
        Assert.False(evaluator.Evaluate(worse, better, 40).IsUpgrade);
    }

    [Fact]
    public void RefusesArmourTheSpecialisationCannotWear()
    {
        // Without this a warrior happily equips cloth, because cloth with good stamina
        // scores perfectly well on weights alone.
        var evaluator = new GearEvaluator(PlateDps);

        GearCandidate cloth = Candidate(
            Items.Make(itemClass: "Armor", subClass: "Cloth", equipSlot: "INVTYPE_HEAD"),
            ("Strength", 100));

        UpgradeDecision decision = evaluator.Evaluate(cloth, equipped: null, characterLevel: 40);

        Assert.False(decision.IsUpgrade);
        Assert.Contains("Cloth", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void StillAllowsRingsAndTrinketsWhichHaveNoArmourClass()
    {
        var evaluator = new GearEvaluator(PlateDps);

        GearCandidate ring = Candidate(
            Items.Make(itemClass: "Armor", subClass: "Miscellaneous", equipSlot: "INVTYPE_FINGER"),
            ("Strength", 10));

        Assert.True(evaluator.Evaluate(ring, equipped: null, characterLevel: 40).IsUpgrade);
    }

    [Fact]
    public void RefusesWeaponsTheSpecialisationCannotUse()
    {
        var evaluator = new GearEvaluator(PlateDps);

        GearCandidate wand = Candidate(
            Items.Make(itemClass: "Weapon", subClass: "Wands", equipSlot: "INVTYPE_RANGEDRIGHT"),
            ("Intellect", 50));

        Assert.False(evaluator.Evaluate(wand, equipped: null, characterLevel: 40).IsUpgrade);
    }

    [Fact]
    public void RefusesItemsTheCharacterIsTooLowFor()
    {
        var evaluator = new GearEvaluator(PlateDps);

        GearCandidate item = Candidate(
            Items.Make(itemClass: "Armor", subClass: "Plate", equipSlot: "INVTYPE_HEAD", requiredLevel: 60),
            ("Strength", 100));

        UpgradeDecision decision = evaluator.Evaluate(item, equipped: null, characterLevel: 40);

        Assert.False(decision.IsUpgrade);
        Assert.Contains("Requires level 60", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesLockedSlotsAlone()
    {
        var evaluator = new GearEvaluator(
            PlateDps,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "INVTYPE_TRINKET" });

        GearCandidate trinket = Candidate(
            Items.Make(itemClass: "Armor", subClass: "Miscellaneous", equipSlot: "INVTYPE_TRINKET"),
            ("Strength", 100));

        UpgradeDecision decision = evaluator.Evaluate(trinket, equipped: null, characterLevel: 40);

        Assert.False(decision.IsUpgrade);
        Assert.Contains("locked", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void FallsBackToItemLevelOnlyWhenTheWeightsCannotSeparateThem()
    {
        // Constant at low level, where nothing has stats the specialisation values. Using
        // item level generally would equip the wrong armour class.
        var evaluator = new GearEvaluator(PlateDps);

        ItemInfo low = Items.Make(itemClass: "Armor", subClass: "Plate", equipSlot: "INVTYPE_HEAD", itemLevel: 5);
        ItemInfo high = Items.Make(itemClass: "Armor", subClass: "Plate", equipSlot: "INVTYPE_HEAD", itemLevel: 25);

        Assert.True(evaluator.Evaluate(Candidate(high), Candidate(low), 20).IsUpgrade);
        Assert.False(evaluator.Evaluate(Candidate(low), Candidate(high), 20).IsUpgrade);
    }

    [Fact]
    public void IgnoresStatsTheSpecialisationDoesNotValue()
    {
        var evaluator = new GearEvaluator(PlateDps);
        ItemInfo template = Items.Make(itemClass: "Armor", subClass: "Plate", equipSlot: "INVTYPE_HEAD");

        GearCandidate intellect = Candidate(template, ("Intellect", 500));
        GearCandidate strength = Candidate(template, ("Strength", 10));

        Assert.False(evaluator.Evaluate(intellect, strength, 40).IsUpgrade);
    }
}

public sealed class ErrandPlannerTests
{
    private static InventoryState Inventory(
        int freeSlots = 10, double durability = 100d, long copper = 100_000) =>
        new(freeSlots, 16, durability, copper);

    [Fact]
    public void NothingIsDueWhenAllIsWell()
    {
        var planner = new ErrandPlanner(new ErrandSettings { TrainingEnabled = false });

        Assert.Equal(Errand.None, planner.Next(Inventory(), 70, hasSellableItems: true));
    }

    [Fact]
    public void RepairComesBeforeEverythingElse()
    {
        // Gear at zero durability gives no stats, and the bot finds that out by dying.
        var planner = new ErrandPlanner(new ErrandSettings());

        Assert.Equal(Errand.Repair,
            planner.Next(Inventory(freeSlots: 0, durability: 20d), 70, hasSellableItems: true));
    }

    [Fact]
    public void RepairIsSkippedWithoutTheMoneyForIt()
    {
        var planner = new ErrandPlanner(new ErrandSettings
        {
            MinimumCopperAfterRepair = 10_000,
            TrainingEnabled = false,
        });

        Assert.Equal(Errand.None,
            planner.Next(Inventory(durability: 5d, copper: 500), 70, hasSellableItems: false));
    }

    [Fact]
    public void SellingIsDueWhenBagsFillUp()
    {
        var planner = new ErrandPlanner(new ErrandSettings { SellAtFreeSlots = 2, TrainingEnabled = false });

        Assert.Equal(Errand.Sell, planner.Next(Inventory(freeSlots: 1), 70, hasSellableItems: true));
    }

    [Fact]
    public void SellingIsNotDueWithNothingToSell()
    {
        // Walking to a vendor with a bag of quest items achieves nothing and the bot would
        // do it again immediately.
        var planner = new ErrandPlanner(new ErrandSettings { TrainingEnabled = false });

        Assert.Equal(Errand.None, planner.Next(Inventory(freeSlots: 0), 70, hasSellableItems: false));
    }

    [Fact]
    public void TrainingIsDueAfterEnoughLevels()
    {
        var planner = new ErrandPlanner(new ErrandSettings { TrainEveryLevels = 2 })
        {
            LastTrainedLevel = 20,
        };

        Assert.Equal(Errand.None, planner.Next(Inventory(), 21, hasSellableItems: false));
        Assert.Equal(Errand.Train, planner.Next(Inventory(), 22, hasSellableItems: false));

        planner.NoteTrained(22);
        Assert.Equal(Errand.None, planner.Next(Inventory(), 22, hasSellableItems: false));
    }

    [Fact]
    public void MailingNeedsARecipient()
    {
        var withoutRecipient = new ErrandPlanner(new ErrandSettings
        {
            MailEnabled = true,
            MailRecipient = "",
            TrainingEnabled = false,
        });

        Assert.Equal(Errand.None,
            withoutRecipient.Next(Inventory(freeSlots: 1), 70, hasSellableItems: false));

        var withRecipient = new ErrandPlanner(new ErrandSettings
        {
            MailEnabled = true,
            MailRecipient = "Bank",
            MailAtFreeSlots = 4,
            TrainingEnabled = false,
        });

        Assert.Equal(Errand.Mail,
            withRecipient.Next(Inventory(freeSlots: 1), 70, hasSellableItems: false));
    }

    [Fact]
    public void RecognisesTheStuckCaseOfFullBagsAndNothingToSell()
    {
        // A bot in this state cannot loot, and a vendor will not fix it. The caller has to
        // know rather than loop.
        var planner = new ErrandPlanner(new ErrandSettings());

        Assert.True(planner.IsWedged(Inventory(freeSlots: 0), hasSellableItems: false));
        Assert.False(planner.IsWedged(Inventory(freeSlots: 0), hasSellableItems: true));
        Assert.False(planner.IsWedged(Inventory(freeSlots: 5), hasSellableItems: false));
    }
}
