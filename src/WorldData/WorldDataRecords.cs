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
public readonly record struct CreatureTemplate(uint Entry, string Name, uint NpcFlags)
{
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
