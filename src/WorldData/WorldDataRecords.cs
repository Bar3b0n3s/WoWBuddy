using WoWBuddy.Common.Geometry;

namespace WoWBuddy.WorldData;

/// <summary>Which server project's database an export came from.</summary>
public enum CoreFlavour
{
    /// <summary>Not stated.</summary>
    Unknown = 0,

    /// <summary>TrinityCore. Its <c>creature</c> table has a single <c>id</c> column.</summary>
    TrinityCore = 1,

    /// <summary>AzerothCore. Its <c>creature</c> table has <c>id1</c>, <c>id2</c> and <c>id3</c>.</summary>
    AzerothCore = 2,
}

/// <summary>Where a world object spawns, from the server's own spawn table.</summary>
/// <param name="Guid">The spawn's database id, unique within the table.</param>
/// <param name="Entry">The template id, shared by every copy of the same object.</param>
/// <param name="MapId">Which map it is on.</param>
/// <param name="Position">Where it spawns.</param>
public readonly record struct GameObjectSpawn(uint Guid, uint Entry, int MapId, Vector3 Position);

/// <summary>What a world object is, from the server's template table.</summary>
/// <param name="Entry">The template id.</param>
/// <param name="Name">Its name, in whatever language the database holds.</param>
/// <param name="Type">
/// The object type. Herb nodes, ore veins and chests are all type 3, so the type alone does
/// not distinguish a Peacebloom from a treasure chest; the name and entry do.
/// </param>
/// <param name="LockId">Its lock, which is what really decides the skill needed to open it.</param>
public readonly record struct GameObjectTemplate(uint Entry, string Name, int Type, uint LockId)
{
    /// <summary>The type value the server uses for chests, herbs and mining veins.</summary>
    public const int ChestType = 3;

    /// <summary>True when this is the type gatherable nodes use.</summary>
    public bool IsGatherableType => Type == ChestType;
}

/// <summary>Where a creature spawns.</summary>
/// <param name="Guid">The spawn's database id.</param>
/// <param name="Entry">The template id.</param>
/// <param name="MapId">Which map it is on.</param>
/// <param name="Position">Where it spawns.</param>
public readonly record struct CreatureSpawn(uint Guid, uint Entry, int MapId, Vector3 Position);

/// <summary>
/// What a creature is and what services it offers.
/// </summary>
/// <param name="Entry">The template id.</param>
/// <param name="Name">Its name.</param>
/// <param name="NpcFlags">Its service flags, matching the client's own NPC flag bits.</param>
public readonly record struct CreatureTemplate(
    uint Entry,
    string Name,
    uint NpcFlags,
    uint Faction = 0,
    int MinLevel = 0,
    int MaxLevel = 0,
    int Rank = 0)
{
    /// <summary>Ordinary. Most things a grinding character kills.</summary>
    public const int NormalRank = 0;

    /// <summary>Elite. Built to be fought by a group.</summary>
    public const int EliteRank = 1;

    /// <summary>Rare elite.</summary>
    public const int RareEliteRank = 2;

    /// <summary>Boss.</summary>
    public const int BossRank = 3;

    /// <summary>Rare, but not elite.</summary>
    public const int RareRank = 4;

    // The bits the bot cares about. Values are the 3.3.5 protocol's own, the same ones the
    // client reports in a unit's descriptor, which is what lets a creature found in the
    // database be recognised again in the object manager.
    private const uint VendorFlag = 0x00000080;
    private const uint RepairFlag = 0x00001000;
    private const uint FlightMasterFlag = 0x00002000;
    private const uint InnkeeperFlag = 0x00010000;
    private const uint BankerFlag = 0x00020000;
    private const uint TrainerFlag = 0x00000010 | 0x00000020 | 0x00000040;

    /// <summary>True when this creature buys and sells.</summary>
    public bool IsVendor => (NpcFlags & VendorFlag) != 0;

    /// <summary>True when this creature repairs gear.</summary>
    public bool CanRepair => (NpcFlags & RepairFlag) != 0;

    /// <summary>True when this creature trains anything.</summary>
    public bool IsTrainer => (NpcFlags & TrainerFlag) != 0;

    /// <summary>True when this creature sells flights.</summary>
    public bool IsFlightMaster => (NpcFlags & FlightMasterFlag) != 0;

    /// <summary>True when this creature is an innkeeper.</summary>
    public bool IsInnkeeper => (NpcFlags & InnkeeperFlag) != 0;

    /// <summary>True when this creature is a banker.</summary>
    public bool IsBanker => (NpcFlags & BankerFlag) != 0;

    /// <summary>
    /// True when this creature is built to be fought by a group.
    /// </summary>
    /// <remarks>
    /// The single most useful thing the database says about a creature. An elite kills a
    /// solo character of its own level, and nothing the bot can see in memory distinguishes
    /// one from an ordinary mob until the fight is already going badly.
    /// </remarks>
    public bool IsElite => Rank is EliteRank or RareEliteRank or BossRank;

    /// <summary>True when this creature is a boss.</summary>
    public bool IsBoss => Rank == BossRank;

    /// <summary>True when it is worth more than the usual, elite or not.</summary>
    public bool IsRare => Rank is RareRank or RareEliteRank;

    /// <summary>True when the level range is known.</summary>
    public bool HasLevels => MinLevel > 0 && MaxLevel >= MinLevel;
}

/// <summary>
/// What an item is, from the server's own table.
/// </summary>
/// <remarks>
/// The bot can ask the client the same questions, but only about items it is carrying and only
/// one round trip at a time. Having the table means a loot decision can be made before the
/// item is picked up rather than after.
/// </remarks>
/// <param name="Entry">Its id.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Quality">Grey through legendary, as the client numbers them.</param>
/// <param name="ItemLevel">Its item level.</param>
/// <param name="RequiredLevel">The level needed to use it.</param>
/// <param name="Class">Its class: weapon, armour, consumable, trade goods, quest.</param>
/// <param name="SubClass">Its subclass within that.</param>
/// <param name="InventoryType">Where it is worn, or zero when it is not worn.</param>
/// <param name="SellPrice">What a vendor pays, in copper. Zero means a vendor will not take it.</param>
/// <param name="Stackable">How many fit in one slot.</param>
public readonly record struct ItemTemplate(
    uint Entry,
    string Name,
    int Quality,
    int ItemLevel,
    int RequiredLevel,
    int Class,
    int SubClass,
    int InventoryType,
    int SellPrice,
    int Stackable)
{
    /// <summary>The item class the server uses for quest items.</summary>
    public const int QuestClass = 12;

    /// <summary>True when a vendor will pay anything for it.</summary>
    public bool IsSellable => SellPrice > 0;

    /// <summary>True when it belongs to a quest and must not be thrown away or sold.</summary>
    public bool IsQuestItem => Class == QuestClass;

    /// <summary>True when it can be worn or wielded.</summary>
    public bool IsEquippable => InventoryType != 0;
}

/// <summary>A creature spawn together with what it does, which is what an errand needs.</summary>
/// <param name="Spawn">Where it is.</param>
/// <param name="Template">What it offers.</param>
public readonly record struct ServiceNpc(CreatureSpawn Spawn, CreatureTemplate Template)
{
    /// <summary>Where to walk to.</summary>
    public Vector3 Position => Spawn.Position;

    /// <summary>Which map it is on.</summary>
    public int MapId => Spawn.MapId;

    /// <summary>Its name.</summary>
    public string Name => Template.Name;
}
