using WoWBuddy.BotBases.Support;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Objects;
using WoWBuddy.Profiles;
using Xunit;

namespace WoWBuddy.BotBases.Tests;

public sealed class SupplyRunTests
{
    private const uint FluxId = 2880;
    private const uint ThreadId = 2320;
    private const uint OreId = 2770;
    private const uint VendorEntry = 1234;
    private const uint OtherVendorEntry = 5678;

    private static readonly Vector3 Anvil = new(100f, 100f, 10f);
    private static readonly Vector3 Shop = new(130f, 100f, 10f);
    private static readonly Vector3 FarAway = new(9000f, 100f, 10f);

    private static ProfileVendor Vendor(string name, Vector3 where, uint entry = VendorEntry) =>
        new(name, entry, MapId: 0, where);

    /// <summary>A world where one shop sells everything named.</summary>
    private static SupplySettings OneShop(
        Vector3 where,
        params uint[] items)
    {
        HashSet<uint> stocked = [.. items];

        return new SupplySettings
        {
            Batches = 2,
            FindVendor = (item, _, _) => stocked.Contains(item) ? Vendor("Smith Argus", where) : null,
        };
    }

    private static FakeBotState AtTheAnvil()
    {
        FakeBotState state = new() { Position = Anvil };

        // The vendor has to be visible to be clicked, the same as any other interaction.
        state.VisibleObjects =
        [
            new VisibleObject(new WoWGuid(0x40), VendorEntry, Shop, HasPosition: true, Distance: 1f),
            new VisibleObject(new WoWGuid(0x41), OtherVendorEntry, Shop, HasPosition: true, Distance: 1f),
        ];

        return state;
    }

    private static FakeTradeSkills Blacksmith(int flux = 0, int thread = 0)
    {
        FakeTradeSkills skills = new();
        skills.With("Rough Grinding Stone", RecipeDifficulty.Optimal, available: 0);
        skills.MadeFrom("Rough Grinding Stone", FluxId, "Weak Flux", needed: 2, have: flux);
        skills.MadeFrom("Rough Grinding Stone", ThreadId, "Coarse Thread", needed: 1, have: thread);
        return skills;
    }

    /// <summary>Walks the trip to its end, or gives up after enough ticks to have finished.</summary>
    private static SupplyStatus RunToCompletion(SupplyRun run, FakeBotState state, int ticks = 20)
    {
        SupplyStatus status = SupplyStatus.Shopping;

        for (int tick = 0; tick < ticks && status == SupplyStatus.Shopping; tick++)
        {
            status = run.Tick(state);

            // Movement in a test is instant: the point being tested is the decisions, not the
            // pathfinding, which has its own tests.
            if (state.MoveRequests.Count > 0)
            {
                state.Position = state.MoveRequests[^1];
                state.MoveRequests.Clear();
            }

            // Clicking the vendor opens its window, as it would in the client.
            if (state.Actions.Exists(action => action.StartsWith("Interact(", StringComparison.Ordinal)))
            {
                state.VendorState.IsMerchantOpen = true;
            }
        }

        return status;
    }

    [Fact]
    public void ItBuysEnoughForSeveralBatchesLessWhatIsAlreadyCarried()
    {
        // The trip is the expensive part, so it is worth stocking up: walking to a shop for two
        // flux and walking back is most of a minute for twenty seconds of crafting.
        SupplyRun run = new(OneShop(Shop, FluxId, ThreadId));
        FakeTradeSkills skills = Blacksmith(flux: 3);

        IReadOnlyList<ShoppingItem> list = run.Plan(skills, skills.Known[0], batch: 5);

        // Two batches of five, at two flux each, is twenty; three are already in the bags.
        Assert.Equal(17, list.Single(item => item.ItemId == FluxId).Count);
        Assert.Equal(10, list.Single(item => item.ItemId == ThreadId).Count);
    }

    [Fact]
    public void WithoutWorldDataItDoesNotShopAtAll()
    {
        // Which vendor stocks what is server data. Without an export the base stops with a
        // reason, exactly as it did before any of this existed.
        SupplyRun run = new();
        FakeTradeSkills skills = Blacksmith();

        Assert.False(run.Begin(AtTheAnvil(), skills, skills.Known[0], batch: 5));
        Assert.Equal(SupplyStatus.Impossible, run.Status);
        Assert.Contains("no world data", run.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MaterialsNobodySellsAreNotWalkedTowards()
    {
        // The ordinary case: ore, herbs and leather come off the world, not off a shelf.
        SupplyRun run = new(OneShop(Shop, FluxId));
        FakeTradeSkills skills = new();
        skills.With("Copper Bar", RecipeDifficulty.Optimal, available: 0);
        skills.MadeFrom("Copper Bar", OreId, "Copper Ore", needed: 1, have: 0);

        Assert.False(run.Begin(AtTheAnvil(), skills, skills.Known[0], batch: 5));
        Assert.Equal(SupplyStatus.Impossible, run.Status);
        Assert.Contains("gathered rather than bought", run.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void ARecipeTheClientCannotDescribeIsNotShoppedFor()
    {
        // An empty reagent list means the client did not say, not that the recipe is free.
        SupplyRun run = new(OneShop(Shop, FluxId));
        FakeTradeSkills skills = new();
        skills.With("Mystery Item", RecipeDifficulty.Optimal, available: 0);

        Assert.False(run.Begin(AtTheAnvil(), skills, skills.Known[0], batch: 5));
        Assert.Contains("did not say what", run.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void AShopOnTheOtherSideOfTheWorldIsNotWorthTheWalk()
    {
        // A character that sets off across the continent for a stack of thread has stopped
        // crafting for the evening.
        SupplyRun run = new(OneShop(FarAway, FluxId, ThreadId));
        FakeTradeSkills skills = Blacksmith();

        Assert.False(run.Begin(AtTheAnvil(), skills, skills.Known[0], batch: 5));
        Assert.Equal(SupplyStatus.Impossible, run.Status);
    }

    [Fact]
    public void ItWalksToTheShopBuysAndComesBack()
    {
        SupplyRun run = new(OneShop(Shop, FluxId, ThreadId));
        FakeTradeSkills skills = Blacksmith();
        FakeBotState state = AtTheAnvil();

        state.VendorState.Selling(FluxId, "Weak Flux", price: 20);
        state.VendorState.Selling(ThreadId, "Coarse Thread", price: 10);

        Assert.True(run.Begin(state, skills, skills.Known[0], batch: 5));
        Assert.Equal(SupplyStatus.Done, RunToCompletion(run, state));

        // Everything on the list, in one visit: a general goods vendor usually has most of it,
        // and going back for the second thing would be another walk.
        Assert.Contains("Buy(Weak Flux, 20)", state.VendorState.Actions);
        Assert.Contains("Buy(Coarse Thread, 10)", state.VendorState.Actions);
        Assert.Contains("Close", state.VendorState.Actions);

        // Back at the anvil, which is the half of the errand that is easy to forget: a
        // blacksmith that buys its flux and stays in the shop has not finished.
        Assert.Equal(Anvil, state.Position);
        Assert.Equal(30, run.Bought);
    }

    [Fact]
    public void ItKeepsMoneyBackForTheRepairBill()
    {
        // Spending the last copper on reagents leaves nothing for repairs, and repairs are what
        // keep the character alive.
        SupplyRun run = new(new SupplySettings
        {
            Batches = 2,
            KeepCopper = 10_000,
            FindVendor = (_, _, _) => Vendor("Smith Argus", Shop),
        });

        FakeTradeSkills skills = Blacksmith();
        FakeBotState state = AtTheAnvil();

        state.Inventory = new InventoryState(16, 16, 100d, Copper: 10_050);
        state.VendorState.Selling(FluxId, "Weak Flux", price: 100);
        state.VendorState.Selling(ThreadId, "Coarse Thread", price: 100);

        Assert.True(run.Begin(state, skills, skills.Known[0], batch: 5));
        RunToCompletion(run, state);

        // Fifty copper to spend, so nothing at a hundred each.
        Assert.DoesNotContain(
            state.VendorState.Actions,
            action => action.StartsWith("Buy(", StringComparison.Ordinal));
    }

    [Fact]
    public void ItBuysWhatTheMoneyAllowsRatherThanNothing()
    {
        // Half a stack of thread still makes something, and the alternative is a trip that
        // achieves nothing at all.
        SupplyRun run = new(new SupplySettings
        {
            Batches = 1,
            KeepCopper = 0,
            FindVendor = (item, _, _) => item == FluxId ? Vendor("Smith Argus", Shop) : null,
        });

        FakeTradeSkills skills = new();
        skills.With("Rough Grinding Stone", RecipeDifficulty.Optimal, available: 0);
        skills.MadeFrom("Rough Grinding Stone", FluxId, "Weak Flux", needed: 10, have: 0);

        FakeBotState state = AtTheAnvil();
        state.Inventory = new InventoryState(16, 16, 100d, Copper: 40);
        state.VendorState.Selling(FluxId, "Weak Flux", price: 10);

        Assert.True(run.Begin(state, skills, skills.Known[0], batch: 1));
        RunToCompletion(run, state);

        Assert.Contains("Buy(Weak Flux, 4)", state.VendorState.Actions);
    }

    [Fact]
    public void ASoldOutShelfIsNotBoughtFrom()
    {
        SupplyRun run = new(OneShop(Shop, FluxId));
        FakeTradeSkills skills = new();
        skills.With("Rough Grinding Stone", RecipeDifficulty.Optimal, available: 0);
        skills.MadeFrom("Rough Grinding Stone", FluxId, "Weak Flux", needed: 2, have: 0);

        FakeBotState state = AtTheAnvil();
        state.VendorState.Selling(FluxId, "Weak Flux", price: 10, available: 0);

        Assert.True(run.Begin(state, skills, skills.Known[0], batch: 5));

        // Nothing bought, so the trip reports that it achieved nothing rather than that it
        // succeeded — which is what stops the crafting base from trying again forever.
        Assert.Equal(SupplyStatus.Impossible, RunToCompletion(run, state));
        Assert.DoesNotContain(
            state.VendorState.Actions,
            action => action.StartsWith("Buy(", StringComparison.Ordinal));
    }

    [Fact]
    public void ALimitedStockShelfSellsOnlyWhatItHas()
    {
        SupplyRun run = new(OneShop(Shop, FluxId));
        FakeTradeSkills skills = new();
        skills.With("Rough Grinding Stone", RecipeDifficulty.Optimal, available: 0);
        skills.MadeFrom("Rough Grinding Stone", FluxId, "Weak Flux", needed: 10, have: 0);

        FakeBotState state = AtTheAnvil();
        state.VendorState.Selling(FluxId, "Weak Flux", price: 1, stackSize: 1, available: 3);

        Assert.True(run.Begin(state, skills, skills.Known[0], batch: 1));
        RunToCompletion(run, state);

        Assert.Contains("Buy(Weak Flux, 3)", state.VendorState.Actions);
    }

    [Fact]
    public void AVendorThatNeverOpensItsWindowIsGivenUpOn()
    {
        // The character can stand in exactly the right place with no window at all. Without a
        // timeout the bot clicks at nothing for the rest of the session.
        DateTimeOffset now = DateTimeOffset.UnixEpoch;

        SupplyRun run = new(
            OneShop(Shop, FluxId) with { WindowTimeout = TimeSpan.FromSeconds(10) },
            () => now);

        FakeTradeSkills skills = Blacksmith();
        FakeBotState state = AtTheAnvil();

        Assert.True(run.Begin(state, skills, skills.Known[0], batch: 5));

        state.Position = Shop;

        Assert.Equal(SupplyStatus.Shopping, run.Tick(state));
        Assert.Contains(state.Actions, action => action.StartsWith("Interact(", StringComparison.Ordinal));

        now += TimeSpan.FromSeconds(30);

        // Given up on everything the shop was going to supply, walked home, and reported that
        // it achieved nothing — rather than clicking at a vendor for the rest of the session.
        SupplyStatus status = SupplyStatus.Shopping;

        for (int tick = 0; tick < 10 && status == SupplyStatus.Shopping; tick++)
        {
            status = run.Tick(state);

            if (state.MoveRequests.Count > 0)
            {
                state.Position = state.MoveRequests[^1];
                state.MoveRequests.Clear();
            }
        }

        Assert.Equal(SupplyStatus.Impossible, status);
        Assert.Equal(Anvil, state.Position);
    }
}
