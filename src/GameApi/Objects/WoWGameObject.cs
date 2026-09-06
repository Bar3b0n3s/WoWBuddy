using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;
using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.GameApi.Objects;

/// <summary>
/// A world object: a herb or ore node, a chest, a door, a mailbox, a transport.
/// </summary>
public sealed class WoWGameObject : WoWObject
{
    internal WoWGameObject(GameObjectRef reference, Offsets335a.PositionLayout positionLayout)
        : base(reference, positionLayout)
    {
    }

    /// <summary>
    /// Always <see cref="Vector3.Zero"/>. Game object positions are <b>not yet supported</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TODO: verify. Game objects do not use the unit position block, and no source reachable
    /// during this work states where a 12340 game object stores its coordinates. Returning
    /// zero is deliberate: a fabricated offset here would send the gathering bot walking to
    /// nonsense coordinates, which is exactly the failure this project is trying to avoid.
    /// </para>
    /// <para>
    /// To find it: attach the inspector to a client standing next to a known node, dump the
    /// object's memory, and search for three consecutive floats matching the player's own
    /// position to within a few yards. Add the result to <c>Offsets335a</c> as a candidate
    /// and extend the attach-time resolver to confirm it the same way unit positions are
    /// confirmed. This is a prerequisite for the phase 6 gathering base.
    /// </para>
    /// </remarks>
    public override Vector3 Position => Vector3.Zero;

    /// <summary>True once game object positions are supported. Currently always false.</summary>
    public bool HasKnownPosition => false;

    /// <summary>Display (model) id, which distinguishes one node type from another.</summary>
    public uint DisplayId => Descriptors.ReadUInt32(UpdateFields335a.GameObject.DisplayId);

    /// <summary>Game object flags.</summary>
    public uint Flags => Descriptors.ReadUInt32(UpdateFields335a.GameObject.Flags);

    /// <summary>State byte: whether a door or chest is open or closed.</summary>
    public byte State => Descriptors.ReadByte(UpdateFields335a.GameObject.Bytes1, 0);

    /// <summary>The kind of game object this is, from the packed bytes field.</summary>
    public byte GameObjectType => Descriptors.ReadByte(UpdateFields335a.GameObject.Bytes1, 1);

    public override string ToString() => $"GameObject entry {Entry} display {DisplayId}";
}
