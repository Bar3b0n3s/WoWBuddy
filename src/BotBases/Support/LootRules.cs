using WoWBuddy.Common.Logging;

namespace WoWBuddy.BotBases.Support;

/// <summary>What to do with an item.</summary>
public enum LootAction
{
    /// <summary>Take it and keep it.</summary>
    Keep,

    /// <summary>Take it, but sell it at the next vendor.</summary>
    Sell,

    /// <summary>Leave it on the corpse.</summary>
    Leave,

    /// <summary>Throw it away to make room.</summary>
    Destroy,
}

/// <summary>What the bot picks up, sells and throws away.</summary>
public sealed record LootSettings
{
    /// <summary>Lowest quality worth taking at all.</summary>
    public ItemQuality MinimumQuality { get; init; } = ItemQuality.Poor;

    /// <summary>Quality at or above which items are kept rather than sold.</summary>
    /// <remarks>
    /// Everything below this is vendor fodder. Uncommon by default: greens are worth more at
    /// auction than to a vendor, and a grinding character accumulates them far faster than it
    /// can use them.
    /// </remarks>
    public ItemQuality KeepQuality { get; init; } = ItemQuality.Uncommon;

    /// <summary>Items always taken and kept, whatever the other rules say.</summary>
    public IReadOnlySet<int> AlwaysKeep { get; init; } = new HashSet<int>();

    /// <summary>Items never taken.</summary>
    public IReadOnlySet<int> NeverLoot { get; init; } = new HashSet<int>();

    /// <summary>Whether to leave items worth nothing to a vendor.</summary>
    public bool SkipWorthlessItems { get; init; } = true;

    /// <summary>Free bag slots to keep in reserve before the bot stops taking vendor fodder.</summary>
    /// <remarks>
    /// Not zero. A completely full bag cannot accept a quest item or a rare drop, and the bot
    /// only discovers that after killing the thing that dropped it.
    /// </remarks>
    public int ReservedSlots { get; init; } = 2;

    /// <summary>Whether to destroy the cheapest junk when the bags are full and no vendor is near.</summary>
    /// <remarks>
    /// Off by default. Destroying the wrong thing is unrecoverable, and a bot that stops to
    /// visit a vendor loses time whereas one that destroys a rare drop loses the drop.
    /// </remarks>
    public bool DestroyJunkWhenFull { get; init; }
}

/// <summary>
/// Decides what to do with items.
/// </summary>
/// <remarks>
/// Pure decisions over item data, deliberately separate from anything that touches the client.
/// Loot rules are the settings users change most and get wrong most, and this way a rule set
/// can be checked against a list of items without a character standing over a corpse.
/// </remarks>
public sealed class LootRules
{
    private readonly LootSettings _settings;

    public LootRules(LootSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>The settings in force.</summary>
    public LootSettings Settings => _settings;

    /// <summary>
    /// Decides what to do with an item that is on a corpse.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="freeSlots">Free bag slots right now.</param>
    public LootAction Decide(ItemInfo item, int freeSlots)
    {
        // Explicit lists win over everything, because they are the user telling the bot about
        // something the general rules cannot know.
        if (_settings.NeverLoot.Contains(item.ItemId))
        {
            return LootAction.Leave;
        }

        if (_settings.AlwaysKeep.Contains(item.ItemId))
        {
            return LootAction.Keep;
        }

        // Quest items are taken whatever else is true: leaving one behind can strand a quest
        // that the bot then retries forever.
        if (item.IsQuestItem)
        {
            return LootAction.Keep;
        }

        if (item.Quality < _settings.MinimumQuality)
        {
            return LootAction.Leave;
        }

        if (_settings.SkipWorthlessItems && !item.IsSellable && item.Quality < _settings.KeepQuality)
        {
            return LootAction.Leave;
        }

        if (item.Quality >= _settings.KeepQuality)
        {
            return LootAction.Keep;
        }

        // Vendor fodder, and only worth a bag slot while there are slots to spare.
        return freeSlots > _settings.ReservedSlots ? LootAction.Sell : LootAction.Leave;
    }

    /// <summary>True when anything on the corpse is worth taking.</summary>
    public bool IsWorthLooting(IEnumerable<ItemInfo> contents, int freeSlots)
    {
        ArgumentNullException.ThrowIfNull(contents);
        return contents.Any(item => Decide(item, freeSlots) is LootAction.Keep or LootAction.Sell);
    }

    /// <summary>Everything in the bags a vendor should be offered.</summary>
    public IReadOnlyList<BagSlot> SelectForSale(IEnumerable<BagSlot> bags)
    {
        ArgumentNullException.ThrowIfNull(bags);

        return bags
            .Where(slot => slot.Item.IsSellable)
            .Where(slot => !_settings.AlwaysKeep.Contains(slot.Item.ItemId))
            .Where(slot => !slot.Item.IsQuestItem)
            .Where(slot => slot.Item.Quality < _settings.KeepQuality)
            .ToList();
    }

    /// <summary>
    /// Picks the least valuable thing to throw away when the bags are full.
    /// </summary>
    /// <remarks>
    /// Returns null unless destroying has been switched on and there is something genuinely
    /// worthless to destroy. Nothing above vendor-fodder quality is ever a candidate, and
    /// quest items and the always-keep list never are: destroying the wrong thing cannot be
    /// undone, and the cost of getting it wrong is far higher than the cost of a vendor trip.
    /// </remarks>
    public BagSlot? SelectForDestruction(IEnumerable<BagSlot> bags)
    {
        ArgumentNullException.ThrowIfNull(bags);

        if (!_settings.DestroyJunkWhenFull)
        {
            return null;
        }

        BagSlot? cheapest = bags
            .Where(slot => slot.Item.Quality <= ItemQuality.Poor)
            .Where(slot => !slot.Item.IsQuestItem)
            .Where(slot => !_settings.AlwaysKeep.Contains(slot.Item.ItemId))
            .OrderBy(slot => slot.StackValue)
            .Select(slot => (BagSlot?)slot)
            .FirstOrDefault();

        if (cheapest is { } chosen)
        {
            Log.For<LootRules>().Warning(
                "Bags are full; destroying {Count}x {Name} worth {Value} copper",
                chosen.Count, chosen.Item.Name, chosen.StackValue);
        }

        return cheapest;
    }
}
