using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;
using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.GameApi.Objects;

/// <summary>
/// A world object: a herb or ore node, a chest, a door, a mailbox, a transport.
/// </summary>
public sealed class WoWGameObject : WoWObject
{
    private readonly uint? _positionOffset;

    internal WoWGameObject(
        GameObjectRef reference,
        Offsets335a.PositionLayout positionLayout,
        uint? positionOffset)
        : base(reference, positionLayout)
    {
        _positionOffset = positionOffset;
    }

    /// <summary>
    /// Where the object is, or <see cref="Vector3.Zero"/> when that could not be established.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Game objects do not use the unit position block, and no source consulted for this
    /// project states where a 12340 game object keeps its coordinates. Rather than guess, the
    /// offset is worked out against the running client during attach — see
    /// <c>GameObjectPositionResolver</c> — and that result is what this reads through.
    /// </para>
    /// <para>
    /// When resolution failed this stays zero and <see cref="HasKnownPosition"/> is false.
    /// Callers must check rather than walking to it: a fabricated coordinate is precisely
    /// the failure this project exists to avoid.
    /// </para>
    /// </remarks>
    public override Vector3 Position =>
        _positionOffset is { } offset
        && Memory.TryReadVector3(Address + (nint)offset, out Vector3 position)
        && WorldBounds.IsPlausible(position)
            ? position
            : Vector3.Zero;

    /// <summary>True when this object's position is known and usable.</summary>
    public bool HasKnownPosition => _positionOffset is not null && !Position.IsZero;

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
