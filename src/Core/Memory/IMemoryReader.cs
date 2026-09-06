namespace WoWBuddy.Core.Memory;

/// <summary>
/// Reads memory out of the attached game client.
/// </summary>
/// <remarks>
/// <para>
/// An interface rather than a concrete class for two reasons. First, it lets the object
/// manager, descriptor decoding and offset resolution be tested against a simulated client
/// with a known layout, which is the only way any of this is verifiable without a running
/// copy of the game. Second, it keeps the boundary where the bot touches another process
/// small and obvious.
/// </para>
/// <para>
/// Implementations must never throw for an unreadable address. Reading freed or unmapped
/// memory is completely routine here: objects are destroyed by the client while the bot is
/// walking the list. Callers get <c>false</c> and are expected to skip that object.
/// </para>
/// </remarks>
public interface IMemoryReader
{
    /// <summary>True while the target process is alive and readable.</summary>
    bool IsValid { get; }

    /// <summary>Base address the client's main module is loaded at.</summary>
    nint ModuleBase { get; }

    /// <summary>Size in bytes of the client's main module.</summary>
    int ModuleSize { get; }

    /// <summary>
    /// Reads <paramref name="buffer"/>.Length bytes from <paramref name="address"/>.
    /// Returns false if the read failed or was short; the buffer contents are then undefined.
    /// </summary>
    bool TryReadBytes(nint address, Span<byte> buffer);
}
