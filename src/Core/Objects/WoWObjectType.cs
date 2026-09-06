namespace WoWBuddy.Core.Objects;

/// <summary>
/// The client's object type tag, read from an object's <c>+0x14</c> field.
/// </summary>
/// <remarks>
/// These seven values are the complete set for 3.3.5a. Anything outside the range is proof
/// that the pointer being read is not an object, which makes this the cheapest sanity check
/// available while walking the manager's list.
/// </remarks>
public enum WoWObjectType
{
    /// <summary>Not a real object; the client uses 0 for the base type.</summary>
    Object = 0,

    /// <summary>An item, in a bag or equipped.</summary>
    Item = 1,

    /// <summary>A bag. Containers are items with extra fields.</summary>
    Container = 2,

    /// <summary>A creature: mob, NPC, vendor, pet, or totem.</summary>
    Unit = 3,

    /// <summary>A player character. Players are units with extra fields.</summary>
    Player = 4,

    /// <summary>A world object: node, chest, door, mailbox, or transport.</summary>
    GameObject = 5,

    /// <summary>A ground-targeted spell effect, such as a Blizzard or a Consecration.</summary>
    DynamicObject = 6,

    /// <summary>A player's corpse.</summary>
    Corpse = 7,
}

/// <summary>Helpers for reasoning about object types.</summary>
public static class WoWObjectTypeExtensions
{
    /// <summary>Highest legal type value, used to reject bad reads.</summary>
    public const int MaxValidType = (int)WoWObjectType.Corpse;

    /// <summary>True when <paramref name="value"/> is inside the legal range.</summary>
    public static bool IsValid(int value) => value is >= 0 and <= MaxValidType;

    /// <summary>
    /// True when the type carries the unit descriptor block, which is what decides whether
    /// health, level and faction can be read from it. Players are units too.
    /// </summary>
    public static bool IsUnitLike(this WoWObjectType type) =>
        type is WoWObjectType.Unit or WoWObjectType.Player;
}
