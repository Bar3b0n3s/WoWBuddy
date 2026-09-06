using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Execution;
using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;
using WoWBuddy.Core.Tests.Fakes;
using Xunit;

namespace WoWBuddy.Core.Tests;

/// <summary>
/// Covers the block that will steer the character from phase 3.
/// </summary>
/// <remarks>
/// Nothing writes here in normal operation yet. The tests exist because the offsets are
/// single-sourced and the write ordering is the part that matters: the destination has to be
/// in place before the action code that acts on it.
/// </remarks>
public sealed class ClickToMoveWriterTests
{
    private static readonly Vector3 Somewhere = new(-8913.23f, 554.63f, 93.79f);

    private static SimulatedClient NewClient()
    {
        var client = new SimulatedClient();
        nint ctm = Offsets335a.Rebase(Offsets335a.ClickToMove.Base, client.ModuleBase);
        for (nint i = 0; i < 0x100; i++)
        {
            client.WriteByte(ctm + i, 0);
        }

        return client;
    }

    [Fact]
    public void ReadReturnsWhatTheClientCurrentlyHolds()
    {
        // The read path is how the single-sourced offsets get confirmed against a real
        // client, so it has to be right before anything writes.
        SimulatedClient client = NewClient();
        var writer = new ClickToMoveWriter(client);
        nint ctm = writer.BaseAddress;

        client.WriteUInt32(ctm + (nint)Offsets335a.ClickToMove.Action, (uint)ClickToMoveAction.Move);
        client.WriteVector3(ctm + (nint)Offsets335a.ClickToMove.DestinationX, Somewhere);
        client.WriteUInt64(ctm + (nint)Offsets335a.ClickToMove.InteractGuid, 0xF130000000001234);
        client.WriteSingle(ctm + (nint)Offsets335a.ClickToMove.StopDistance, 1.5f);

        ClickToMoveState state = writer.Read();

        Assert.Equal(ClickToMoveAction.Move, state.Action);
        Assert.Equal(Somewhere.X, state.Destination.X, 2);
        Assert.Equal(Somewhere.Y, state.Destination.Y, 2);
        Assert.Equal(Somewhere.Z, state.Destination.Z, 2);
        Assert.Equal(new WoWGuid(0xF130000000001234), state.InteractGuid);
        Assert.Equal(1.5f, state.StopDistance);
    }

    [Fact]
    public void WriteRoundTripsThroughRead()
    {
        SimulatedClient client = NewClient();
        var writer = new ClickToMoveWriter(client);
        var guid = new WoWGuid(0xF130000000005678);

        Assert.True(writer.Write(ClickToMoveAction.Interact, Somewhere, guid, stopDistance: 2.5f));

        ClickToMoveState state = writer.Read();

        Assert.Equal(ClickToMoveAction.Interact, state.Action);
        Assert.Equal(guid, state.InteractGuid);
        Assert.Equal(2.5f, state.StopDistance);
        Assert.Equal(Somewhere.X, state.Destination.X, 2);
    }

    [Fact]
    public void ImpossibleDestinationsAreRefused()
    {
        // Walking a character to a coordinate that cannot exist is the exact failure this
        // project is built to avoid, and it costs one comparison to refuse.
        SimulatedClient client = NewClient();
        var writer = new ClickToMoveWriter(client);

        Assert.False(writer.Write(ClickToMoveAction.Move, new Vector3(999999f, 0f, 0f)));
        Assert.False(writer.Write(ClickToMoveAction.Move, new Vector3(float.NaN, 0f, 0f)));
        Assert.False(writer.Write(ClickToMoveAction.Move, Vector3.Zero));

        Assert.Equal(default, writer.Read().Action);
    }

    [Fact]
    public void StopDoesNotNeedAPlausibleDestination()
    {
        SimulatedClient client = NewClient();
        var writer = new ClickToMoveWriter(client);

        Assert.True(writer.Stop());
        Assert.Equal(ClickToMoveAction.Stop, writer.Read().Action);
    }

    [Fact]
    public void TheActionCodeIsWrittenLastSoTheClientNeverSeesAStaleDestination()
    {
        // If the action landed first, a frame arriving between the writes would send the
        // character to wherever it was going previously.
        SimulatedClient client = NewClient();
        var writer = new ClickToMoveWriter(client);

        var order = new List<string>();
        var recorder = new WriteOrderRecorder(client, writer.BaseAddress, order);
        var recordingWriter = new ClickToMoveWriter(recorder);

        recordingWriter.Write(ClickToMoveAction.Move, Somewhere);

        Assert.Equal("action", order[^1]);
        Assert.Contains("destination", order);
    }

    /// <summary>Records which fields of the block are written, and in what order.</summary>
    private sealed class WriteOrderRecorder(SimulatedClient inner, nint ctmBase, List<string> order) : IProcessMemory
    {
        public bool IsValid => inner.IsValid;

        public nint ModuleBase => inner.ModuleBase;

        public int ModuleSize => inner.ModuleSize;

        public bool TryReadBytes(nint address, Span<byte> buffer) => inner.TryReadBytes(address, buffer);

        public bool TryWriteBytes(nint address, ReadOnlySpan<byte> buffer)
        {
            nint offset = address - ctmBase;
            order.Add(offset switch
            {
                _ when offset == Offsets335a.ClickToMove.Action => "action",
                _ when offset == Offsets335a.ClickToMove.DestinationX => "destination",
                _ when offset == Offsets335a.ClickToMove.InteractGuid => "guid",
                _ when offset == Offsets335a.ClickToMove.StopDistance => "distance",
                _ => $"other+0x{offset:X}",
            });

            return inner.TryWriteBytes(address, buffer);
        }

        public nint Allocate(int size, bool executable) => inner.Allocate(size, executable);

        public bool Free(nint address) => inner.Free(address);

        public bool WithWritableMemory(nint address, int size, Action action) =>
            inner.WithWritableMemory(address, size, action);
    }
}
