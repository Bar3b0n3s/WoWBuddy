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

            into.Add(new CreatureTemplate(entry, fields[1], flags));
            return true;
        });
    }

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
