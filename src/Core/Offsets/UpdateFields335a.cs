namespace WoWBuddy.Core.Offsets;

/// <summary>
/// Descriptor ("update field") indices for 3.3.5a build 12340.
/// </summary>
/// <remarks>
/// <para>
/// The client keeps a flat array of 4-byte fields per object, populated from the server's
/// update packets. Indices into that array are defined by the wire protocol, which makes
/// them the best-evidenced facts available: they were read off the field enumerations in
/// the AzerothCore 3.3.5 server implementation and independently agree with the values in
/// the public client-side tables (source A). A server and a client that disagreed on these
/// could not talk to each other at all, so agreement between the two is strong evidence.
/// </para>
/// <para>
/// Values below are <b>indices</b>, not byte offsets. Multiply by 4 to get a byte offset;
/// <see cref="ByteOffset"/> does this. Fields wider than 4 bytes (GUIDs) occupy two
/// consecutive indices, low dword first.
/// </para>
/// <para>
/// The blocks are cumulative: a Unit's fields start where Object's end, a Player's start
/// where Unit's end. That is why <c>UNIT_FIELD_CHARM</c> is 0x06, the same as
/// <c>OBJECT_END</c>.
/// </para>
/// </remarks>
public static class UpdateFields335a
{
    /// <summary>Converts a descriptor index to a byte offset within the descriptor array.</summary>
    public static uint ByteOffset(uint index) => index * 4u;

    /// <summary>Fields present on every object.</summary>
    public static class Object
    {
        /// <summary>64-bit GUID. Occupies indices 0 and 1.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EObjectFields; agrees with source A.",
            HowToVerify = "Must equal the object's inline GUID at object+0x30. Asserted at attach.")]
        public const uint Guid = 0x00;

        /// <summary>Type mask (a bitmask, not the same as the object's type tag).</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EObjectFields.",
            HowToVerify = "Bit 0 (Object) is set for every object.")]
        public const uint Type = 0x02;

        /// <summary>Template/entry id. Zero for players.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EObjectFields.",
            HowToVerify = "For a creature it matches the creature_template id on the server.")]
        public const uint Entry = 0x03;

        /// <summary>Model scale multiplier.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EObjectFields.",
            HowToVerify = "A float that is normally 1.0.")]
        public const uint ScaleX = 0x04;
    }

    /// <summary>Fields present on units (creatures, players, pets).</summary>
    public static class Unit
    {
        /// <summary>GUID of the unit's summoner, if any.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EUnitFields; agrees with source A (0x0E).",
            HowToVerify = "For a hunter pet it equals the owner's GUID.")]
        public const uint SummonedBy = 0x0E;

        /// <summary>GUID of the unit's current target.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EUnitFields; agrees with source A (0x12).",
            HowToVerify = "Target something in-game and confirm the local player's value matches the target's GUID.")]
        public const uint Target = 0x12;

        /// <summary>Class, race, gender and power type packed into four bytes.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EUnitFields (byte offset 0x5C agrees with source A).",
            HowToVerify = "Byte 0 is race, byte 1 class; compare against the character actually logged in.")]
        public const uint Bytes0 = 0x17;

        /// <summary>Current health.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EUnitFields; agrees with source A (0x18).",
            HowToVerify = "Compare with the health shown on the player frame.")]
        public const uint Health = 0x18;

        /// <summary>
        /// First of seven current-power fields, indexed by power type
        /// (0 mana, 1 rage, 2 focus, 3 energy, 4 happiness, 5 runes, 6 runic power).
        /// </summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EUnitFields; agrees with source A (0x19).",
            HowToVerify = "Read at the index given by the unit's own power type from Bytes0 and compare with the player frame.")]
        public const uint Power1 = 0x19;

        /// <summary>Maximum health.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EUnitFields; agrees with source A (0x20).",
            HowToVerify = "Compare with the player frame.")]
        public const uint MaxHealth = 0x20;

        /// <summary>First of seven maximum-power fields. Same indexing as <see cref="Power1"/>.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EUnitFields; agrees with source A (0x21).",
            HowToVerify = "Compare with the player frame.")]
        public const uint MaxPower1 = 0x21;

        /// <summary>Unit level.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EUnitFields; agrees with source A (0x36).",
            HowToVerify = "Compare with the character's level. Must be 1-83 for anything the bot will meet.")]
        public const uint Level = 0x36;

        /// <summary>Faction template id, which decides whether a unit is attackable.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EUnitFields.",
            HowToVerify = "Alliance and Horde player characters report different values; compare two characters.")]
        public const uint FactionTemplate = 0x37;

        /// <summary>Unit flags (see <c>UnitFlags</c>).</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EUnitFields (byte offset 0xEC agrees with source A).",
            HowToVerify = "Sit down in-game and confirm the Sitting/standing state bit changes.")]
        public const uint Flags = 0x3B;

        /// <summary>Second unit flag word.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EUnitFields.", HowToVerify = "See Flags.")]
        public const uint Flags2 = 0x3C;

        /// <summary>Bounding radius, used for melee reach maths.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EUnitFields.",
            HowToVerify = "A small positive float, typically well under 5.0.")]
        public const uint BoundingRadius = 0x41;

        /// <summary>Combat reach, used together with bounding radius for melee range.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EUnitFields.",
            HowToVerify = "A small positive float.")]
        public const uint CombatReach = 0x42;

        /// <summary>Display (model) id.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EUnitFields.",
            HowToVerify = "Changes when the unit shapeshifts or mounts.")]
        public const uint DisplayId = 0x43;

        /// <summary>Mount display id. Zero when not mounted.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EUnitFields.",
            HowToVerify = "Mount up in-game and confirm it becomes non-zero.")]
        public const uint MountDisplayId = 0x45;

        /// <summary>Dynamic flags: tapped, lootable, tracked, and similar transient state.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EUnitFields.",
            HowToVerify = "Kill a mob and confirm the lootable bit appears. Needed from phase 5 (looting).")]
        public const uint DynamicFlags = 0x4F;

        /// <summary>NPC flags: vendor, repair, trainer, flight master, and so on.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EUnitFields.",
            HowToVerify = "Compare a known vendor against a known trainer. Needed from phase 5.")]
        public const uint NpcFlags = 0x52;

        /// <summary>Standing, sitting, mounted and shapeshift state packed into four bytes.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EUnitFields.",
            HowToVerify = "Byte 0 is the stand state; sit down and confirm it changes.")]
        public const uint Bytes1 = 0x4A;
    }

    /// <summary>Fields present only on player characters.</summary>
    public static class Player
    {
        /// <summary>Skin, face, hair and facial-hair bytes.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EPlayerFields.",
            HowToVerify = "Cosmetic only; a wrong read is harmless.")]
        public const uint Bytes = 0x99;

        /// <summary>Current experience.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EPlayerFields.",
            HowToVerify = "Compare with the XP bar; gain XP and confirm it rises. Drives the XP/hour readout.")]
        public const uint Xp = 0x27A;

        /// <summary>Experience required for the next level.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EPlayerFields.",
            HowToVerify = "Compare with the XP bar maximum.")]
        public const uint NextLevelXp = 0x27B;

        /// <summary>Money in copper.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EPlayerFields.",
            HowToVerify = "Compare with the backpack gold display. Drives the gold/hour readout.")]
        public const uint Coinage = 0x492;

        /// <summary>First entry of the quest log. Each entry is five fields wide.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EPlayerFields.",
            HowToVerify = "Field 0 of an entry is the quest id; compare with the quest log in-game. Needed from phase 7.")]
        public const uint QuestLog1_1 = 0x9E;

        /// <summary>Number of 4-byte fields per quest-log entry.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 quest log layout (id, state, counts, timer).",
            HowToVerify = "Entry n's quest id sits at QuestLog1_1 + n * QuestLogEntrySize.")]
        public const uint QuestLogEntrySize = 5;

        /// <summary>First equipped-item GUID (head). Each slot is a 2-field GUID.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EPlayerFields.",
            HowToVerify = "Head slot GUID must match the equipped helm's item GUID. Needed from phase 5.")]
        public const uint InvSlotHead = 0x144;
    }

    /// <summary>Fields present on items.</summary>
    public static class Item
    {
        /// <summary>GUID of the item's owner.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EItemFields.",
            HowToVerify = "For an item in the player's bags it equals the local player GUID.")]
        public const uint Owner = 0x06;

        /// <summary>Stack size.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EItemFields.",
            HowToVerify = "Compare with the stack count shown on the bag icon.")]
        public const uint StackCount = 0x0E;

        /// <summary>Current durability.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EItemFields.",
            HowToVerify = "Compare with the item tooltip. Drives repair runs from phase 5.")]
        public const uint Durability = 0x3C;
    }

    /// <summary>Fields present on containers (bags), which extend items.</summary>
    public static class Container
    {
        /// <summary>Number of slots in the container.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EContainerFields.",
            HowToVerify = "Compare with the bag's actual size.")]
        public const uint NumSlots = 0x40;

        /// <summary>First contained-item GUID. Each slot is a 2-field GUID.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EContainerFields.",
            HowToVerify = "Slot GUIDs must resolve to Item objects in the object manager.")]
        public const uint Slot1 = 0x42;
    }

    /// <summary>Fields present on game objects (nodes, chests, doors, mailboxes).</summary>
    public static class GameObject
    {
        /// <summary>Display (model) id.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EGameObjectFields.",
            HowToVerify = "Distinct per node type; herb and ore nodes differ.")]
        public const uint DisplayId = 0x08;

        /// <summary>Game object flags.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EGameObjectFields.",
            HowToVerify = "A locked chest has the locked bit set.")]
        public const uint Flags = 0x09;

        /// <summary>State and type packed into four bytes. Byte 0 is the state (open/closed).</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EGameObjectFields.",
            HowToVerify = "Open a door and confirm byte 0 changes. Needed from phase 6 (gathering).")]
        public const uint Bytes1 = 0x11;
    }

    /// <summary>Fields present on dynamic objects (ground-targeted spell effects).</summary>
    public static class DynamicObject
    {
        /// <summary>GUID of the unit that created the effect.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EDynamicObjectFields; agrees with source B (0x06).",
            HowToVerify = "Cast a ground-targeted spell and confirm it equals the local player GUID.")]
        public const uint Caster = 0x06;

        /// <summary>Spell that created the effect.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EDynamicObjectFields; agrees with source B (0x09).",
            HowToVerify = "Compare with the cast spell's id. Needed for ground-effect avoidance in later phases.")]
        public const uint SpellId = 0x09;

        /// <summary>Effect radius in yards.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 EDynamicObjectFields; agrees with source B (0x0A).",
            HowToVerify = "A positive float matching the spell's radius.")]
        public const uint Radius = 0x0A;
    }

    /// <summary>Fields present on corpses.</summary>
    public static class Corpse
    {
        /// <summary>GUID of the player the corpse belongs to.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 ECorpseFields.",
            HowToVerify = "After dying, exactly one corpse reports the local player GUID. Needed for corpse runs in phase 4.")]
        public const uint Owner = 0x06;

        /// <summary>Corpse display id.</summary>
        [OffsetInfo(OffsetConfidence.ProtocolDefined, "AzerothCore 3.3.5 ECorpseFields.",
            HowToVerify = "Cosmetic; a wrong read is harmless.")]
        public const uint DisplayId = 0x0A;
    }
}
