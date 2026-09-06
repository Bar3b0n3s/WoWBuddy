using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Attach;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;
using WoWBuddy.GameApi.Objects;

namespace WoWBuddy.GameApi;

/// <summary>
/// The typed view of the game world.
/// </summary>
/// <remarks>
/// <para>
/// Everything above this line works in terms of <see cref="WoWUnit"/> and friends and never
/// touches an address. Everything below works in raw pointers and offsets. Keeping that seam
/// sharp is what makes the unverified parts of the project auditable: they are all on one
/// side of it.
/// </para>
/// <para>
/// Enumeration is lazy and uncached. Callers that need a stable picture across several
/// queries take one <see cref="Snapshot"/> and work from that.
/// </para>
/// </remarks>
public sealed class World
{
    private readonly GameClient _client;

    /// <summary>Creates a typed view over an attached client.</summary>
    public World(GameClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>The attached client.</summary>
    public GameClient Client => _client;

    /// <summary>True while a character is in the world.</summary>
    public bool IsInWorld => _client.Objects.IsInWorld;

    /// <summary>
    /// The character the bot is playing, or null when not in the world.
    /// </summary>
    public WoWLocalPlayer? Me
    {
        get
        {
            GameObjectRef reference = _client.Objects.FindLocalPlayer();
            return reference.IsValid
                ? new WoWLocalPlayer(reference, _client.PositionLayout, _client.Names)
                : null;
        }
    }

    /// <summary>Every object the client currently knows about, typed.</summary>
    public IEnumerable<WoWObject> Objects =>
        _client.Objects.EnumerateObjects().Select(Wrap);

    /// <summary>Every unit and player.</summary>
    public IEnumerable<WoWUnit> Units =>
        _client.Objects.EnumerateObjects().Where(o => o.Type.IsUnitLike()).Select(Wrap).OfType<WoWUnit>();

    /// <summary>Every player character, including the bot's own.</summary>
    public IEnumerable<WoWPlayer> Players =>
        _client.Objects.EnumerateObjects(WoWObjectType.Player).Select(Wrap).OfType<WoWPlayer>();

    /// <summary>Every world object.</summary>
    public IEnumerable<WoWGameObject> GameObjects =>
        _client.Objects.EnumerateObjects(WoWObjectType.GameObject).Select(Wrap).OfType<WoWGameObject>();

    /// <summary>Every item, including bags.</summary>
    public IEnumerable<WoWItem> Items =>
        _client.Objects.EnumerateObjects().Where(o => o.Type is WoWObjectType.Item or WoWObjectType.Container)
            .Select(Wrap).OfType<WoWItem>();

    /// <summary>Every corpse.</summary>
    public IEnumerable<WoWCorpse> Corpses =>
        _client.Objects.EnumerateObjects(WoWObjectType.Corpse).Select(Wrap).OfType<WoWCorpse>();

    /// <summary>Every ground-targeted spell effect.</summary>
    public IEnumerable<WoWDynamicObject> DynamicObjects =>
        _client.Objects.EnumerateObjects(WoWObjectType.DynamicObject).Select(Wrap).OfType<WoWDynamicObject>();

    /// <summary>Finds one object by GUID, or null when it is not present.</summary>
    public WoWObject? FindByGuid(WoWGuid guid)
    {
        GameObjectRef reference = _client.Objects.FindByGuid(guid);
        return reference.IsValid ? Wrap(reference) : null;
    }

    /// <summary>The bot's current target, or null when nothing is targeted.</summary>
    public WoWUnit? Target
    {
        get
        {
            WoWLocalPlayer? me = Me;
            if (me is null)
            {
                return null;
            }

            WoWGuid targetGuid = me.TargetGuid;
            return targetGuid.IsZero ? null : FindByGuid(targetGuid) as WoWUnit;
        }
    }

    /// <summary>
    /// Units within <paramref name="range"/> yards of the local player, nearest first.
    /// </summary>
    /// <remarks>
    /// Excludes the local player itself, which is almost never what a caller asking for
    /// "units near me" wants included.
    /// </remarks>
    public IReadOnlyList<WoWUnit> UnitsNear(float range)
    {
        WoWLocalPlayer? me = Me;
        if (me is null)
        {
            return [];
        }

        Vector3 origin = me.Position;
        WoWGuid myGuid = me.Guid;

        return Units
            .Where(u => u.Guid != myGuid)
            .Select(u => (Unit: u, Distance: u.Position.Distance(origin)))
            .Where(x => x.Distance <= range && WorldBounds.IsPlausible(x.Unit.Position))
            .OrderBy(x => x.Distance)
            .Select(x => x.Unit)
            .ToList();
    }

    /// <summary>
    /// Takes one consistent picture of the world.
    /// </summary>
    /// <remarks>
    /// One walk of the object list rather than one per query. Behaviour trees run several
    /// checks per tick and would otherwise re-walk the list for each, both wasting reads and
    /// seeing a slightly different world in each check.
    /// </remarks>
    public WorldSnapshot Snapshot()
    {
        var objects = new List<WoWObject>();
        WoWLocalPlayer? me = null;
        WoWGuid myGuid = _client.Objects.GetLocalPlayerGuid();

        foreach (GameObjectRef reference in _client.Objects.EnumerateObjects())
        {
            WoWObject wrapped = Wrap(reference);
            objects.Add(wrapped);

            if (reference.Guid == myGuid && reference.Type == WoWObjectType.Player)
            {
                me = new WoWLocalPlayer(reference, _client.PositionLayout, _client.Names);
            }
        }

        return new WorldSnapshot(objects, me);
    }

    private WoWObject Wrap(GameObjectRef reference)
    {
        Offsets335a.PositionLayout layout = _client.PositionLayout;

        return reference.Type switch
        {
            WoWObjectType.Player => reference.Guid == _client.LocalPlayerGuid
                ? new WoWLocalPlayer(reference, layout, _client.Names)
                : new WoWPlayer(reference, layout, _client.Names),
            WoWObjectType.Unit => new WoWUnit(reference, layout),
            WoWObjectType.GameObject => new WoWGameObject(reference, layout, _client.GameObjectPositionOffset),
            WoWObjectType.Container => new WoWContainer(reference, layout),
            WoWObjectType.Item => new WoWItem(reference, layout),
            WoWObjectType.Corpse => new WoWCorpse(reference, layout),
            WoWObjectType.DynamicObject => new WoWDynamicObject(reference, layout),
            _ => new WoWObject(reference, layout),
        };
    }
}

/// <summary>
/// One consistent picture of the world, taken in a single walk of the object list.
/// </summary>
/// <param name="Objects">Every object present at the moment of the walk.</param>
/// <param name="Me">The local player, or null when not in the world.</param>
public sealed record WorldSnapshot(IReadOnlyList<WoWObject> Objects, WoWLocalPlayer? Me)
{
    /// <summary>Units and players in the snapshot.</summary>
    public IEnumerable<WoWUnit> Units => Objects.OfType<WoWUnit>();

    /// <summary>Players in the snapshot.</summary>
    public IEnumerable<WoWPlayer> Players => Objects.OfType<WoWPlayer>();

    /// <summary>World objects in the snapshot.</summary>
    public IEnumerable<WoWGameObject> GameObjects => Objects.OfType<WoWGameObject>();

    /// <summary>How many objects of each type the snapshot holds.</summary>
    public IReadOnlyDictionary<WoWObjectType, int> CountByType =>
        Objects.GroupBy(o => o.Type).ToDictionary(g => g.Key, g => g.Count());
}
