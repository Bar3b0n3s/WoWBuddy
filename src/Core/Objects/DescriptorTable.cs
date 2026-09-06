using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Offsets;

namespace WoWBuddy.Core.Objects;

/// <summary>
/// Typed access to one object's descriptor array.
/// </summary>
/// <remarks>
/// <para>
/// The descriptor array is the block of 4-byte fields the server replicates to the client.
/// Reading state from here rather than from ad-hoc struct offsets is a deliberate choice:
/// descriptor indices are defined by the wire protocol and are therefore the best-evidenced
/// facts available, whereas struct offsets are whatever the compiler happened to produce and
/// have to be rediscovered by disassembly.
/// </para>
/// <para>
/// This is a struct holding a base address, not a cache. Descriptor values change constantly
/// and a stale health value is worse than no health value, so every read goes to the client.
/// </para>
/// </remarks>
public readonly struct DescriptorTable
{
    private readonly IMemoryReader _reader;

    /// <summary>Creates a view over the descriptor array at <paramref name="baseAddress"/>.</summary>
    public DescriptorTable(IMemoryReader reader, nint baseAddress)
    {
        _reader = reader;
        BaseAddress = baseAddress;
    }

    /// <summary>Address of field index 0.</summary>
    public nint BaseAddress { get; }

    /// <summary>True when this view points somewhere plausible.</summary>
    public bool IsValid => _reader is not null && BaseAddress > 0x1000;

    /// <summary>Address of the field at <paramref name="index"/>.</summary>
    public nint AddressOf(uint index) => BaseAddress + (nint)UpdateFields335a.ByteOffset(index);

    /// <summary>Reads a 32-bit signed field.</summary>
    public int ReadInt32(uint index) => _reader.ReadOrDefault<int>(AddressOf(index));

    /// <summary>Reads a 32-bit unsigned field.</summary>
    public uint ReadUInt32(uint index) => _reader.ReadOrDefault<uint>(AddressOf(index));

    /// <summary>Reads a single-precision field.</summary>
    public float ReadSingle(uint index) => _reader.ReadOrDefault<float>(AddressOf(index));

    /// <summary>
    /// Reads a 64-bit GUID, which occupies this index and the next one, low dword first.
    /// </summary>
    public WoWGuid ReadGuid(uint index) => new(_reader.ReadOrDefault<ulong>(AddressOf(index)));

    /// <summary>Reads one byte out of a packed four-byte field.</summary>
    /// <param name="index">The field index.</param>
    /// <param name="byteIndex">Which byte of the field, 0 to 3.</param>
    public byte ReadByte(uint index, int byteIndex)
    {
        if ((uint)byteIndex > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(byteIndex), byteIndex, "Packed fields are four bytes wide.");
        }

        return _reader.ReadOrDefault<byte>(AddressOf(index) + byteIndex);
    }

    /// <summary>Reads a field and reports whether the read succeeded.</summary>
    public bool TryReadUInt32(uint index, out uint value) =>
        _reader.TryRead(AddressOf(index), out value);
}
