namespace WoWBuddy.BotBases.Support;

/// <summary>Item quality, as the client reports it.</summary>
/// <remarks>Values are the 3.3.5 protocol's own, confirmed against the server's definitions.</remarks>
public enum ItemQuality
{
    /// <summary>Grey. Vendor fodder.</summary>
    Poor = 0,

    /// <summary>White.</summary>
    Common = 1,

    /// <summary>Green.</summary>
    Uncommon = 2,

    /// <summary>Blue.</summary>
    Rare = 3,

    /// <summary>Purple.</summary>
    Epic = 4,

    /// <summary>Orange.</summary>
    Legendary = 5,

    /// <summary>Light yellow.</summary>
    Artifact = 6,

    /// <summary>Heirloom.</summary>
    Heirloom = 7,
}

/// <summary>
/// What the client can say about an item.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors what <c>GetItemInfo</c> returns on this client: name, quality, item level,
/// required level, type and subtype, stack size, equip location and vendor price.
/// </para>
/// <para>
/// <b>Binding is not here.</b> On 3.3.5 <c>GetItemInfo</c> does not report whether an item is
/// bind-on-pickup; the only way to know is to scan a tooltip for a localised string. Loot
/// rules therefore do not depend on binding, which is no great loss — soulbound items can
/// still be sold, so binding mostly affects what is worth mailing rather than what is worth
/// picking up.
/// </para>
/// </remarks>
/// <param name="ItemId">The item's numeric id.</param>
/// <param name="Name">Its name in the client's language.</param>
/// <param name="Quality">Its quality.</param>
/// <param name="ItemLevel">Its item level.</param>
/// <param name="RequiredLevel">The level needed to use it.</param>
/// <param name="ItemClass">Its class, for example Armor, Weapon, Quest, Trade Goods.</param>
/// <param name="ItemSubClass">Its subclass, for example Cloth, Sword.</param>
/// <param name="EquipSlot">Its equip location token, for example INVTYPE_HEAD. Empty when not equippable.</param>
/// <param name="StackCount">Maximum stack size.</param>
/// <param name="SellPrice">What a vendor pays, in copper, per item.</param>
public readonly record struct ItemInfo(
    int ItemId,
    string Name,
    ItemQuality Quality,
    int ItemLevel,
    int RequiredLevel,
    string ItemClass,
    string ItemSubClass,
    string EquipSlot,
    int StackCount,
    int SellPrice)
{
    /// <summary>True when the item can be worn or wielded.</summary>
    public bool IsEquippable => !string.IsNullOrEmpty(EquipSlot) && EquipSlot != "INVTYPE_NON_EQUIP";

    /// <summary>True when the item belongs to a quest and must not be thrown away.</summary>
    /// <remarks>
    /// Compared case-insensitively against the client's own class name, which is localised.
    /// A non-English client will not match, so quest items are additionally protected by
    /// never destroying anything the loot rules did not explicitly mark as junk.
    /// </remarks>
    public bool IsQuestItem => ItemClass.Equals("Quest", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when a vendor will pay anything for it.</summary>
    public bool IsSellable => SellPrice > 0;
}

/// <summary>One slot's worth of a character's bags.</summary>
/// <param name="Bag">Which bag, 0 being the backpack.</param>
/// <param name="Slot">Which slot within the bag.</param>
/// <param name="Item">What is in it.</param>
/// <param name="Count">How many.</param>
public readonly record struct BagSlot(int Bag, int Slot, ItemInfo Item, int Count)
{
    /// <summary>What a vendor would pay for this stack.</summary>
    public int StackValue => Item.SellPrice * Count;
}

/// <summary>How full the bags are and how worn the gear is.</summary>
/// <param name="FreeSlots">Empty bag slots.</param>
/// <param name="TotalSlots">Bag slots in total.</param>
/// <param name="LowestDurabilityPercent">The worst-worn equipped item, 0 to 100.</param>
/// <param name="Copper">Money carried.</param>
public readonly record struct InventoryState(
    int FreeSlots,
    int TotalSlots,
    double LowestDurabilityPercent,
    long Copper)
{
    /// <summary>Fraction of bag space used, 0 to 1.</summary>
    public double UsedFraction => TotalSlots == 0 ? 0d : 1d - ((double)FreeSlots / TotalSlots);
}
