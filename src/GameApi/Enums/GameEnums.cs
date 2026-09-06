namespace WoWBuddy.GameApi.Enums;

/// <summary>
/// The ten playable classes of 3.3.5a.
/// </summary>
/// <remarks>
/// Values are the ones the 3.3.5 protocol uses. Note the gap at 10: it is unused in Wrath,
/// and Druid is 11, not 10.
/// </remarks>
public enum WoWClass
{
    None = 0,
    Warrior = 1,
    Paladin = 2,
    Hunter = 3,
    Rogue = 4,
    Priest = 5,
    DeathKnight = 6,
    Shaman = 7,
    Mage = 8,
    Warlock = 9,
    Druid = 11,
}

/// <summary>
/// The ten playable races of 3.3.5a.
/// </summary>
/// <remarks>
/// Value 9 (Goblin) exists in the protocol but is not playable until Cataclysm, so it is
/// absent here.
/// </remarks>
public enum WoWRace
{
    None = 0,
    Human = 1,
    Orc = 2,
    Dwarf = 3,
    NightElf = 4,
    Undead = 5,
    Tauren = 6,
    Gnome = 7,
    Troll = 8,
    BloodElf = 10,
    Draenei = 11,
}

/// <summary>
/// The power bars a unit can have.
/// </summary>
/// <remarks>
/// A unit has exactly one active power type, named in its packed bytes field, and that value
/// indexes the seven power descriptor fields. Happiness belongs to hunter pets and Rune and
/// Runic Power to death knights, both of which are 3.3.5a-era mechanics.
/// </remarks>
public enum PowerType
{
    Mana = 0,
    Rage = 1,
    Focus = 2,
    Energy = 3,
    Happiness = 4,
    Rune = 5,
    RunicPower = 6,
}

/// <summary>Whether a unit is standing, sitting, sleeping or kneeling.</summary>
/// <remarks>
/// Sitting matters to the bot beyond cosmetics: eating and drinking require it, and a
/// character that is sitting when it should be fighting is a bug worth noticing.
/// </remarks>
public enum StandState : byte
{
    Stand = 0,
    Sit = 1,
    SitChair = 2,
    Sleep = 3,
    SitLowChair = 4,
    SitMediumChair = 5,
    SitHighChair = 6,
    Dead = 7,
    Kneel = 8,
}

/// <summary>
/// Unit flag bits that the bot actually reasons about.
/// </summary>
/// <remarks>
/// Only the flags with a bearing on bot decisions are named; the rest of the word is left
/// unnamed rather than half-guessed. Values are from the 3.3.5 server's own definitions,
/// so they are protocol facts rather than client offsets.
/// </remarks>
[Flags]
public enum UnitFlags : uint
{
    None = 0,

    /// <summary>The server has taken control away from the player.</summary>
    ServerControlled = 0x00000001,

    /// <summary>Non-attackable. A vendor or quest giver standing in peace.</summary>
    NonAttackable = 0x00000002,

    /// <summary>Movement is locked, for example while in a vehicle or rooted by a cutscene.</summary>
    DisableMove = 0x00000004,

    /// <summary>Controlled by a player rather than by server AI.</summary>
    PlayerControlled = 0x00000008,

    /// <summary>Playing the loot animation.</summary>
    Looting = 0x00000400,

    /// <summary>Flagged for PvP.</summary>
    Pvp = 0x00001000,

    /// <summary>Silenced: cannot cast.</summary>
    Silenced = 0x00002000,

    /// <summary>Playing the swim animation, which is how the bot knows it is in deep water.</summary>
    Swimming = 0x00008000,

    /// <summary>Pacified: cannot attack.</summary>
    Pacified = 0x00020000,

    /// <summary>Currently in combat.</summary>
    InCombat = 0x00080000,

    /// <summary>Mounted on a taxi. The bot must not try to move while this is set.</summary>
    TaxiFlight = 0x00100000,

    /// <summary>Cannot act: stunned, or otherwise fully controlled.</summary>
    Stunned = 0x00040000,

    /// <summary>Confused, as by a fear or a wandering effect.</summary>
    Confused = 0x00400000,

    /// <summary>Fleeing.</summary>
    Fleeing = 0x00800000,

    /// <summary>Under another unit's control.</summary>
    Possessed = 0x01000000,

    /// <summary>Cannot be selected by a player.</summary>
    NotSelectable = 0x02000000,

    /// <summary>Skinnable. Set on a corpse once it has been looted.</summary>
    Skinnable = 0x04000000,

    /// <summary>Mounted.</summary>
    Mount = 0x08000000,
}

/// <summary>
/// Dynamic flag bits describing transient state of a unit.
/// </summary>
/// <remarks>
/// These drive looting and tapping decisions from phase 5 onward: a mob that is lootable but
/// tapped by someone else is not the bot's to loot.
/// </remarks>
[Flags]
public enum UnitDynamicFlags : uint
{
    None = 0,

    /// <summary>Has loot on it right now.</summary>
    Lootable = 0x0001,

    /// <summary>Highlighted by a tracking ability, such as Find Herbs.</summary>
    TrackUnit = 0x0002,

    /// <summary>Tapped by another player or group.</summary>
    Tapped = 0x0004,

    /// <summary>Tapped by the local player, so its loot is the bot's.</summary>
    TappedByMe = 0x0008,

    /// <summary>Special info is visible, as when a rogue inspects a lock.</summary>
    SpecialInfo = 0x0010,

    /// <summary>Displayed as dead even before the death animation finishes.</summary>
    Dead = 0x0020,

    /// <summary>Recruit-a-friend bonus applies.</summary>
    ReferAFriend = 0x0040,

    /// <summary>Tapped by everyone on the threat list, so its loot is shared.</summary>
    TappedByAllThreatList = 0x0080,
}

/// <summary>
/// NPC service flag bits.
/// </summary>
/// <remarks>
/// The bot's vendor, repair, train and flight-path errands from phase 5 onward all come down
/// to finding a nearby unit with the right bit set. Values are from the 3.3.5 server's own
/// definitions.
/// </remarks>
[Flags]
public enum NpcFlags : uint
{
    None = 0,
    Gossip = 0x00000001,
    QuestGiver = 0x00000002,
    Trainer = 0x00000010,
    ClassTrainer = 0x00000020,
    ProfessionTrainer = 0x00000040,
    Vendor = 0x00000080,
    AmmoVendor = 0x00000100,
    FoodVendor = 0x00000200,
    PoisonVendor = 0x00000400,
    ReagentVendor = 0x00000800,
    Repair = 0x00001000,
    FlightMaster = 0x00002000,
    SpiritHealer = 0x00004000,
    SpiritGuide = 0x00008000,
    Innkeeper = 0x00010000,
    Banker = 0x00020000,
    Petitioner = 0x00040000,
    TabardDesigner = 0x00080000,
    BattleMaster = 0x00100000,
    Auctioneer = 0x00200000,
    StableMaster = 0x00400000,
    GuildBanker = 0x00800000,
    Mailbox = 0x04000000,
}
