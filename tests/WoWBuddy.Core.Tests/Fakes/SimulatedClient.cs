using System.Buffers.Binary;
using System.Text;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;

namespace WoWBuddy.Core.Tests.Fakes;

/// <summary>
/// A fake 3.3.5a client laid out in memory exactly as the real offset table describes.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes phase 1 testable without the game. The layout is built using the very
/// constants in <see cref="Offsets335a"/> that the production code reads, so a test that
/// walks this client is genuinely exercising the pointer chain, the list walk, the descriptor
/// decoding and the verifier, rather than a parallel reimplementation of them.
/// </para>
/// <para>
/// What it deliberately cannot prove is that the offsets themselves are the ones the real
/// client uses. Nothing running away from a copy of the game can prove that; that is what the
/// attach-time verification and the manual test script in <c>docs/phase-1-manual-test.md</c>
/// are for.
/// </para>
/// </remarks>
public sealed class SimulatedClient : IMemoryReader
{
    /// <summary>Where the fake module is loaded. The same base the real client uses.</summary>
    public const uint ImageBase = 0x00400000;

    /// <summary>Start of the fake heap that objects are allocated from.</summary>
    private const uint HeapBase = 0x10000000;

    private readonly Dictionary<nint, byte> _memory = [];
    private uint _nextAllocation = HeapBase;

    /// <summary>Address of the client-connection pointer, after rebasing.</summary>
    public nint ClientConnectionAddress => Offsets335a.Rebase(Offsets335a.ObjectManager.ClientConnection, ModuleBase);

    /// <inheritdoc />
    public bool IsValid { get; set; } = true;

    /// <inheritdoc />
    public nint ModuleBase { get; init; } = (nint)ImageBase;

    /// <inheritdoc />
    public int ModuleSize { get; init; } = 0x00800000;

    /// <summary>Address of the object manager structure, once one has been created.</summary>
    public nint ManagerAddress { get; private set; }

    /// <summary>The position layout this fake client uses. Defaults to the first candidate.</summary>
    public Offsets335a.PositionLayout PositionLayout { get; set; } = Offsets335a.UnitPosition.Candidates[0];

    /// <summary>Every object address handed out, in creation order.</summary>
    public List<nint> ObjectAddresses { get; } = [];

    /// <inheritdoc />
    public bool TryReadBytes(nint address, Span<byte> buffer)
    {
        if (!IsValid)
        {
            return false;
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            if (!_memory.TryGetValue(address + i, out byte value))
            {
                return false;
            }

            buffer[i] = value;
        }

        return true;
    }

    /// <summary>Reserves <paramref name="size"/> bytes of zeroed fake memory.</summary>
    public nint Allocate(int size)
    {
        nint address = (nint)_nextAllocation;
        for (int i = 0; i < size; i++)
        {
            _memory[address + i] = 0;
        }

        // Keep allocations apart so that an overrun in the code under test reads unmapped
        // memory and fails loudly, instead of silently running into the next object.
        _nextAllocation += (uint)(size + 0x1000);
        return address;
    }

    /// <summary>Writes a raw byte.</summary>
    public void WriteByte(nint address, byte value) => _memory[address] = value;

    /// <summary>Writes a 32-bit value.</summary>
    public void WriteUInt32(nint address, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        Write(address, buffer);
    }

    /// <summary>Writes a 32-bit client pointer.</summary>
    public void WritePointer(nint address, nint value) => WriteUInt32(address, (uint)value);

    /// <summary>Writes a 64-bit value.</summary>
    public void WriteUInt64(nint address, ulong value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        Write(address, buffer);
    }

    /// <summary>Writes a single-precision float.</summary>
    public void WriteSingle(nint address, float value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        Write(address, buffer);
    }

    /// <summary>Writes a position as three consecutive floats.</summary>
    public void WriteVector3(nint address, Vector3 value)
    {
        WriteSingle(address, value.X);
        WriteSingle(address + 4, value.Y);
        WriteSingle(address + 8, value.Z);
    }

    /// <summary>Writes a null-terminated string.</summary>
    public void WriteCString(nint address, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Write(address, bytes);
        WriteByte(address + bytes.Length, 0);
    }

    /// <summary>Unmaps a range so that reads from it fail, as they do for a freed object.</summary>
    public void Unmap(nint address, int size)
    {
        for (int i = 0; i < size; i++)
        {
            _memory.Remove(address + i);
        }
    }

    private void Write(nint address, ReadOnlySpan<byte> bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            _memory[address + i] = bytes[i];
        }
    }
}
