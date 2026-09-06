using WoWBuddy.Common.Logging;

namespace WoWBuddy.WorldData.Dbc;

/// <summary>
/// One row of <c>FactionTemplate.dbc</c>: how a faction feels about everyone else.
/// </summary>
/// <param name="Id">The faction template id, which is what a creature carries.</param>
/// <param name="Faction">The faction it belongs to.</param>
/// <param name="OwnGroup">The mask of groups it is a member of.</param>
/// <param name="FriendGroup">The mask of groups it likes.</param>
/// <param name="EnemyGroup">The mask of groups it attacks.</param>
/// <param name="Enemies">Up to four factions it attacks by name.</param>
/// <param name="Friends">Up to four factions it likes by name.</param>
public readonly record struct FactionTemplate(
    int Id,
    int Faction,
    int OwnGroup,
    int FriendGroup,
    int EnemyGroup,
    IReadOnlyList<int> Enemies,
    IReadOnlyList<int> Friends)
{
    /// <summary>
    /// True when this faction attacks <paramref name="other"/> on sight.
    /// </summary>
    /// <remarks>
    /// The order is what matters and is easy to get backwards: a named friend beats a hostile
    /// group mask, which is how guards are hostile to the other side in general and friendly to
    /// specific neutral factions within it. Checking the masks first would make the bot attack
    /// things it should not.
    /// </remarks>
    public bool IsHostileTo(FactionTemplate other)
    {
        if (Friends.Contains(other.Faction) && other.Faction != 0)
        {
            return false;
        }

        if (Enemies.Contains(other.Faction) && other.Faction != 0)
        {
            return true;
        }

        if ((FriendGroup & other.OwnGroup) != 0)
        {
            return false;
        }

        return (EnemyGroup & other.OwnGroup) != 0;
    }
}

/// <summary>
/// The faction relationships, read from the client's own data.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is what closes hostility properly.</b> The server database says which faction
/// template a creature carries; only this file says what that template attacks. Without it the
/// bot falls back to an approximation that excludes players and anything wearing NPC flags and
/// treats the rest as fair game — which offers neutral critters as targets.
/// </para>
/// <para>
/// The layout below is the 3.3.5a one: fourteen fields, with the four enemy factions at index
/// five and the four friends at nine. A file whose field count does not match is refused rather
/// than read, because reading the wrong columns would produce a hostility table that looks
/// entirely reasonable and is wrong.
/// </para>
/// </remarks>
public sealed class FactionTemplates
{
    /// <summary>How many fields the 3.3.5a file has.</summary>
    public const int ExpectedFieldCount = 14;

    /// <summary>The usual name of the file.</summary>
    public const string FileName = "FactionTemplate.dbc";

    private readonly Dictionary<int, FactionTemplate> _byId = [];

    /// <summary>How many templates were read.</summary>
    public int Count => _byId.Count;

    /// <summary>True when anything was loaded.</summary>
    public bool IsLoaded => _byId.Count > 0;

    /// <summary>Reads the file.</summary>
    /// <param name="dbc">The parsed DBC.</param>
    /// <param name="templates">What it held.</param>
    /// <param name="error">Why it could not be used.</param>
    public static bool TryRead(DbcFile dbc, out FactionTemplates templates, out string error)
    {
        ArgumentNullException.ThrowIfNull(dbc);

        templates = new FactionTemplates();
        error = string.Empty;

        if (dbc.FieldCount != ExpectedFieldCount)
        {
            error = $"{FileName} has {dbc.FieldCount} fields; the 3.3.5a one has "
                + $"{ExpectedFieldCount}. Reading the wrong columns would produce a hostility "
                + "table that looks reasonable and is wrong, so it has not been read.";

            return false;
        }

        for (int record = 0; record < dbc.RecordCount; record++)
        {
            int[] enemies =
            [
                dbc.GetInt(record, 5), dbc.GetInt(record, 6),
                dbc.GetInt(record, 7), dbc.GetInt(record, 8),
            ];

            int[] friends =
            [
                dbc.GetInt(record, 9), dbc.GetInt(record, 10),
                dbc.GetInt(record, 11), dbc.GetInt(record, 12),
            ];

            FactionTemplate template = new(
                dbc.GetInt(record, 0),
                dbc.GetInt(record, 1),
                dbc.GetInt(record, 3),
                dbc.GetInt(record, 4),
                dbc.GetInt(record, 2),
                enemies,
                friends);

            templates._byId[template.Id] = template;
        }

        Log.For<FactionTemplates>().Information(
            "Read {Count} faction templates from {File}", templates.Count, FileName);

        return true;
    }

    /// <summary>Reads the file from a folder of extracted DBCs.</summary>
    public static bool TryReadFrom(string directory, out FactionTemplates templates, out string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        templates = new FactionTemplates();

        if (!DbcFile.TryReadFile(Path.Combine(directory, FileName), out DbcFile? dbc, out error))
        {
            return false;
        }

        return TryRead(dbc!, out templates, out error);
    }

    /// <summary>A template by id, or null when the file did not have it.</summary>
    public FactionTemplate? Find(int id) =>
        _byId.TryGetValue(id, out FactionTemplate template) ? template : null;

    /// <summary>
    /// True when a creature attacks the character on sight.
    /// </summary>
    /// <remarks>
    /// Unknown on either side answers false. A creature the file does not describe is not
    /// something to attack on the strength of a failed lookup.
    /// </remarks>
    public bool IsHostile(int creatureTemplateId, int myTemplateId)
    {
        if (Find(creatureTemplateId) is not { } creature || Find(myTemplateId) is not { } me)
        {
            return false;
        }

        return creature.IsHostileTo(me);
    }
}
