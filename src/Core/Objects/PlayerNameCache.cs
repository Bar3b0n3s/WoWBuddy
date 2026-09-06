using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Offsets;

namespace WoWBuddy.Core.Objects;

/// <summary>
/// Resolves player names from the client's own name cache.
/// </summary>
/// <remarks>
/// <para>
/// The client keeps a hash table of the player names it has seen, so a name can be looked up
/// by GUID without a Lua round trip. That matters because phase 1 has no way to execute Lua
/// at all, and because even later it is far cheaper to read a name than to marshal a call
/// onto the game thread.
/// </para>
/// <para>
/// Only player names live here. Creature names come from a different structure and are left
/// to phase 2, where the client's own name-getter can be called directly.
/// </para>
/// <para>
/// <b>Two parts of this structure are unverified</b>: the stride between bucket-index entries
/// and the position of the GUID inside a node (see <c>Offsets335a.NameCache.BucketStride</c>
/// and <c>NodeGuid</c>, both marked <see cref="OffsetConfidence.Unverified"/>). They are
/// documented assumptions, not facts, which is why this class is advisory. It can only ever
/// return a name whose GUID it has already matched, so a wrong stride produces <em>no</em>
/// name rather than the <em>wrong</em> name, and nothing the bot decides is keyed on a name.
/// </para>
/// <para>
/// Every other failure mode degrades the same way. The bucket walk is bounded, implausible
/// pointers end it, and an absurd hash mask skips the lookup entirely.
/// </para>
/// </remarks>
public sealed class PlayerNameCache
{
    /// <summary>
    /// Longest bucket chain that will be walked before giving up.
    /// </summary>
    /// <remarks>
    /// A healthy hash table has chains of one or two. Anything remotely near this bound means
    /// the structure is not what the offsets claim.
    /// </remarks>
    private const int MaxChainLength = 64;

    /// <summary>
    /// Largest hash mask that will be believed. A mask is a power of two minus one; a real
    /// one here is in the low thousands, so anything larger is a bad read.
    /// </summary>
    private const uint MaxPlausibleMask = 0xFFFF;

    private readonly IMemoryReader _reader;
    private readonly nint _storeAddress;
    private readonly Dictionary<ulong, string> _cache = [];

    public PlayerNameCache(IMemoryReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _storeAddress = Offsets335a.Rebase(Offsets335a.NameCache.Store, reader.ModuleBase);
    }

    /// <summary>
    /// Looks up the name for <paramref name="guid"/>, or an empty string when it is unknown.
    /// </summary>
    /// <remarks>
    /// Results are memoised for the life of the attach. A player's name cannot change while
    /// the bot is attached, so a hit never needs re-reading.
    /// </remarks>
    public string GetName(WoWGuid guid)
    {
        if (guid.IsZero || !guid.IsPlayer)
        {
            return string.Empty;
        }

        if (_cache.TryGetValue(guid.Value, out string? cached))
        {
            return cached;
        }

        string name = Lookup(guid);
        _cache[guid.Value] = name;
        return name;
    }

    /// <summary>Forgets every memoised name.</summary>
    public void Clear() => _cache.Clear();

    private string Lookup(WoWGuid guid)
    {
        if (!_reader.TryRead(_storeAddress + (nint)Offsets335a.NameCache.Mask, out uint mask)
            || mask == 0
            || mask > MaxPlausibleMask)
        {
            return string.Empty;
        }

        if (!_reader.TryReadPointer(_storeAddress + (nint)Offsets335a.NameCache.Base, out nint tableBase)
            || tableBase == 0)
        {
            return string.Empty;
        }

        // The low dword of the GUID selects the bucket. The stride between bucket entries is
        // an unverified assumption; see the class remarks for why a wrong value is harmless.
        uint shortGuid = (uint)(guid.Value & 0xFFFFFFFF);
        nint bucketAddress = tableBase + (nint)(Offsets335a.NameCache.BucketStride * (shortGuid & mask));

        if (!_reader.TryReadPointer(bucketAddress, out nint node) || node == 0)
        {
            return string.Empty;
        }

        for (int step = 0; step < MaxChainLength && node > 0x1000; step++)
        {
            if (!_reader.TryRead(node + (nint)Offsets335a.NameCache.NodeGuid, out uint nodeGuid))
            {
                return string.Empty;
            }

            if (nodeGuid == shortGuid)
            {
                return _reader.TryReadCString(node + (nint)Offsets335a.NameCache.NodeName, out string name)
                    ? name
                    : string.Empty;
            }

            if (!_reader.TryReadPointer(node + (nint)Offsets335a.NameCache.NodeNext, out nint next) || next == node)
            {
                return string.Empty;
            }

            node = next;
        }

        return string.Empty;
    }
}
