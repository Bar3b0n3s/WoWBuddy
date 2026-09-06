using System.Buffers.Binary;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;

namespace WoWBuddy.Core.Execution;

/// <summary>The click-to-move block as the client currently holds it.</summary>
/// <param name="Action">What the client is currently being asked to do.</param>
/// <param name="Destination">Where it is heading.</param>
/// <param name="InteractGuid">What it will interact with on arrival, if anything.</param>
/// <param name="StopDistance">How close it will get before stopping.</param>
public readonly record struct ClickToMoveState(
    ClickToMoveAction Action,
    Vector3 Destination,
    WoWGuid InteractGuid,
    float StopDistance);

/// <summary>
/// Reads and writes the client's click-to-move block.
/// </summary>
/// <remarks>
/// <para>
/// Movement is not done by pressing keys. The client already knows how to walk somewhere:
/// right-clicking the ground fills in a destination and an action code and lets its own
/// pathing take over. Writing the same block does the same thing, which means the movement
/// the server sees is the movement the client would have produced anyway.
/// </para>
/// <para>
/// <b>Nothing writes here yet.</b> Phase 3 owns movement; this exists now because the
/// offsets are single-sourced and <see cref="Read"/> is how they get confirmed. Reading the
/// block while moving normally in game, and seeing the destination match where you clicked,
/// is the evidence that has to come before anything writes to it.
/// </para>
/// </remarks>
public sealed class ClickToMoveWriter
{
    private readonly IProcessMemory _memory;
    private readonly nint _base;

    public ClickToMoveWriter(IProcessMemory memory)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _base = Offsets335a.Rebase(Offsets335a.ClickToMove.Base, memory.ModuleBase);
    }

    /// <summary>Base address of the block in the client.</summary>
    public nint BaseAddress => _base;

    /// <summary>
    /// Reads the block without changing it.
    /// </summary>
    /// <remarks>
    /// The verification tool for these offsets. Move normally in game and read this: the
    /// destination must match where you clicked, and the action code must change as you
    /// walk, interact and stop.
    /// </remarks>
    public ClickToMoveState Read()
    {
        uint action = _memory.ReadOrDefault<uint>(_base + (nint)Offsets335a.ClickToMove.Action);
        ulong guid = _memory.ReadOrDefault<ulong>(_base + (nint)Offsets335a.ClickToMove.InteractGuid);
        float distance = _memory.ReadOrDefault<float>(_base + (nint)Offsets335a.ClickToMove.StopDistance);

        _memory.TryReadVector3(_base + (nint)Offsets335a.ClickToMove.DestinationX, out Vector3 destination);

        return new ClickToMoveState((ClickToMoveAction)action, destination, new WoWGuid(guid), distance);
    }

    /// <summary>
    /// Writes a destination and action into the block.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Order matters: the destination, GUID and stop distance all go in before the action
    /// code, because the action code is what the client acts on. Writing it first would let
    /// a frame land between the writes and send the character to the previous destination.
    /// </para>
    /// <para>
    /// The destination is checked against the world grid first. Walking a character to a
    /// coordinate that cannot exist is the failure mode this whole project is trying to avoid,
    /// and it costs one comparison to refuse.
    /// </para>
    /// </remarks>
    public bool Write(
        ClickToMoveAction action,
        Vector3 destination,
        WoWGuid interactGuid = default,
        float stopDistance = 0.5f)
    {
        if (!WorldBounds.IsPlausible(destination) && action is not (ClickToMoveAction.Stop or ClickToMoveAction.FaceTarget))
        {
            Log.For<ClickToMoveWriter>().Error(
                "Refusing to write click-to-move destination {Destination}: it is not a possible world position.",
                destination);
            return false;
        }

        Span<byte> position = stackalloc byte[12];
        BinaryPrimitives.WriteSingleLittleEndian(position, destination.X);
        BinaryPrimitives.WriteSingleLittleEndian(position[4..], destination.Y);
        BinaryPrimitives.WriteSingleLittleEndian(position[8..], destination.Z);

        Span<byte> guid = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(guid, interactGuid.Value);

        Span<byte> scalar = stackalloc byte[4];

        bool ok = _memory.TryWriteBytes(_base + (nint)Offsets335a.ClickToMove.DestinationX, position)
            && _memory.TryWriteBytes(_base + (nint)Offsets335a.ClickToMove.InteractGuid, guid);

        BinaryPrimitives.WriteSingleLittleEndian(scalar, stopDistance);
        ok &= _memory.TryWriteBytes(_base + (nint)Offsets335a.ClickToMove.StopDistance, scalar);

        // Last, so the client never sees a new action with a stale destination.
        BinaryPrimitives.WriteUInt32LittleEndian(scalar, (uint)action);
        ok &= _memory.TryWriteBytes(_base + (nint)Offsets335a.ClickToMove.Action, scalar);

        return ok;
    }

    /// <summary>Tells the client to stop moving.</summary>
    public bool Stop() => Write(ClickToMoveAction.Stop, Vector3.Zero);
}
