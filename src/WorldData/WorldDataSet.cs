using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;

namespace WoWBuddy.WorldData;

/// <summary>
/// Everything the bot knows about where things are, loaded from the user's export.
/// </summary>
/// <remarks>
/// <para>
/// Indexed for the two questions the bot actually asks: "where are the nearest nodes of these
/// kinds" and "where is the nearest vendor". Both are asked repeatedly and both scan the
/// whole set, so the spawn lists are grouped by map once at load rather than filtered each
/// time; a full database is a few hundred thousand rows and scanning it per tick would be
/// felt.
/// </para>
/// </remarks>
public sealed class WorldDataSet
{
    private readonly Dictionary<int, List<GameObjectSpawn>> _gameObjectsByMap = [];
    private readonly Dictionary<int, List<CreatureSpawn>> _creaturesByMap = [];
    private readonly Dictionary<uint, GameObjectTemplate> _gameObjectTemplates = [];
    private readonly Dictionary<uint, CreatureTemplate> _creatureTemplates = [];
    private readonly Dictionary<uint, ItemTemplate> _itemTemplates = [];
    private readonly Dictionary<uint, List<uint>> _vendorsByItem = [];
    private readonly Dictionary<uint, QuestTemplate> _quests = [];

    /// <summary>Standard file names produced by the export scripts.</summary>
    public static class FileNames
    {
        public const string GameObjectSpawns = "gameobject-spawns.tsv";

        public const string GameObjectTemplates = "gameobject-templates.tsv";

        public const string CreatureSpawns = "creature-spawns.tsv";

        public const string CreatureTemplates = "creature-templates.tsv";

        /// <summary>Every item, for deciding about loot before picking it up.</summary>
        public const string ItemTemplates = "item-templates.tsv";

        /// <summary>What each vendor sells, for buying materials.</summary>
        public const string VendorItems = "npc-vendors.tsv";

        /// <summary>What each quest asks for.</summary>
        public const string QuestTemplates = "quest-templates.tsv";
    }

    /// <summary>Game object spawns, by map.</summary>
    public IReadOnlyDictionary<int, List<GameObjectSpawn>> GameObjectsByMap => _gameObjectsByMap;

    /// <summary>How many game object spawns are loaded.</summary>
    public int GameObjectSpawnCount => _gameObjectsByMap.Values.Sum(list => list.Count);

    /// <summary>How many creature spawns are loaded.</summary>
    public int CreatureSpawnCount => _creaturesByMap.Values.Sum(list => list.Count);

    /// <summary>How many game object templates are loaded.</summary>
    public int GameObjectTemplateCount => _gameObjectTemplates.Count;

    /// <summary>How many creature templates are loaded.</summary>
    public int CreatureTemplateCount => _creatureTemplates.Count;

    /// <summary>True when enough was loaded to be useful.</summary>
    public bool IsLoaded => GameObjectSpawnCount > 0 || CreatureSpawnCount > 0;

    /// <summary>
    /// Loads every export file found in a directory.
    /// </summary>
    /// <remarks>
    /// Missing files are not an error. A user who only wants gathering has no reason to
    /// export creatures, and one who only wants vendor errands has no reason to export game
    /// objects; each part of the bot checks for what it needs and says so if it is absent.
    /// </remarks>
    public IReadOnlyList<string> LoadFrom(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var problems = new List<string>();

        LoadFile(directory, FileNames.GameObjectSpawns, problems, reader =>
        {
            var spawns = new List<GameObjectSpawn>();
            WorldDataReadResult result = WorldDataReader.ReadGameObjectSpawns(reader, spawns);
            foreach (GameObjectSpawn spawn in spawns)
            {
                Bucket(_gameObjectsByMap, spawn.MapId).Add(spawn);
            }

            return result;
        });

        LoadFile(directory, FileNames.GameObjectTemplates, problems, reader =>
        {
            var templates = new List<GameObjectTemplate>();
            WorldDataReadResult result = WorldDataReader.ReadGameObjectTemplates(reader, templates);
            foreach (GameObjectTemplate template in templates)
            {
                _gameObjectTemplates[template.Entry] = template;
            }

            return result;
        });

        LoadFile(directory, FileNames.CreatureSpawns, problems, reader =>
        {
            var spawns = new List<CreatureSpawn>();
            WorldDataReadResult result = WorldDataReader.ReadCreatureSpawns(reader, spawns);
            foreach (CreatureSpawn spawn in spawns)
            {
                Bucket(_creaturesByMap, spawn.MapId).Add(spawn);
            }

            return result;
        });

        LoadFile(directory, FileNames.CreatureTemplates, problems, reader =>
        {
            var templates = new List<CreatureTemplate>();
            WorldDataReadResult result = WorldDataReader.ReadCreatureTemplates(reader, templates);
            foreach (CreatureTemplate template in templates)
            {
                _creatureTemplates[template.Entry] = template;
            }

            return result;
        });

        LoadFile(directory, FileNames.ItemTemplates, problems, reader =>
        {
            var templates = new List<ItemTemplate>();
            WorldDataReadResult result = WorldDataReader.ReadItemTemplates(reader, templates);
            foreach (ItemTemplate template in templates)
            {
                _itemTemplates[template.Entry] = template;
            }

            return result;
        });

        LoadFile(directory, FileNames.VendorItems, problems, reader =>
        {
            var items = new List<VendorItem>();
            WorldDataReadResult result = WorldDataReader.ReadVendorItems(reader, items);
            foreach (VendorItem item in items)
            {
                // Indexed by what is sold rather than by who sells it: the question the bot
                // asks is "who has this", never "what does that one have".
                if (!_vendorsByItem.TryGetValue(item.ItemEntry, out List<uint>? sellers))
                {
                    sellers = [];
                    _vendorsByItem[item.ItemEntry] = sellers;
                }

                sellers.Add(item.VendorEntry);
            }

            return result;
        });

        LoadFile(directory, FileNames.QuestTemplates, problems, reader =>
        {
            var quests = new List<QuestTemplate>();
            WorldDataReadResult result = WorldDataReader.ReadQuestTemplates(reader, quests);
            foreach (QuestTemplate quest in quests)
            {
                _quests[quest.Id] = quest;
            }

            return result;
        });

        Log.For<WorldDataSet>().Information(
            "World data loaded: {GameObjects} object spawns, {Creatures} creature spawns, " +
            "{ObjectTemplates} object templates, {CreatureTemplates} creatures, {Items} items, "
            + "{Quests} quests",
            GameObjectSpawnCount, CreatureSpawnCount, GameObjectTemplateCount,
            CreatureTemplateCount, ItemTemplateCount, QuestCount);

        return problems;
    }

    /// <summary>How many items were exported.</summary>
    public int ItemTemplateCount => _itemTemplates.Count;

    /// <summary>What a creature is, if it was exported.</summary>
    public CreatureTemplate? CreatureFor(uint entry) =>
        _creatureTemplates.TryGetValue(entry, out CreatureTemplate template) ? template : null;

    /// <summary>How many quests were exported.</summary>
    public int QuestCount => _quests.Count;

    /// <summary>What a quest asks for, if it was exported.</summary>
    public QuestTemplate? QuestFor(uint id) =>
        _quests.TryGetValue(id, out QuestTemplate quest) ? quest : null;

    /// <summary>Creatures that sell an item.</summary>
    public IReadOnlyList<uint> VendorsSelling(uint itemEntry) =>
        _vendorsByItem.TryGetValue(itemEntry, out List<uint>? vendors) ? vendors : [];

    /// <summary>
    /// A trainer for a class, nearest to a position.
    /// </summary>
    /// <remarks>
    /// The Train errand's whole problem: the trainer flag is on profession trainers, mount
    /// vendors and pet trainers too, and none of those teaches a warrior how to hit things.
    /// </remarks>
    public ServiceNpc? FindClassTrainer(int mapId, Vector3 near, int characterClass)
    {
        ServiceNpc? best = null;
        float bestDistance = float.MaxValue;

        if (!_creaturesByMap.TryGetValue(mapId, out List<CreatureSpawn>? spawns))
        {
            return null;
        }

        foreach (CreatureSpawn spawn in spawns)
        {
            if (!_creatureTemplates.TryGetValue(spawn.Entry, out CreatureTemplate template)
                || !template.TrainsClass(characterClass))
            {
                continue;
            }

            float distance = spawn.Position.Distance(near);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = new ServiceNpc(spawn, template);
            }
        }

        return best;
    }

    /// <summary>What an item is, if it was exported.</summary>
    public ItemTemplate? ItemFor(uint entry) =>
        _itemTemplates.TryGetValue(entry, out ItemTemplate template) ? template : null;

    /// <summary>The template for a game object entry, if it was exported.</summary>
    public GameObjectTemplate? TemplateFor(uint entry) =>
        _gameObjectTemplates.TryGetValue(entry, out GameObjectTemplate template) ? template : null;

    /// <summary>
    /// Finds game object entries whose name contains <paramref name="text"/>.
    /// </summary>
    /// <remarks>
    /// How a user discovers the entry ids to gather. The bot ships no list of node ids —
    /// inventing one would be exactly the kind of unverified fact this project refuses — so
    /// instead it searches the user's own database: "Copper Vein", "Peacebloom", "Thorium".
    /// Names are in the database's language, so this works as well as that database does.
    /// </remarks>
    public IReadOnlyList<GameObjectTemplate> FindTemplatesByName(string text, bool gatherableOnly = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return _gameObjectTemplates.Values
            .Where(template => !gatherableOnly || template.IsGatherableType)
            .Where(template => template.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Spawns of the given entries on a map, nearest to <paramref name="origin"/> first.
    /// </summary>
    public IReadOnlyList<GameObjectSpawn> FindNodes(
        int mapId,
        IReadOnlySet<uint> entries,
        Vector3 origin,
        float maxDistance = float.MaxValue,
        int limit = 64)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0 || !_gameObjectsByMap.TryGetValue(mapId, out List<GameObjectSpawn>? spawns))
        {
            return [];
        }

        return spawns
            .Where(spawn => entries.Contains(spawn.Entry))
            .Select(spawn => (Spawn: spawn, Distance: spawn.Position.Distance(origin)))
            .Where(x => x.Distance <= maxDistance)
            .OrderBy(x => x.Distance)
            .Take(limit)
            .Select(x => x.Spawn)
            .ToList();
    }

    /// <summary>
    /// Finds the nearest creature offering a service, which is what an errand needs.
    /// </summary>
    /// <param name="mapId">The map the character is on.</param>
    /// <param name="origin">Where the character is.</param>
    /// <param name="wanted">What the creature must offer.</param>
    /// <param name="limit">How many to return.</param>
    public IReadOnlyList<ServiceNpc> FindServices(
        int mapId,
        Vector3 origin,
        Func<CreatureTemplate, bool> wanted,
        int limit = 8)
    {
        ArgumentNullException.ThrowIfNull(wanted);

        if (!_creaturesByMap.TryGetValue(mapId, out List<CreatureSpawn>? spawns))
        {
            return [];
        }

        var results = new List<(ServiceNpc Npc, float Distance)>();

        foreach (CreatureSpawn spawn in spawns)
        {
            if (!_creatureTemplates.TryGetValue(spawn.Entry, out CreatureTemplate template)
                || !wanted(template))
            {
                continue;
            }

            results.Add((new ServiceNpc(spawn, template), spawn.Position.Distance(origin)));
        }

        return results
            .OrderBy(x => x.Distance)
            .Take(limit)
            .Select(x => x.Npc)
            .ToList();
    }

    private static List<T> Bucket<T>(Dictionary<int, List<T>> map, int key)
    {
        if (!map.TryGetValue(key, out List<T>? list))
        {
            list = [];
            map[key] = list;
        }

        return list;
    }

    private static void LoadFile(
        string directory,
        string fileName,
        List<string> problems,
        Func<TextReader, WorldDataReadResult> load)
    {
        string path = Path.Combine(directory, fileName);

        if (!File.Exists(path))
        {
            problems.Add($"{fileName} was not found in {directory}. Parts of the bot that need it will say so.");
            return;
        }

        try
        {
            using var reader = new StreamReader(path);
            WorldDataReadResult result = load(reader);

            if (!result.Success)
            {
                problems.Add($"{fileName}: {result.Message}");
            }
        }
        catch (IOException ex)
        {
            problems.Add($"{fileName} could not be read: {ex.Message}");
        }
    }
}
