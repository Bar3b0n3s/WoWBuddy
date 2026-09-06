using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using WoWBuddy.Common.Geometry;

namespace WoWBuddy.Core.Memory;

/// <summary>
/// Typed reads on top of <see cref="IMemoryReader"/>.
/// </summary>
/// <remarks>
/// The client is 32-bit and little-endian, so a pointer is always exactly four bytes no
/// matter what the bot process itself is. <see cref="TryReadPointer"/> is explicit about
/// that: reading a client pointer as a native <c>nint</c> would silently read eight bytes
/// if the bot were ever built for x64.
/// </remarks>
public static class MemoryReaderExtensions
{
    /// <summary>Reads an unmanaged value of type <typeparamref name="T"/>.</summary>
    public static bool TryRead<T>(this IMemoryReader reader, nint address, out T value)
        where T : unmanaged
    {
        Unsafe.SkipInit(out value);
        Span<byte> buffer = stackalloc byte[Unsafe.SizeOf<T>()];
        if (!reader.TryReadBytes(address, buffer))
        {
            value = default;
            return false;
        }

        value = MemoryMarshal.Read<T>(buffer);
        return true;
    }

    /// <summary>Reads an unmanaged value, returning <paramref name="fallback"/> on failure.</summary>
    public static T ReadOrDefault<T>(this IMemoryReader reader, nint address, T fallback = default)
        where T : unmanaged
        => reader.TryRead(address, out T value) ? value : fallback;

    /// <summary>
    /// Reads a 32-bit pointer from the client and widens it to a native integer.
    /// </summary>
    /// <remarks>
    /// Always four bytes, because the client is always 32-bit. A zero pointer is reported as
    /// a successful read of zero rather than as a failure: null is a legitimate value all
    /// over the client's structures, and callers check for it themselves.
    /// </remarks>
    public static bool TryReadPointer(this IMemoryReader reader, nint address, out nint pointer)
    {
        Span<byte> buffer = stackalloc byte[4];
        if (!reader.TryReadBytes(address, buffer))
        {
            pointer = 0;
            return false;
        }

        pointer = (nint)BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        return true;
    }

    /// <summary>Reads a 32-bit pointer, returning 0 on failure.</summary>
    public static nint ReadPointerOrZero(this IMemoryReader reader, nint address)
        => reader.TryReadPointer(address, out nint pointer) ? pointer : 0;

    /// <summary>
    /// Follows a chain of 32-bit pointers, adding each offset in turn.
    /// </summary>
    /// <remarks>
    /// The final offset is added without a dereference, matching how pointer chains are
    /// conventionally written: <c>[[base + o1] + o2] + o3</c>.
    /// </remarks>
    public static bool TryFollowChain(this IMemoryReader reader, nint start, out nint result, params uint[] offsets)
    {
        ArgumentNullException.ThrowIfNull(offsets);

        nint current = start;
        for (int i = 0; i < offsets.Length; i++)
        {
            if (i == offsets.Length - 1)
            {
                result = current + (nint)offsets[i];
                return true;
            }

            if (!reader.TryReadPointer(current + (nint)offsets[i], out current) || current == 0)
            {
                result = 0;
                return false;
            }
        }

        result = current;
        return true;
    }

    /// <summary>Reads three consecutive little-endian floats as a position.</summary>
    public static bool TryReadVector3(this IMemoryReader reader, nint address, out Vector3 position)
    {
        Span<byte> buffer = stackalloc byte[12];
        if (!reader.TryReadBytes(address, buffer))
        {
            position = Vector3.Zero;
            return false;
        }

        position = new Vector3(
            BinaryPrimitives.ReadSingleLittleEndian(buffer),
            BinaryPrimitives.ReadSingleLittleEndian(buffer[4..]),
            BinaryPrimitives.ReadSingleLittleEndian(buffer[8..]));
        return true;
    }

    /// <summary>
    /// Reads a null-terminated single-byte string.
    /// </summary>
    /// <remarks>
    /// The client stores names in its own locale's code page. UTF-8 decodes plain ASCII
    /// names correctly, which covers every English realm; accented names on other locales
    /// may decode imperfectly. That is cosmetic, and names are never used as identifiers,
    /// only GUIDs are.
    /// </remarks>
    public static bool TryReadCString(this IMemoryReader reader, nint address, out string value, int maxLength = 128)
    {
        Span<byte> buffer = stackalloc byte[Math.Clamp(maxLength, 1, 512)];
        if (!reader.TryReadBytes(address, buffer))
        {
            value = string.Empty;
            return false;
        }

        int end = buffer.IndexOf((byte)0);
        if (end < 0)
        {
            end = buffer.Length;
        }

        value = end == 0 ? string.Empty : Encoding.UTF8.GetString(buffer[..end]);
        return true;
    }
}
