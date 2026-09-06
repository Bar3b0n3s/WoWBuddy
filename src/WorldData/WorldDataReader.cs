using System.Globalization;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;

namespace WoWBuddy.WorldData;

/// <summary>Why a world data file could not be read.</summary>
public enum WorldDataError
{
    /// <summary>No error.</summary>
    None = 0,

    /// <summary>The file is not where it was expected.</summary>
    Missing,

    /// <summary>The file's header does not name the columns the reader expects.</summary>
    UnexpectedColumns,

    /// <summary>Rows were present but none could be parsed.</summary>
    NoUsableRows,
}

/// <summary>What reading one export file produced.</summary>
/// <param name="RowsRead">Rows successfully parsed.</param>
/// <param name="RowsSkipped">Rows that could not be parsed.</param>
/// <param name="Error">Why the read failed, when it did.</param>
/// <param name="Message">A sentence for the user.</param>
public readonly record struct WorldDataReadResult(
    int RowsRead,
    int RowsSkipped,
    WorldDataError Error,
    string Message)
{
    /// <summary>True when anything usable came back.</summary>
    public bool Success => Error == WorldDataError.None && RowsRead > 0;
}

/// <summary>
/// Reads the world data a user exported from their own server database.
/// </summary>
/// <remarks>
/// <para>
/// The bot needs to know where things are: where herb nodes spawn, where the vendors and
/// repair NPCs and mailboxes are. All of that is already in the database of any 3.3.5 server,
/// and asking the user to export it is far better than having the bot wander until it
/// stumbles across things.
/// </para>
/// <para>
/// <b>Read from a file rather than from the database directly.</b> Connecting to MySQL would
/// mean a database driver and a network connection, and this project promises to make no
/// network calls at all. An export is also more useful: it works when the server is not
/// running, it can be shared between machines, and it can be inspected in a text editor when
/// something looks wrong.
/// </para>
/// <para>
/// <b>No data ships with this project.</b> The export comes from the user's own database, the
/// same posture as the navigation meshes. See <c>docs/world-data.md</c>.
/// </para>
/// <para>
/// Tab-separated, because that is what the <c>mysql</c> client emits with <c>-B</c> and needs
/// no dependency to parse. Fields are never quoted by that output, so a name containing a tab
/// would break the row; such a row is skipped rather than corrupting the file.
/// </para>
/// </remarks>
public static class WorldDataReader
{
    /// <summary>The value the mysql client writes for a null field.</summary>
    private const string NullMarker = "NULL";

    /// <summary>Reads game object spawns.</summary>
    public static WorldDataReadResult ReadGameObjectSpawns(
        TextReader reader, ICollection<GameObjectSpawn> into)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(into);

        return Read(reader, ["guid", "entry", "map", "x", "y", "z"], fields =>
        {
            if (!TryUInt(fields[0], out uint guid)
                || !TryUInt(fields[1], out uint entry)
                || !TryInt(fields[2], out int map)
                || !TryVector(fields[3], fields[4], fields[5], out Vector3 position))
            {
                return false;
            }

            into.Add(new GameObjectSpawn(guid, entry, map, position));
            return true;
        });
    }

    /// <summary>Reads game object templates.</summary>
    public static WorldDataReadResult ReadGameObjectTemplates(
        TextReader reader, ICollection<GameObjectTemplate> into)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(into);

        return Read(reader, ["entry", "name", "type", "lockId"], fields =>
        {
            if (!TryUInt(fields[0], out uint entry)
                || !TryInt(fields[2], out int type)
                || !TryUInt(fields[3], out uint lockId))
            {
                return false;
            }

            into.Add(new GameObjectTemplate(entry, fields[1], type, lockId));
            return true;
        });
    }

    /// <summary>Reads creature spawns.</summary>
    public static WorldDataReadResult ReadCreatureSpawns(
        TextReader reader, ICollection<CreatureSpawn> into)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(into);

        return Read(reader, ["guid", "entry", "map", "x", "y", "z"], fields =>
        {
            if (!TryUInt(fields[0], out uint guid)
                || !TryUInt(fields[1], out uint entry)
                || !TryInt(fields[2], out int map)
                || !TryVector(fields[3], fields[4], fields[5], out Vector3 position))
            {
                return false;
            }

            into.Add(new CreatureSpawn(guid, entry, map, position));
            return true;
        });
    }

    /// <summary>Reads creature templates and their service flags.</summary>
    /// <remarks>
    /// Only the first three columns are required. Faction, level range and rank were added
    /// later and are read when the file has them, so an export taken before they existed still
    /// loads — it simply says less about each creature. Re-exporting is worth it: rank is what
    /// tells the bot an elite from an ordinary mob, and nothing it can see in memory does.
    /// </remarks>
    public static WorldDataReadResult ReadCreatureTemplates(
        TextReader reader, ICollection<CreatureTemplate> into)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(into);

        return Read(reader, ["entry", "name", "npcflag"], fields =>
        {
            if (!TryUInt(fields[0], out uint entry) || !TryUInt(fields[2], out uint flags))
            {
                return false;
            }

            into.Add(new CreatureTemplate(
                entry,
                fields[1],
                flags,
                Optional(fields, 3, out uint faction) ? faction : 0,
                OptionalInt(fields, 4),
                OptionalInt(fields, 5),
                OptionalInt(fields, 6),
                fields.Length > 7 ? OptionalInt(fields, 7) : -1,
                OptionalInt(fields, 8)));

            return true;
        });
    }

    /// <summary>Reads what each vendor sells.</summary>
    public static WorldDataReadResult ReadVendorItems(
        TextReader reader, ICollection<VendorItem> into)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(into);

        return Read(reader, ["entry", "item"], fields =>
        {
            if (!TryUInt(fields[0], out uint vendor) || !TryUInt(fields[1], out uint item))
            {
                return false;
            }

            into.Add(new VendorItem(vendor, item));
            return true;
        });
    }

    /// <summary>Reads quest objectives.</summary>
    /// <remarks>
    /// Four creature slots and six item slots, which is what a 3.3.5 quest has. Empty slots are
    /// zero and are dropped rather than kept as requirements for nothing.
    /// </remarks>
    public static WorldDataReadResult ReadQuestTemplates(
        TextReader reader, ICollection<QuestTemplate> into)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(into);

        string[] columns =
        [
            "id", "title", "minlevel", "questlevel",
            "npc1", "npccount1", "npc2", "npccount2", "npc3", "npccount3", "npc4", "npccount4",
            "item1", "itemcount1", "item2", "itemcount2", "item3", "itemcount3",
            "item4", "itemcount4", "item5", "itemcount5", "item6", "itemcount6",
        ];

        return Read(reader, columns, fields =>
        {
            if (!TryUInt(fields[0], out uint id) || id == 0)
            {
                return false;
            }

            List<QuestRequirement> requirements = [];

            // Creatures first, at four columns in, then items at twelve. A negative creature
            // entry means a game object, which the bot interacts with rather than kills; the
            // sign is dropped because the entry is all it needs.
            AddPairs(fields, 4, 4, isItem: false, requirements);
            AddPairs(fields, 12, 6, isItem: true, requirements);

            into.Add(new QuestTemplate(
                id,
                fields[1],
                OptionalInt(fields, 2),
                OptionalInt(fields, 3),
                requirements));

            return true;
        });
    }

    private static void AddPairs(
        string[] fields,
        int start,
        int count,
        bool isItem,
        List<QuestRequirement> into)
    {
        for (int slot = 0; slot < count; slot++)
        {
            int index = start + (slot * 2);

            if (index + 1 >= fields.Length)
            {
                return;
            }

            int entry = Math.Abs(OptionalInt(fields, index));
            int needed = OptionalInt(fields, index + 1);

            if (entry != 0 && needed > 0)
            {
                into.Add(new QuestRequirement((uint)entry, needed, isItem));
            }
        }
    }

    /// <summary>Reads item templates.</summary>
    /// <remarks>
    /// The whole table, because a loot decision is about an item the character is not carrying
    /// yet and so could be about any of them. It is the largest of the exports and still only a
    /// few megabytes.
    /// </remarks>
    public static WorldDataReadResult ReadItemTemplates(
        TextReader reader, ICollection<ItemTemplate> into)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(into);

        string[] columns =
        [
            "entry", "name", "quality", "itemlevel", "requiredlevel",
            "class", "subclass", "inventorytype", "sellprice", "stackable",
        ];

        return Read(reader, columns, fields =>
        {
            if (!TryUInt(fields[0], out uint entry))
            {
                return false;
            }

            into.Add(new ItemTemplate(
                entry,
                fields[1],
                OptionalInt(fields, 2),
                OptionalInt(fields, 3),
                OptionalInt(fields, 4),
                OptionalInt(fields, 5),
                OptionalInt(fields, 6),
                OptionalInt(fields, 7),
                OptionalInt(fields, 8),
                OptionalInt(fields, 9)));

            return true;
        });
    }

    /// <summary>Reads a column that a file may not have, treating anything unreadable as zero.</summary>
    private static bool Optional(string[] fields, int index, out uint value)
    {
        value = 0;
        return index < fields.Length && TryUInt(fields[index], out value);
    }

    private static int OptionalInt(string[] fields, int index) =>
        index < fields.Length
        && int.TryParse(fields[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : 0;

    private static WorldDataReadResult Read(
        TextReader reader,
        string[] expectedColumns,
        Func<string[], bool> parseRow)
    {
        string? header = reader.ReadLine();
        if (header is null)
        {
            return new WorldDataReadResult(
                0, 0, WorldDataError.Missing, "The export file was empty.");
        }

        string[] columns = header.Split('\t');
        if (columns.Length < expectedColumns.Length)
        {
            return new WorldDataReadResult(
                0, 0, WorldDataError.UnexpectedColumns,
                $"Expected columns [{string.Join(", ", expectedColumns)}] but the file's first line " +
                $"has {columns.Length} field(s). Re-run the export script; the header line must be kept.");
        }

        int read = 0;
        int skipped = 0;

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            string[] fields = line.Split('\t');
            if (fields.Length < expectedColumns.Length || !parseRow(fields))
            {
                skipped++;
                continue;
            }

            read++;
        }

        if (read == 0)
        {
            return new WorldDataReadResult(
                0, skipped, WorldDataError.NoUsableRows,
                $"The file had a usable header but none of its {skipped} row(s) could be parsed.");
        }

        if (skipped > 0)
        {
            Log.For(nameof(WorldDataReader)).Warning(
                "Skipped {Skipped} unparseable row(s) out of {Total}", skipped, read + skipped);
        }

        return new WorldDataReadResult(read, skipped, WorldDataError.None, $"Read {read} row(s).");
    }

    private static bool TryUInt(string field, out uint value) =>
        uint.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryInt(string field, out int value) =>
        int.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryVector(string x, string y, string z, out Vector3 position)
    {
        position = Vector3.Zero;

        if (x == NullMarker || y == NullMarker || z == NullMarker)
        {
            return false;
        }

        if (!float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out float px)
            || !float.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out float py)
            || !float.TryParse(z, NumberStyles.Float, CultureInfo.InvariantCulture, out float pz))
        {
            return false;
        }

        position = new Vector3(px, py, pz);

        // A spawn at the exact origin is a placeholder row, not a place. Letting one through
        // would send the bot to the centre of the map.
        return WorldBounds.IsPlausible(position);
    }
}
