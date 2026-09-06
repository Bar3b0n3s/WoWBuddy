using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Offsets;

namespace WoWBuddy.Core.Objects;

/// <summary>
/// Walks the client's object manager.
/// </summary>
/// <remarks>
/// <para>
/// The manager holds a singly-linked list of every object the client currently knows about:
/// units, players, items in bags, world objects, spell effects and corpses. The bot's entire
/// picture of the world comes from walking it.
/// </para>
/// <para>
/// The walk is defensive by design. The list is being mutated by the game thread while it is
/// being read, so a stale or torn pointer is normal rather than exceptional. Three guards
/// keep a bad list from turning into a hang or a flood of garbage: a hard cap on how many
/// objects will be visited, a check that each object's type tag is inside the legal range,
/// and cycle detection on the addresses already seen.
/// </para>
/// </remarks>
public sealed class ObjectManager
{
    /// <summary>
    /// Upper bound on objects visited in one walk.
    /// </summary>
    /// <remarks>
    /// A crowded capital city or a 40-player battleground sits in the low thousands. Well
    /// above that but still finite, so a corrupt list terminates instead of spinning.
    /// </remarks>
    public const int MaxObjects = 16384;

    private readonly IMemoryReader _reader;
    private readonly nint _clientConnectionAddress;

    /// <summary>Creates a manager reading through <paramref name="reader"/>.</summary>
    public ObjectManager(IMemoryReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _clientConnectionAddress = Offsets335a.Rebase(
            Offsets335a.ObjectManager.ClientConnection, reader.ModuleBase);
    }

    /// <summary>Address the client connection pointer is read from, after rebasing.</summary>
    public nint ClientConnectionAddress => _clientConnectionAddress;

    /// <summary>
    /// Resolves the current object manager, or 0 when the client is not in the world.
    /// </summary>
    /// <remarks>
    /// Null here is the normal state at the login and character-select screens, not an error.
    /// </remarks>
    public nint ResolveManager()
    {
        nint connection = _reader.ReadPointerOrZero(_clientConnectionAddress);
        if (connection == 0)
        {
            return 0;
        }

        return _reader.ReadPointerOrZero(connection + (nint)Offsets335a.ObjectManager.CurMgr);
    }

    /// <summary>True when the object manager is resolvable, meaning a character is in the world.</summary>
    public bool IsInWorld => ResolveManager() != 0;

    /// <summary>
    /// Reads the local player's GUID from the object manager.
    /// Returns <see cref="WoWGuid.Zero"/> when not in the world.
    /// </summary>
    public WoWGuid GetLocalPlayerGuid()
    {
        nint manager = ResolveManager();
        if (manager == 0)
        {
            return WoWGuid.Zero;
        }

        return new WoWGuid(_reader.ReadOrDefault<ulong>(
            manager + (nint)Offsets335a.ObjectManager.LocalPlayerGuid));
    }

    /// <summary>
    /// Enumerates every object currently in the manager's list.
    /// </summary>
    /// <remarks>
    /// Lazily evaluated, so a caller that only wants the first match does not pay for the
    /// whole list. Objects whose type tag is out of range are skipped rather than ending the
    /// walk: a single torn read should not cost the caller the rest of the world.
    /// </remarks>
    public IEnumerable<GameObjectRef> EnumerateObjects()
    {
        nint manager = ResolveManager();
        if (manager == 0)
        {
            yield break;
        }

        nint current = _reader.ReadPointerOrZero(manager + (nint)Offsets335a.ObjectManager.FirstObject);
        var visited = new HashSet<nint>();
        int examined = 0;

        // The client marks the end of the list with a pointer that is not 4-byte aligned,
        // as well as with null, so both terminate the walk.
        while (current > 0x1000 && (current & 1) == 0 && examined < MaxObjects)
        {
            if (!visited.Add(current))
            {
                Log.For<ObjectManager>().Warning(
                    "Object manager list looped at 0x{Address:X8} after {Count} objects; stopping walk",
                    current, examined);
                yield break;
            }

            examined++;

            if (!_reader.TryRead(current + (nint)Offsets335a.Object.Type, out int rawType))
            {
                // The object went away underneath us. The next pointer is unreadable too,
                // so there is nothing left to walk.
                yield break;
            }

            nint next = _reader.ReadPointerOrZero(current + (nint)Offsets335a.ObjectManager.NextObject);

            if (WoWObjectTypeExtensions.IsValid(rawType))
            {
                ulong rawGuid = _reader.ReadOrDefault<ulong>(current + (nint)Offsets335a.Object.Guid);
                if (rawGuid != 0)
                {
                    yield return new GameObjectRef(_reader, current, (WoWObjectType)rawType, new WoWGuid(rawGuid));
                }
            }

            if (next == current)
            {
                yield break;
            }

            current = next;
        }

        if (examined >= MaxObjects)
        {
            Log.For<ObjectManager>().Warning(
                "Object manager walk hit the {Max}-object cap; the list is probably corrupt", MaxObjects);
        }
    }

    /// <summary>Enumerates only objects of the given type.</summary>
    public IEnumerable<GameObjectRef> EnumerateObjects(WoWObjectType type) =>
        EnumerateObjects().Where(o => o.Type == type);

    /// <summary>
    /// Finds one object by GUID, or an invalid handle when it is not in the manager.
    /// </summary>
    /// <remarks>
    /// A linear walk. That is fine at phase 1 volumes and for the inspector; the typed game
    /// API added in later phases caches one snapshot per tick and indexes it by GUID rather
    /// than calling this per lookup.
    /// </remarks>
    public GameObjectRef FindByGuid(WoWGuid guid)
    {
        if (guid.IsZero)
        {
            return default;
        }

        foreach (GameObjectRef obj in EnumerateObjects())
        {
            if (obj.Guid == guid)
            {
                return obj;
            }
        }

        return default;
    }

    /// <summary>
    /// Finds the local player's object, or an invalid handle when not in the world.
    /// </summary>
    public GameObjectRef FindLocalPlayer()
    {
        WoWGuid guid = GetLocalPlayerGuid();
        return guid.IsZero ? default : FindByGuid(guid);
    }

    /// <summary>Counts the objects of each type currently in the list.</summary>
    public IReadOnlyDictionary<WoWObjectType, int> CountByType()
    {
        var counts = new Dictionary<WoWObjectType, int>();
        foreach (GameObjectRef obj in EnumerateObjects())
        {
            counts[obj.Type] = counts.GetValueOrDefault(obj.Type) + 1;
        }

        return counts;
    }
}
