using System.Text.Json;
using System.Text.Json.Serialization;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;

namespace WoWBuddy.WorldData;

/// <summary>What kind of thing the bot remembers seeing.</summary>
public enum RememberedKind
{
    /// <summary>A gatherable node: herb, vein, or chest.</summary>
    Node = 0,

    /// <summary>A creature that buys and sells.</summary>
    Vendor = 1,

    /// <summary>A creature that repairs gear.</summary>
    Repair = 2,

    /// <summary>A creature that trains abilities or professions.</summary>
    Trainer = 3,

    /// <summary>A mailbox.</summary>
    Mailbox = 4,

    /// <summary>A flight master.</summary>
    FlightMaster = 5,

    /// <summary>An innkeeper, which is where a hearthstone is set.</summary>
    Innkeeper = 6,
}

/// <summary>Something the bot has walked past and written down.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Entry">Its template id, so the same thing is recognised again.</param>
/// <param name="MapId">Which map it is on.</param>
/// <param name="X">Where it is.</param>
/// <param name="Y">Where it is.</param>
/// <param name="Z">Where it is.</param>
/// <param name="Name">Its name, when the client offered one.</param>
/// <param name="LastSeenUtc">When it was last confirmed to exist.</param>
public sealed record RememberedPlace(
    RememberedKind Kind,
    uint Entry,
    int MapId,
    float X,
    float Y,
    float Z,
    string Name,
    DateTimeOffset LastSeenUtc)
{
    /// <summary>Where it is.</summary>
    [JsonIgnore]
    public Vector3 Position => new(X, Y, Z);
}

/// <summary>
/// What the bot has learned about the world by playing in it.
/// </summary>
/// <remarks>
/// <para>
/// A bot needs to know where things are: where nodes spawn, where the nearest repair NPC is.
/// The obvious source is a server's database, and that is the wrong assumption — a person
/// botting on somebody else's realm has no access to it. What they do have is a character
/// that walks around and a client that reports everything nearby.
/// </para>
/// <para>
/// So the bot writes down what it sees. Every vendor it walks past, every mailbox, every node
/// it gathers goes into a file, and the next time an errand comes due it already knows where
/// to go. The map fills in as the character plays, which is how a person learns a zone too.
/// </para>
/// <para>
/// Kept per realm and character folder, because a private server's world is not necessarily
/// the same as another's: spawns are edited, NPCs are moved, custom content exists. Sharing
/// one file between realms would teach the bot to walk to places that are not there.
/// </para>
/// <para>
/// Positions are recorded only when the client actually reported one. Game object positions
/// depend on an offset worked out at attach, and when that fails nothing is written rather
/// than zeroes being stored and later walked to.
/// </para>
/// </remarks>
public sealed class WorldMemory
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// How close two sightings must be to count as the same thing.
    /// </summary>
    /// <remarks>
    /// Nodes respawn at slightly different points and NPCs wander a little, so exact
    /// coordinates would fill the file with duplicates of the same vendor.
    /// </remarks>
    public const float SamePlaceRadius = 8f;

    /// <summary>Places forgotten after this long without being seen again.</summary>
    /// <remarks>
    /// Servers get patched and NPCs get moved. A place the bot has walked past repeatedly
    /// without finding anything is worse than no memory at all, because it keeps sending the
    /// character back.
    /// </remarks>
    public static readonly TimeSpan Staleness = TimeSpan.FromDays(30);

    private readonly List<RememberedPlace> _places = [];

    /// <summary>Everything remembered.</summary>
    public IReadOnlyList<RememberedPlace> Places => _places;

    /// <summary>How many places are remembered.</summary>
    public int Count => _places.Count;

    /// <summary>True when anything has been written down since the last save.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>
    /// Records something the bot can see.
    /// </summary>
    /// <returns>True when this was new, false when it was already known.</returns>
    public bool Remember(
        RememberedKind kind,
        uint entry,
        int mapId,
        Vector3 position,
        string name,
        DateTimeOffset now)
    {
        // A position the client could not supply must never be written down: a zero would be
        // remembered as the middle of the map and walked to later.
        if (!WorldBounds.IsPlausible(position))
        {
            return false;
        }

        for (int i = 0; i < _places.Count; i++)
        {
            RememberedPlace existing = _places[i];

            if (existing.Kind != kind || existing.MapId != mapId || existing.Entry != entry)
            {
                continue;
            }

            if (existing.Position.Distance(position) > SamePlaceRadius)
            {
                continue;
            }

            // Seen again: refresh the timestamp so it does not go stale, but keep the
            // original position rather than drifting it towards wherever the character
            // happened to be standing.
            _places[i] = existing with { LastSeenUtc = now };
            IsDirty = true;
            return false;
        }

        _places.Add(new RememberedPlace(
            kind, entry, mapId, position.X, position.Y, position.Z, name, now));
        IsDirty = true;

        Log.For<WorldMemory>().Debug(
            "Learned a {Kind} on map {Map} at {Position}: {Name}", kind, mapId, position, name);

        return true;
    }

    /// <summary>
    /// The nearest remembered places of a kind, closest first.
    /// </summary>
    /// <param name="kind">What is wanted.</param>
    /// <param name="mapId">The map the character is on.</param>
    /// <param name="origin">Where the character is.</param>
    /// <param name="now">The current time, for discarding stale entries.</param>
    /// <param name="limit">How many to return.</param>
    public IReadOnlyList<RememberedPlace> Nearest(
        RememberedKind kind,
        int mapId,
        Vector3 origin,
        DateTimeOffset now,
        int limit = 8) =>
        _places
            .Where(place => place.Kind == kind && place.MapId == mapId)
            .Where(place => now - place.LastSeenUtc < Staleness)
            .OrderBy(place => place.Position.Distance(origin))
            .Take(limit)
            .ToList();

    /// <summary>The nearest remembered place of a kind, or null when none is known.</summary>
    public RememberedPlace? NearestOrDefault(
        RememberedKind kind, int mapId, Vector3 origin, DateTimeOffset now) =>
        Nearest(kind, mapId, origin, now, limit: 1) is [RememberedPlace first, ..] ? first : null;

    /// <summary>Removes entries that have not been seen for a long time.</summary>
    public int Forget(DateTimeOffset now)
    {
        int removed = _places.RemoveAll(place => now - place.LastSeenUtc >= Staleness);

        if (removed > 0)
        {
            IsDirty = true;
            Log.For<WorldMemory>().Information("Forgot {Count} stale place(s)", removed);
        }

        return removed;
    }

    /// <summary>
    /// Adds everything from a world data export, as though the bot had seen it.
    /// </summary>
    /// <remarks>
    /// Optional. Someone who happens to have a server database — an operator, or a developer
    /// running a local server — can seed the map rather than waiting for the character to
    /// walk past everything. Nobody needs to: the map fills in by itself.
    /// </remarks>
    public int SeedFrom(WorldDataSet worldData, IReadOnlySet<uint> nodeEntries, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(worldData);
        ArgumentNullException.ThrowIfNull(nodeEntries);

        int added = 0;

        foreach ((int mapId, List<GameObjectSpawn> spawns) in worldData.GameObjectsByMap)
        {
            foreach (GameObjectSpawn spawn in spawns)
            {
                if (!nodeEntries.Contains(spawn.Entry))
                {
                    continue;
                }

                string name = worldData.TemplateFor(spawn.Entry)?.Name ?? string.Empty;

                if (Remember(RememberedKind.Node, spawn.Entry, mapId, spawn.Position, name, now))
                {
                    added++;
                }
            }
        }

        Log.For<WorldMemory>().Information("Seeded {Count} place(s) from a world data export", added);
        return added;
    }

    /// <summary>Loads a saved map, returning an empty one when there is nothing saved.</summary>
    public static WorldMemory Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var memory = new WorldMemory();

        if (!File.Exists(path))
        {
            return memory;
        }

        try
        {
            RememberedPlace[]? places =
                JsonSerializer.Deserialize<RememberedPlace[]>(File.ReadAllText(path), SerializerOptions);

            if (places is not null)
            {
                memory._places.AddRange(places);
            }

            Log.For<WorldMemory>().Information(
                "Loaded {Count} remembered place(s) from {Path}", memory.Count, path);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A corrupt map is not worth stopping for: the bot relearns as it plays.
            Log.For<WorldMemory>().Warning(ex, "Could not read {Path}; starting with an empty map", path);
        }

        return memory;
    }

    /// <summary>Saves the map, atomically.</summary>
    public bool Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            string json = JsonSerializer.Serialize(_places, SerializerOptions);
            string temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);

            IsDirty = false;
            return true;
        }
        catch (IOException ex)
        {
            Log.For<WorldMemory>().Warning(ex, "Could not save the world map to {Path}", path);
            return false;
        }
    }
}
