namespace WoWBuddy.Core.Memory;

/// <summary>
/// Read/write access to the attached client, plus the ability to allocate memory in it.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IMemoryReader"/> so that the read-only half of the bot cannot
/// accidentally acquire the ability to write. Phase 1 is entirely read-only and takes
/// <see cref="IMemoryReader"/>; only the execution layer added in phase 2 asks for this.
/// The distinction is worth keeping: reading a wrong address is harmless, and writing one
/// crashes somebody's game.
/// </para>
/// </remarks>
public interface IProcessMemory : IMemoryReader
{
    /// <summary>
    /// Writes <paramref name="buffer"/> at <paramref name="address"/>.
    /// Returns false if the write failed or was short.
    /// </summary>
    bool TryWriteBytes(nint address, ReadOnlySpan<byte> buffer);

    /// <summary>
    /// Allocates <paramref name="size"/> bytes in the client.
    /// </summary>
    /// <param name="executable">
    /// Whether the pages must be executable. Only the code cave needs this; the command
    /// block does not, and asking for it anyway would leave a needless RWX page in the
    /// client for the lifetime of the session.
    /// </param>
    /// <returns>The address, or 0 on failure.</returns>
    nint Allocate(int size, bool executable);

    /// <summary>Releases memory previously returned by <see cref="Allocate"/>.</summary>
    bool Free(nint address);

    /// <summary>
    /// Makes a range writable, runs <paramref name="action"/>, and restores the original
    /// protection.
    /// </summary>
    /// <remarks>
    /// Needed to patch the Direct3D vtable, which lives on a read-only page inside d3d9.dll.
    /// The original protection is always restored, including when the action throws, so a
    /// failure part-way through cannot leave a writable page behind in the client.
    /// </remarks>
    bool WithWritableMemory(nint address, int size, Action action);
}
