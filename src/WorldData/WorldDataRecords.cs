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
    int Rank = 0,
    int TrainerType = -1,
    int TrainerClass = 0)
{
    /// <summary>Teaches a class its abilities.</summary>
    public const int ClassTrainerType = 0;

    /// <summary>Teaches mounts.</summary>
    public const int MountTrainerType = 1;

    /// <summary>Teaches professions.</summary>
    public const int ProfessionTrainerType = 2;

    /// <summary>Teaches pets.</summary>
    public const int PetTrainerType = 3;

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

    /// <summary>
    /// True when this trainer teaches the given class its own abilities.
    /// </summary>
    /// <remarks>
    /// The Train errand needs a trainer for the character's class specifically. A profession
    /// trainer, a mount vendor and a pet trainer all carry the trainer flag and none of them
    /// teaches a warrior how to hit things.
    /// </remarks>
    public bool TrainsClass(int characterClass) =>
        IsTrainer && TrainerType == ClassTrainerType && TrainerClass == characterClass;

    /// <summary>True when this trainer teaches professions.</summary>
    public bool TrainsProfessions => IsTrainer && TrainerType == ProfessionTrainerType;
}

/// <summary>Something a vendor sells.</summary>
/// <param name="VendorEntry">The creature selling it.</param>
/// <param name="ItemEntry">What it sells.</param>
public readonly record struct VendorItem(uint VendorEntry, uint ItemEntry);

/// <summary>One thing a quest asks for.</summary>
/// <param name="Entry">The creature, object or item.</param>
/// <param name="Count">How many.</param>
/// <param name="IsItem">Whether it is an item to collect rather than something to kill.</param>
public readonly record struct QuestRequirement(uint Entry, int Count, bool IsItem);

/// <summary>
/// What a quest actually wants.
/// </summary>
/// <remarks>
/// The questing base can read the log and see whether an objective is finished, but not what it
/// is for: the log gives text in the client's language. This says which creature to kill and
/// how many, which is what turns "work the objective" into something the bot can act on.
/// </remarks>
/// <param name="Id">The quest id.</param>
/// <param name="Title">Its name, for logs.</param>
/// <param name="MinLevel">The level needed to take it.</param>
/// <param name="QuestLevel">The level it is written for.</param>
/// <param name="Requirements">What it asks for, kills and collections together.</param>
public readonly record struct QuestTemplate(
    uint Id,
    string Title,
    int MinLevel,
    int QuestLevel,
    IReadOnlyList<QuestRequirement> Requirements)
{
    /// <summary>Things to kill or interact with.</summary>
    public IEnumerable<QuestRequirement> Kills =>
        Requirements.Where(requirement => !requirement.IsItem);

    /// <summary>Things to collect.</summary>
    public IEnumerable<QuestRequirement> Collections =>
        Requirements.Where(requirement => requirement.IsItem);

    /// <summary>True when the quest asks for anything the bot could work towards.</summary>
    public bool HasRequirements => Requirements.Count > 0;
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
