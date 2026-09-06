using System.Globalization;

namespace WoWBuddy.Core.Objects;

/// <summary>
/// A 64-bit object GUID.
/// </summary>
/// <remarks>
/// <para>
/// GUIDs are the only stable identity in the client. Object pointers are recycled as things
/// leave and re-enter view, and names are neither unique nor available for every object, so
/// everything the bot remembers about the world is keyed by GUID.
/// </para>
/// <para>
/// The high bits encode what kind of thing the GUID refers to. For creatures and game
/// objects the entry id is packed into the middle, which is what makes it possible to tell
/// a herb node from a mailbox without resolving the object at all.
/// </para>
/// </remarks>
public readonly record struct WoWGuid(ulong Value)
{
    /// <summary>The null GUID, meaning "nothing".</summary>
    public static readonly WoWGuid Zero = new(0UL);

    /// <summary>True when this GUID refers to nothing.</summary>
    public bool IsZero => Value == 0UL;

    /// <summary>The high type nibble, identifying the broad category of object.</summary>
    public WoWGuidType Type => (WoWGuidType)((Value >> 48) & 0xFFFF);

    /// <summary>
    /// The creature or game object template id, for GUID types that pack one.
    /// Meaningless (and zero) for players and items.
    /// </summary>
    public uint Entry => Type switch
    {
        WoWGuidType.Creature or WoWGuidType.Vehicle or WoWGuidType.Pet or WoWGuidType.GameObject
            => (uint)((Value >> 24) & 0x00FFFFFF),
        _ => 0u,
    };

    /// <summary>True when the GUID refers to a player character.</summary>
    public bool IsPlayer => Type == WoWGuidType.Player;

    /// <summary>True when the GUID refers to a creature, pet or vehicle.</summary>
    public bool IsCreature => Type is WoWGuidType.Creature or WoWGuidType.Pet or WoWGuidType.Vehicle;

    public override string ToString() =>
        "0x" + Value.ToString("X16", CultureInfo.InvariantCulture);
}

/// <summary>
/// High-word GUID type tags used by 3.3.5a.
/// </summary>
/// <remarks>
/// Values are the ones the 3.3.5 server assigns when it builds a GUID (its <c>HighGuid</c>
/// enumeration), so they are protocol facts rather than client offsets. A GUID is built as
/// <c>counter | (entry &lt;&lt; 24) | (high &lt;&lt; 48)</c>, which is what makes
/// <see cref="WoWGuid.Entry"/> readable straight out of the GUID.
/// <para>
/// Containers share the item tag, so there is no separate value for them; an object's type
/// field distinguishes the two.
/// </para>
/// </remarks>
public enum WoWGuidType : ushort
{
    /// <summary>An item or a container.</summary>
    Item = 0x4000,

    /// <summary>A player character. Player GUIDs have a zero high word.</summary>
    Player = 0x0000,

    /// <summary>A world object.</summary>
    GameObject = 0xF110,

    /// <summary>A transport, such as a boat or zeppelin.</summary>
    Transport = 0xF120,

    /// <summary>An ordinary creature.</summary>
    Creature = 0xF130,

    /// <summary>A pet.</summary>
    Pet = 0xF140,

    /// <summary>A vehicle.</summary>
    Vehicle = 0xF150,

    /// <summary>A ground-targeted spell effect.</summary>
    DynamicObject = 0xF100,

    /// <summary>A corpse.</summary>
    Corpse = 0xF101,

    /// <summary>
    /// A moving transport: the boats and zeppelins that carry players between continents.
    /// Needed from phase 3, where positions aboard one are transport-relative.
    /// </summary>
    MovingTransport = 0x1FC0,
}
