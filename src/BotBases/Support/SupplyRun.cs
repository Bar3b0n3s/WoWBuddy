using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;
using WoWBuddy.Profiles;

namespace WoWBuddy.BotBases.Support;

/// <summary>One thing to buy, and how many of it.</summary>
/// <param name="ItemId">What to buy.</param>
/// <param name="Name">What it is called, for logs.</param>
/// <param name="Count">How many are wanted.</param>
public readonly record struct ShoppingItem(uint ItemId, string Name, int Count);

/// <summary>How the bot restocks a profession.</summary>
public sealed record SupplySettings
{
    /// <summary>
    /// Finds a vendor selling an item, near a position on a map.
    /// </summary>
    /// <remarks>
    /// Supplied from the user's own world data export. Without it there is no shopping: which
    /// vendor stocks Weak Flux is server data, and this project neither ships it nor guesses.
    /// </remarks>
    public Func<uint, int, Vector3, ProfileVendor?>? FindVendor { get; init; }

    /// <summary>How many crafts' worth of materials to buy in one trip.</summary>
    /// <remarks>
    /// More than one batch, because the trip is the expensive part: walking to a vendor for
    /// five flux and walking back is most of a minute for twenty seconds of crafting.
    /// </remarks>
    public int Batches { get; init; } = 4;

    /// <summary>How close counts as standing at the vendor.</summary>
    public float InteractRange { get; init; } = 4f;

    /// <summary>
    /// How far the bot will walk for materials, in yards.
    /// </summary>
    /// <remarks>
    /// A cap rather than a preference. The nearest vendor selling a reagent can be on another
    /// continent, and a character that sets off across the world for one stack of thread has
    /// stopped crafting for the evening.
    /// </remarks>
    public float MaxTripDistance { get; init; } = 1500f;

    /// <summary>Money to keep back, in copper.</summary>
    /// <remarks>
    /// Spending the last copper on reagents leaves nothing for the repair bill, and repairs are
    /// what keep the character alive.
    /// </remarks>
    public long KeepCopper { get; init; } = 10_000;

    /// <summary>How long to wait for a merchant window before giving up.</summary>
    public TimeSpan WindowTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>True when there is enough here to go shopping.</summary>
    public bool IsUsable => FindVendor is not null && Batches > 0;
}

/// <summary>Where a shopping trip has got to.</summary>
public enum SupplyStatus
{
    /// <summary>Not shopping.</summary>
    Idle,

    /// <summary>On the way to a vendor, or at one buying.</summary>
    Shopping,

    /// <summary>Everything buyable was bought and the character is back where it started.</summary>
    Done,

    /// <summary>Nothing here can be bought. <see cref="SupplyRun.Explanation"/> says why.</summary>
    Impossible,
}

/// <summary>
/// Going to a vendor for the materials a recipe needs, and coming back.
/// </summary>
/// <remarks>
/// <para>
/// What the crafting base does instead of stopping the moment the bags run dry. The client says
/// what a recipe takes and how much of it the character is carrying; the user's world data says
/// who sells the difference; this walks there, buys it and comes back.
/// </para>
/// <para>
/// <b>Most reagents cannot be bought at all.</b> Ore, herbs, leather and cloth come off the
/// world, not off a vendor, so <see cref="SupplyStatus.Impossible"/> is the ordinary answer and
/// not a failure — it puts the crafting base exactly where it was before this existed, stopped
/// with a reason. What this buys is the other kind: flux, thread, dye, salt, vials.
/// </para>
/// <para>
/// It comes back to where it set off from, which matters more than it sounds: a blacksmith
/// crafts at an anvil, and one that buys its flux and then stands in the shop has not finished
/// the errand.
/// </para>
/// </remarks>
public sealed class SupplyRun
{
    private readonly SupplySettings _settings;
    private readonly VendorApproach _approach;

    private readonly List<ShoppingItem> _list = [];
    private Vector3 _origin;
    private bool _returning;
    private int _bought;

    public SupplyRun(SupplySettings? settings = null, Func<DateTimeOffset>? clock = null)
    {
        _settings = settings ?? new SupplySettings();
        _approach = new VendorApproach(_settings.InteractRange, _settings.WindowTimeout, clock);
    }

    /// <summary>What it is doing.</summary>
    public SupplyStatus Status { get; private set; } = SupplyStatus.Idle;

    /// <summary>Why it could not shop, when it could not.</summary>
    public string Explanation { get; private set; } = string.Empty;

    /// <summary>What is still to buy.</summary>
    public IReadOnlyList<ShoppingItem> List => _list;

    /// <summary>How many items it has asked to buy this trip.</summary>
    public int Bought => _bought;

    /// <summary>What it would buy to make <paramref name="batch"/> of a recipe.</summary>
    /// <remarks>
    /// Enough for <see cref="SupplySettings.Batches"/> batches rather than one, minus whatever
    /// is already in the bags. An empty answer means either that nothing is short — the caller
    /// should not be shopping — or that the client could not say what the recipe takes, which
    /// is the same instruction: do not go.
    /// </remarks>
    public IReadOnlyList<ShoppingItem> Plan(ITradeSkills skills, TradeSkillRecipe recipe, int batch)
    {
        ArgumentNullException.ThrowIfNull(skills);

        int wanted = Math.Max(1, batch) * Math.Max(1, _settings.Batches);
        List<ShoppingItem> list = [];

        foreach (TradeSkillReagent reagent in skills.ReagentsFor(recipe))
        {
            // An unknown item id cannot be searched for, and buying by name is not something
            // the client offers. Skipping it is honest; the trip simply buys the rest.
            if (reagent.ItemId == 0)
            {
                continue;
            }

            int missing = reagent.ShortFor(wanted);

            if (missing > 0)
            {
                list.Add(new ShoppingItem(reagent.ItemId, reagent.Name, missing));
            }
        }

        return list;
    }

    /// <summary>
    /// Starts a trip for what a recipe is short of.
    /// </summary>
    /// <returns>False when there is nothing to buy or nowhere to buy it.</returns>
    public bool Begin(IBotState state, ITradeSkills skills, TradeSkillRecipe recipe, int batch)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(skills);

        Reset();

        if (!_settings.IsUsable)
        {
            return Impossible(
                "There is no world data loaded, so the bot does not know who sells anything.");
        }

        IReadOnlyList<ShoppingItem> list = Plan(skills, recipe, batch);

        if (list.Count == 0)
        {
            return Impossible(
                $"The client did not say what {recipe.Name} is made from, so there is nothing "
                + "to shop for.");
        }

        // Anything nobody sells is dropped here rather than walked towards: ore, herbs and
        // leather are the common case, and finding that out at the shop wastes the trip.
        foreach (ShoppingItem item in list)
        {
            if (Where(state, item) is not null)
            {
                _list.Add(item);
            }
        }

        if (_list.Count == 0)
        {
            return Impossible(
                $"Nothing within {_settings.MaxTripDistance:0} yards sells what {recipe.Name} "
                + "needs. Most trade materials are gathered rather than bought.");
        }

        _origin = state.Position;
        Status = SupplyStatus.Shopping;

        Log.For<SupplyRun>().Information(
            "Going shopping for {Count} material(s): {Items}",
            _list.Count,
            string.Join(", ", _list.Select(item => $"{item.Count} x {item.Name}")));

        return true;
    }

    /// <summary>Takes one step of the trip.</summary>
    public SupplyStatus Tick(IBotState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (Status != SupplyStatus.Shopping)
        {
            return Status;
        }

        if (_returning)
        {
            return GoBack(state);
        }

        if (_list.Count == 0)
        {
            _returning = true;
            _approach.Reset();
            return SupplyStatus.Shopping;
        }

        if (Where(state, _list[0]) is not { } destination)
        {
            // The vendor was there when the trip was planned and is not now, which happens when
            // the character walks onto another map. Drop it and try the next thing.
            Drop(_list[0], "there is no longer a vendor for it nearby");
            return SupplyStatus.Shopping;
        }

        switch (_approach.Step(state, destination))
        {
            case ApproachResult.Travelling:
                return SupplyStatus.Shopping;

            case ApproachResult.Failed:
                // Everything this shop was going to supply, not just the item at the head of
                // the list: the next one would send the character straight back to the vendor
                // that just refused to open, and the trip would never end.
                DropEverythingFrom(state, destination, _approach.Failure);
                _approach.Reset();
                return SupplyStatus.Shopping;

            default:
                return BuyHere(state, destination);
        }
    }

    /// <summary>Forgets the trip.</summary>
    public void Reset()
    {
        _list.Clear();
        _approach.Reset();
        _returning = false;
        _bought = 0;
        Status = SupplyStatus.Idle;
        Explanation = string.Empty;
    }

    /// <summary>
    /// Buys everything on the list this vendor stocks, then moves on.
    /// </summary>
    /// <remarks>
    /// Everything, not just what the trip came for: a general goods vendor often carries three
    /// of the four things on the list, and buying them all now saves three more walks.
    /// </remarks>
    private SupplyStatus BuyHere(IBotState state, ProfileVendor destination)
    {
        IReadOnlyList<MerchantItem> stock = state.Vendor.MerchantStock;
        long purse = state.Inventory.Copper - _settings.KeepCopper;

        for (int index = _list.Count - 1; index >= 0; index--)
        {
            ShoppingItem wanted = _list[index];

            if (Find(stock, wanted.ItemId) is not { } offer)
            {
                continue;
            }

            int count = offer.Obtainable(wanted.Count);
            long cost = Cost(offer, count);

            while (count > 0 && cost > purse)
            {
                // Buy what the money allows rather than nothing. Half a stack of thread still
                // makes something, and the alternative is a trip that achieves nothing at all.
                count -= Math.Max(1, offer.StackSize);
                cost = Cost(offer, count);
            }

            if (count <= 0)
            {
                Log.For<SupplyRun>().Warning(
                    "Not enough money for {Item}; {Copper} copper is being kept back",
                    wanted.Name, _settings.KeepCopper);
                continue;
            }

            if (!state.Vendor.Buy(offer, count))
            {
                continue;
            }

            purse -= cost;
            _bought += count;
            _list.RemoveAt(index);

            Log.For<SupplyRun>().Information(
                "Bought {Count} x {Item} from {Vendor}", count, wanted.Name, destination.Name);
        }

        state.Vendor.Close();
        _approach.Reset();

        // The shop has been seen now, so anything still on the list that it was going to supply
        // is not going to be supplied: it was sold out, or unaffordable, or the purchase did
        // not go through. Leaving it on would walk back to the same shelf forever.
        DropEverythingFrom(state, destination, $"{destination.Name} did not have it");

        return SupplyStatus.Shopping;
    }

    private SupplyStatus GoBack(IBotState state)
    {
        if (state.Position.Distance(_origin) <= _settings.InteractRange)
        {
            state.StopMoving();
            Status = _bought > 0 ? SupplyStatus.Done : SupplyStatus.Impossible;

            if (Status == SupplyStatus.Impossible)
            {
                Explanation = "The trip bought nothing.";
            }

            return Status;
        }

        if (state.MovementFailed || !state.MoveTo(_origin))
        {
            // Back at the shop with the goods and no way home. Reporting Done rather than
            // failing is right: the materials are bought, and the crafting base will find out
            // soon enough whether it can work where it is standing.
            Log.For<SupplyRun>().Warning(
                "Could not walk back to where the crafting was happening");

            Status = _bought > 0 ? SupplyStatus.Done : SupplyStatus.Impossible;
            return Status;
        }

        return SupplyStatus.Shopping;
    }

    private ProfileVendor? Where(IBotState state, ShoppingItem item)
    {
        if (_settings.FindVendor?.Invoke(item.ItemId, state.MapId, state.Position)
            is not { } vendor)
        {
            return null;
        }

        return state.Position.Distance(vendor.Position) <= _settings.MaxTripDistance
            ? vendor
            : null;
    }

    /// <summary>Gives up on everything a vendor was going to supply.</summary>
    private void DropEverythingFrom(IBotState state, ProfileVendor vendor, string why)
    {
        for (int index = _list.Count - 1; index >= 0; index--)
        {
            if (Where(state, _list[index]) == vendor)
            {
                Drop(_list[index], why);
            }
        }
    }

    private void Drop(ShoppingItem item, string why)
    {
        _list.Remove(item);

        Log.For<SupplyRun>().Warning(
            "Not buying {Item}: {Reason}", item.Name, why);
    }

    private bool Impossible(string explanation)
    {
        Status = SupplyStatus.Impossible;
        Explanation = explanation;
        return false;
    }

    private static long Cost(MerchantItem offer, int count) =>
        offer.Price * Purchases(offer, count);

    /// <summary>How many times the buy button has to be pressed for that many items.</summary>
    private static int Purchases(MerchantItem offer, int count)
    {
        int stack = Math.Max(1, offer.StackSize);
        return (count + stack - 1) / stack;
    }

    private static MerchantItem? Find(IReadOnlyList<MerchantItem> stock, uint itemId)
    {
        foreach (MerchantItem item in stock)
        {
            if (item.ItemId == itemId && item.InStock)
            {
                return item;
            }
        }

        return null;
    }
}
