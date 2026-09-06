namespace WoWBuddy.Core.Offsets;

/// <summary>
/// Every static address and structure offset the bot uses for WoW 3.3.5a build 12340.
/// </summary>
/// <remarks>
/// <para>
/// This is the single place where low-level client facts live. Nothing outside this file
/// may contain a magic address. Each constant carries an <see cref="OffsetInfoAttribute"/>
/// recording where the value came from and how to re-check it; see
/// <c>docs/offsets.md</c> for the generated provenance table and the source key.
/// </para>
/// <para>
/// <b>Addresses are absolute for the default image base.</b> The published community values
/// assume WoW.exe is loaded at <see cref="DefaultImageBase"/>, which it normally is (the
/// 2010 build is not ASLR-aware). Private-server launchers do occasionally rebase it, so
/// every static address is translated through <c>Rebase</c> against the real module base
/// rather than being used raw.
/// </para>
/// <para>
/// <b>Nothing here is trusted on faith.</b> Values marked <see cref="OffsetConfidence.Conflicted"/>
/// or <see cref="OffsetConfidence.Unverified"/> are resolved against the live client during
/// attach; if they cannot be resolved, the bot refuses to start rather than acting on a
/// bad read.
/// </para>
/// </remarks>
public static class Offsets335a
{
    /// <summary>The build this table describes. Attach rejects any other build.</summary>
    [OffsetInfo(
        OffsetConfidence.Corroborated,
        "The build number of the 3.3.5a client, stated by the client's own version resource and by every source consulted.",
        HowToVerify = "Right-click WoW.exe, Properties, Details: the file version reads 3.3.5.12340. ClientBuildDetector reads the same resource at attach.")]
    public const int SupportedBuild = 12340;

    /// <summary>The client version this table describes.</summary>
    public const string SupportedVersion = "3.3.5";

    /// <summary>
    /// Image base the published addresses assume. Static addresses are rebased from this
    /// onto the module's actual load address.
    /// </summary>
    [OffsetInfo(
        OffsetConfidence.Corroborated,
        "The standard PE image base for the 2010 client, which is not ASLR-aware; consistent with every published address in the community tables being expressed against it.",
        HowToVerify = "Compare against the module base reported by the process at attach. When they differ, Rebase translates every static address, so a relocated client still works.")]
    public const uint DefaultImageBase = 0x00400000;

    /// <summary>
    /// Translates a published static address onto the module's real load address.
    /// A no-op in the normal case where the client loaded at <see cref="DefaultImageBase"/>.
    /// </summary>
    public static nint Rebase(uint publishedAddress, nint actualModuleBase) =>
        actualModuleBase + (nint)(publishedAddress - DefaultImageBase);

    /// <summary>
    /// Pointer chain roots: the object manager and the local player's identity.
    /// </summary>
    public static class ObjectManager
    {
        /// <summary>
        /// Static address holding the pointer to the client's connection object. The object
        /// manager hangs off it. This is the root of every object read the bot performs.
        /// </summary>
        [OffsetInfo(
            OffsetConfidence.Corroborated,
            "Two independent public 12340 tables agree (source A and source B, docs/offsets.md); matches the value cited in the project brief.",
            HowToVerify = "Dereference it: the result must be a readable pointer, and +CurMgr must itself be a readable pointer while a character is in the world. Both are asserted during attach.")]
        public const uint ClientConnection = 0x00C79CE0;

        /// <summary>Offset inside the connection object to the current object manager pointer.</summary>
        [OffsetInfo(
            OffsetConfidence.Corroborated,
            "Sources A and B agree; matches the brief.",
            HowToVerify = "Walking the resulting list must yield an object whose GUID equals the local player GUID at +LocalPlayerGuid.")]
        public const uint CurMgr = 0x2ED0;

        /// <summary>Offset inside the object manager to the head of the object linked list.</summary>
        [OffsetInfo(
            OffsetConfidence.Corroborated,
            "Sources A and B agree; matches the brief.",
            HowToVerify = "The walk terminates and produces objects whose type values are all within the legal 0-7 range.")]
        public const uint FirstObject = 0xAC;

        /// <summary>Offset inside an object to the next object in the manager's list.</summary>
        [OffsetInfo(
            OffsetConfidence.Corroborated,
            "Sources A and B agree; matches the brief.",
            HowToVerify = "Same as FirstObject: a wrong value produces an immediate walk failure or a cycle, both of which the enumerator detects.")]
        public const uint NextObject = 0x3C;

        /// <summary>Offset inside the object manager to the local player's GUID.</summary>
        [OffsetInfo(
            OffsetConfidence.Corroborated,
            "Sources A and B agree; matches the brief.",
            HowToVerify = "The GUID read here must match exactly one object of type Player in the enumerated list. Asserted during attach.")]
        public const uint LocalPlayerGuid = 0xC0;
    }

    /// <summary>
    /// Offsets common to every object in the manager's list, relative to the object's base.
    /// </summary>
    public static class Object
    {
        /// <summary>
        /// Pointer to the object's descriptor array: the block of 4-byte fields the server
        /// replicates to the client. Everything durable about an object (health, level,
        /// faction, flags) is read from there rather than from ad-hoc struct offsets,
        /// because the descriptor layout is defined by the protocol and is therefore the
        /// best-evidenced thing in this file.
        /// </summary>
        [OffsetInfo(
            OffsetConfidence.SingleSource,
            "Source A. Source B does not state it directly for units.",
            HowToVerify = "Self-validating and checked at attach: descriptor index OBJECT_FIELD_GUID (0) must contain the same 64-bit GUID as the object's own GUID field at +Guid. If the descriptor pointer is wrong those two cannot agree.")]
        public const uint Descriptors = 0x8;

        /// <summary>The object's type tag (see <c>WoWObjectType</c>).</summary>
        [OffsetInfo(
            OffsetConfidence.Corroborated,
            "Sources A and B agree.",
            HowToVerify = "Every enumerated object must report a value in 0-7. Asserted during attach.")]
        public const uint Type = 0x14;

        /// <summary>The object's 64-bit GUID, stored inline on the object.</summary>
        [OffsetInfo(
            OffsetConfidence.Corroborated,
            "Sources A and B agree.",
            HowToVerify = "Must equal descriptor field OBJECT_FIELD_GUID for the same object.")]
        public const uint Guid = 0x30;
    }

    /// <summary>
    /// Unit position and facing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The public sources disagree here</b>, and one of them disagrees with itself: the same
    /// project states 0x798 in one file and 0x9B8 in another. Rather than pick one and hope,
    /// both are treated as candidates and the correct one is determined against the running
    /// client at attach time (see <c>PositionResolver</c>): the local player's position is
    /// known to be a plausible world coordinate and to change when the character moves, which
    /// is enough to identify the right block unambiguously.
    /// </para>
    /// </remarks>
    public static class UnitPosition
    {
        /// <summary>
        /// Candidate layouts for the position block. Resolved at attach; never used directly.
        /// </summary>
        [OffsetInfo(
            OffsetConfidence.Conflicted,
            "Source A states 0x798 in one file and 0x9B8 in another; source B does not state it.",
            Conflicts = "0x798 (facing +0x10) vs 0x9B8 (facing +0xC)",
            HowToVerify = "Resolved at runtime by PositionResolver: the candidate must yield a finite in-bounds coordinate for the local player that also tracks movement. If neither candidate resolves, attach fails.")]
        public static readonly PositionLayout[] Candidates =
        [
            new PositionLayout(PositionBlock: 0x798, Facing: 0x7A8),
            new PositionLayout(PositionBlock: 0x9B8, Facing: 0x9C4),
        ];
    }

    /// <summary>
    /// One candidate layout of a unit's position block: three consecutive floats (X, Y, Z)
    /// at <paramref name="PositionBlock"/> and a facing radian at <paramref name="Facing"/>.
    /// </summary>
    public readonly record struct PositionLayout(uint PositionBlock, uint Facing)
    {
        public uint X => PositionBlock;

        public uint Y => PositionBlock + 4;

        public uint Z => PositionBlock + 8;

        public override string ToString() => $"pos=0x{PositionBlock:X}, facing=0x{Facing:X}";
    }

    /// <summary>
    /// The client's player-name cache, used to resolve player names without a Lua round trip.
    /// </summary>
    /// <remarks>
    /// Unit names for creatures come from a different structure; only player names live here.
    /// This is a hash table: mask and base are read from the store, then the bucket for a GUID
    /// is walked through <see cref="NodeNext"/> until the entry with the matching GUID is found.
    /// </remarks>
    public static class NameCache
    {
        /// <summary>Base of the name store structure.</summary>
        [OffsetInfo(
            OffsetConfidence.Corroborated,
            "Sources A and B agree, both expressed as 0xC5D938 + 0x8.",
            HowToVerify = "Looking up the local player's own GUID must return the character name shown in-game. Asserted by the inspector tool.")]
        public const uint Store = 0x00C5D938 + 0x8;

        /// <summary>Offset in the store to the hash mask.</summary>
        [OffsetInfo(OffsetConfidence.Corroborated, "Sources A and B agree.",
            HowToVerify = "A sane mask is a small power-of-two-minus-one; a wrong read produces an absurd value and the lookup is skipped.")]
        public const uint Mask = 0x24;

        /// <summary>Offset in the store to the bucket array base.</summary>
        [OffsetInfo(OffsetConfidence.Corroborated, "Sources A and B agree.", HowToVerify = "See Store.")]
        public const uint Base = 0x1C;

        /// <summary>Offset in a name node to the null-terminated name string.</summary>
        [OffsetInfo(OffsetConfidence.Corroborated, "Sources A and B agree.", HowToVerify = "See Store.")]
        public const uint NodeName = 0x20;

        /// <summary>Offset in a name node to the next node in its bucket.</summary>
        [OffsetInfo(
            OffsetConfidence.SingleSource,
            "Source A only; source B does not state it.",
            HowToVerify = "Bucket walks are bounded and abandoned on an implausible pointer, so a wrong value degrades to 'name unavailable' rather than a bad read.")]
        public const uint NodeNext = 0xC;

        /// <summary>
        /// Offset in a name node to the low 32 bits of the entry's GUID, used to pick the
        /// matching node out of a bucket chain.
        /// </summary>
        [OffsetInfo(
            OffsetConfidence.Unverified,
            "No source. TODO: verify.",
            HowToVerify = "Attach the inspector, dump 0x40 bytes at a bucket's first node, and look for the local player's low GUID dword. The name string at NodeName gives a known-good anchor to dump from.")]
        public const uint NodeGuid = 0x0;

        /// <summary>
        /// Stride in bytes between entries of the bucket index.
        /// </summary>
        [OffsetInfo(
            OffsetConfidence.Unverified,
            "No source. TODO: verify.",
            HowToVerify = "Dump the bucket array and measure the spacing between successive non-null node pointers. Until then the name cache is advisory: a wrong stride yields no name rather than a wrong name, because the GUID in the node must still match.")]
        public const uint BucketStride = 12;
    }

    /// <summary>
    /// Static addresses describing the client's overall state. Used to tell 'sitting at the
    /// character screen' apart from 'in the world', which decides whether the object manager
    /// is safe to walk at all.
    /// </summary>
    public static class ClientState
    {
        /// <summary>Non-zero while the player is in the world.</summary>
        [OffsetInfo(
            OffsetConfidence.SingleSource,
            "Source B only.",
            HowToVerify = "Cross-checked against the object manager: 'in world' must coincide with a resolvable local player object. Attach relies on the object-manager check, not on this flag alone.")]
        public const uint IsInWorld = 0x00BD0792;

        /// <summary>Non-zero while a loading screen is up.</summary>
        [OffsetInfo(OffsetConfidence.SingleSource, "Source B only.",
            HowToVerify = "Should be non-zero exactly while zoning. Advisory only.")]
        public const uint IsLoadingScreen = 0x00B6AA38;

        /// <summary>Current map (continent) id.</summary>
        [OffsetInfo(OffsetConfidence.SingleSource, "Source B only.",
            HowToVerify = "Compare against the known map ids: 0 Eastern Kingdoms, 1 Kalimdor, 530 Outland, 571 Northrend. The inspector prints it so it can be eyeballed against the character's actual location.")]
        public const uint MapId = 0x00AB63BC;

        /// <summary>Current zone id.</summary>
        [OffsetInfo(OffsetConfidence.SingleSource, "Source B only.",
            HowToVerify = "Compare with the zone shown in-game via the inspector.")]
        public const uint ZoneId = 0x00BD080C;
    }

    /// <summary>
    /// Addresses needed from phase 2 onward. Recorded here now, with their provenance, so
    /// that the later phases start from evidence rather than from memory.
    /// </summary>
    /// <remarks>
    /// None of these are used yet. They are listed because the same two sources that supplied
    /// the phase 1 anchors also supplied these, and capturing the corroboration while it is in
    /// hand is cheaper than re-deriving it later.
    /// </remarks>
    public static class FutureUse
    {
        /// <summary>
        /// <c>FrameScript_Execute(const char* code, const char* source, int unused)</c>.
        /// The unprotected Lua entry point. Phase 2.
        /// </summary>
        [OffsetInfo(
            OffsetConfidence.Corroborated,
            "Sources A and B agree (A names it FRAMESCRIPT_EXECUTE, B names it luaDoString).",
            HowToVerify = "Phase 2: call it with a harmless script such as a DEFAULT_CHAT_FRAME message and confirm the message appears in-game.")]
        public const uint FrameScriptExecute = 0x00819210;

        /// <summary>First hop of the Direct3D device pointer chain. Phase 2 EndScene hook.</summary>
        [OffsetInfo(OffsetConfidence.Corroborated, "Sources A and B agree.",
            HowToVerify = "Phase 2: the resolved vtable entry must lie inside d3d9.dll's address range.")]
        public const uint D3DDevicePointer1 = 0x00C5DF88;

        /// <summary>Second hop of the Direct3D device pointer chain.</summary>
        [OffsetInfo(OffsetConfidence.Corroborated, "Sources A and B agree.", HowToVerify = "See D3DDevicePointer1.")]
        public const uint D3DDevicePointer2 = 0x397C;

        /// <summary>Byte offset of EndScene in the device vtable (entry 42).</summary>
        [OffsetInfo(OffsetConfidence.Corroborated, "Sources A and B agree.", HowToVerify = "See D3DDevicePointer1.")]
        public const uint D3DEndSceneVTableOffset = 0xA8;

        /// <summary>Base of the click-to-move block. Phase 3 movement.</summary>
        [OffsetInfo(
            OffsetConfidence.SingleSource,
            "Source B only.",
            HowToVerify = "Phase 3: read the block while manually right-click-moving in-game; the destination floats must match the clicked location before anything is written.")]
        public const uint ClickToMoveBase = 0x00CA11D8;

        /// <summary>Offset in the CTM block to the action code.</summary>
        [OffsetInfo(OffsetConfidence.SingleSource, "Source B only.", HowToVerify = "See ClickToMoveBase.")]
        public const uint ClickToMoveAction = 0x1C;

        /// <summary>Offset in the CTM block to the interacted GUID.</summary>
        [OffsetInfo(OffsetConfidence.SingleSource, "Source B only.", HowToVerify = "See ClickToMoveBase.")]
        public const uint ClickToMoveGuid = 0x20;

        /// <summary>Offset in the CTM block to the destination X (Y and Z follow).</summary>
        [OffsetInfo(OffsetConfidence.SingleSource, "Source B only.", HowToVerify = "See ClickToMoveBase.")]
        public const uint ClickToMoveDestinationX = 0x8C;

        /// <summary>Offset in the CTM block to the stop distance.</summary>
        [OffsetInfo(OffsetConfidence.SingleSource, "Source B only.", HowToVerify = "See ClickToMoveBase.")]
        public const uint ClickToMoveDistance = 0xC;
    }
}
