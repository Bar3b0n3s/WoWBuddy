using WoWBuddy.Common.Geometry;
using WoWBuddy.WorldData;
using Xunit;

namespace WoWBuddy.WorldData.Tests;

/// <summary>
/// Covers reading the export a user makes from their own server database.
/// </summary>
/// <remarks>
/// The export scripts cannot be run here — that needs a populated MySQL database — so these
/// reproduce what the mysql client emits: a header line and tab-separated rows, unquoted,
/// with NULL written out as the word.
/// </remarks>
public sealed class WorldDataReaderTests
{
    private static StringReader Tsv(params string[] lines) =>
        new(string.Join('\n', lines));

    [Fact]
    public void ReadsGameObjectSpawns()
    {
        var spawns = new List<GameObjectSpawn>();
        WorldDataReadResult result = WorldDataReader.ReadGameObjectSpawns(
            Tsv(
                "guid\tentry\tmap\tx\ty\tz",
                "12\t1617\t0\t-8913.23\t554.63\t93.79",
                "13\t1618\t1\t1629.36\t-4373.39\t31.26"),
            spawns);

        Assert.True(result.Success);
        Assert.Equal(2, spawns.Count);
        Assert.Equal(1617u, spawns[0].Entry);
        Assert.Equal(-8913.23f, spawns[0].Position.X, 2);
        Assert.Equal(1, spawns[1].MapId);
    }

    [Fact]
    public void ReadsCreatureTemplatesAndTheirServices()
    {
        var templates = new List<CreatureTemplate>();
        WorldDataReader.ReadCreatureTemplates(
            Tsv(
                "entry\tname\tnpcflag",
                "1234\tInnkeeper Allison\t65664",  // 0x10080: vendor | innkeeper
                "5678\tBlacksmith Argus\t4224"),   // 0x1080: vendor | repair
            templates);

        Assert.Equal(2, templates.Count);
        Assert.True(templates[0].IsVendor);
        Assert.True(templates[0].IsInnkeeper);
        Assert.True(templates[1].CanRepair);
        Assert.True(templates[1].IsVendor);
        Assert.False(templates[1].IsFlightMaster);
    }

    [Fact]
    public void RejectsAFileWhoseHeaderIsMissing()
    {
        // Running mysql without keeping the header line is an easy mistake and produces a
        // file that would otherwise silently lose its first row.
        var spawns = new List<GameObjectSpawn>();
        WorldDataReadResult result = WorldDataReader.ReadGameObjectSpawns(Tsv("12\t1617\t0"), spawns);

        Assert.False(result.Success);
        Assert.Equal(WorldDataError.UnexpectedColumns, result.Error);
    }

    [Fact]
    public void SkipsRowsItCannotParseRatherThanFailingTheWholeFile()
    {
        var spawns = new List<GameObjectSpawn>();
        WorldDataReadResult result = WorldDataReader.ReadGameObjectSpawns(
            Tsv(
                "guid\tentry\tmap\tx\ty\tz",
                "12\t1617\t0\t-8913.23\t554.63\t93.79",
                "not-a-number\t1618\t0\t1\t2\t3",
                "14\t1619\t0\t-8900\t550\t90"),
            spawns);

        Assert.True(result.Success);
        Assert.Equal(2, result.RowsRead);
        Assert.Equal(1, result.RowsSkipped);
    }

    [Fact]
    public void SkipsSpawnsAtTheOriginBecauseTheyArePlaceholders()
    {
        // A row at exactly (0,0,0) is an unfilled record, not a place. Following one would
        // send the bot to the centre of the map.
        var spawns = new List<GameObjectSpawn>();
        WorldDataReader.ReadGameObjectSpawns(
            Tsv("guid\tentry\tmap\tx\ty\tz", "12\t1617\t0\t0\t0\t0"), spawns);

        Assert.Empty(spawns);
    }

    [Fact]
    public void HandlesTheNullMarkerTheMysqlClientWrites()
    {
        var spawns = new List<GameObjectSpawn>();
        WorldDataReader.ReadGameObjectSpawns(
            Tsv("guid\tentry\tmap\tx\ty\tz", "12\t1617\t0\tNULL\tNULL\tNULL"), spawns);

        Assert.Empty(spawns);
    }

    [Fact]
    public void AnEmptyFileIsReportedRatherThanTreatedAsNoRows()
    {
        var spawns = new List<GameObjectSpawn>();
        WorldDataReadResult result = WorldDataReader.ReadGameObjectSpawns(new StringReader(""), spawns);

        Assert.Equal(WorldDataError.Missing, result.Error);
    }
}

public sealed class WorldDataSetTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "wowbuddy-worlddata", Guid.NewGuid().ToString("N"));

    public WorldDataSetTests() => Directory.CreateDirectory(_directory);

    private void Write(string fileName, params string[] lines) =>
        File.WriteAllText(Path.Combine(_directory, fileName), string.Join('\n', lines));

    private WorldDataSet Loaded()
    {
        Write(WorldDataSet.FileNames.GameObjectSpawns,
            "guid\tentry\tmap\tx\ty\tz",
            "1\t1617\t0\t-8900\t500\t90",   // near
            "2\t1617\t0\t-8500\t500\t90",   // far
            "3\t1618\t0\t-8905\t505\t90",   // near, different entry
            "4\t1617\t1\t-8900\t500\t90");  // another map

        Write(WorldDataSet.FileNames.GameObjectTemplates,
            "entry\tname\ttype\tlockId",
            "1617\tCopper Vein\t3\t1",
            "1618\tPeacebloom\t3\t2",
            "1619\tLarge Chest\t3\t3",
            "9999\tMailbox\t19\t0");

        Write(WorldDataSet.FileNames.CreatureSpawns,
            "guid\tentry\tmap\tx\ty\tz",
            "10\t555\t0\t-8890\t500\t90",
            "11\t666\t0\t-8700\t500\t90");

        Write(WorldDataSet.FileNames.CreatureTemplates,
            "entry\tname\tnpcflag",
            "555\tGeneral Goods\t128",       // vendor
            "666\tBlacksmith\t4224");        // vendor | repair

        var set = new WorldDataSet();
        set.LoadFrom(_directory);
        return set;
    }

    [Fact]
    public void LoadsEveryExportFile()
    {
        WorldDataSet set = Loaded();

        Assert.True(set.IsLoaded);
        Assert.Equal(4, set.GameObjectSpawnCount);
        Assert.Equal(4, set.GameObjectTemplateCount);
        Assert.Equal(2, set.CreatureSpawnCount);
        Assert.Equal(2, set.CreatureTemplateCount);
    }

    [Fact]
    public void MissingFilesAreReportedButNotFatal()
    {
        // A user who only wants gathering has no reason to export creatures.
        Write(WorldDataSet.FileNames.GameObjectSpawns,
            "guid\tentry\tmap\tx\ty\tz", "1\t1617\t0\t-8900\t500\t90");

        var set = new WorldDataSet();
        IReadOnlyList<string> problems = set.LoadFrom(_directory);

        Assert.True(set.IsLoaded);
        Assert.Contains(problems, p => p.Contains("creature-spawns", StringComparison.Ordinal));
    }

    [Fact]
    public void FindsNodesOnTheRightMapNearestFirst()
    {
        WorldDataSet set = Loaded();

        IReadOnlyList<GameObjectSpawn> nodes = set.FindNodes(
            mapId: 0,
            entries: new HashSet<uint> { 1617 },
            origin: new Vector3(-8900f, 500f, 90f));

        Assert.Equal(2, nodes.Count);
        Assert.Equal(1u, nodes[0].Guid);
        Assert.Equal(2u, nodes[1].Guid);
    }

    [Fact]
    public void RespectsTheSearchRadius()
    {
        WorldDataSet set = Loaded();

        IReadOnlyList<GameObjectSpawn> nodes = set.FindNodes(
            0, new HashSet<uint> { 1617 }, new Vector3(-8900f, 500f, 90f), maxDistance: 100f);

        Assert.Single(nodes);
    }

    [Fact]
    public void SearchesTemplatesByNameBecauseNoNodeListShipsWithTheBot()
    {
        // Inventing a list of node entry ids would be exactly the sort of unverified fact
        // this project refuses; the user finds them in their own database instead.
        WorldDataSet set = Loaded();

        IReadOnlyList<GameObjectTemplate> found = set.FindTemplatesByName("vein");

        Assert.Single(found);
        Assert.Equal(1617u, found[0].Entry);
    }

    [Fact]
    public void NameSearchSkipsThingsThatAreNotGatherable()
    {
        WorldDataSet set = Loaded();

        Assert.Empty(set.FindTemplatesByName("Mailbox"));
        Assert.Single(set.FindTemplatesByName("Mailbox", gatherableOnly: false));
    }

    [Fact]
    public void FindsTheNearestServiceNpc()
    {
        // This is what gives phase 5's errands somewhere to go.
        WorldDataSet set = Loaded();

        IReadOnlyList<ServiceNpc> repairers = set.FindServices(
            0, new Vector3(-8900f, 500f, 90f), template => template.CanRepair);

        Assert.Single(repairers);
        Assert.Equal("Blacksmith", repairers[0].Name);

        IReadOnlyList<ServiceNpc> vendors = set.FindServices(
            0, new Vector3(-8900f, 500f, 90f), template => template.IsVendor);

        Assert.Equal(2, vendors.Count);
        Assert.Equal("General Goods", vendors[0].Name);
    }

    [Fact]
    public void FindsNothingOnAMapWithNoData()
    {
        WorldDataSet set = Loaded();

        Assert.Empty(set.FindServices(571, Vector3.Zero, _ => true));
        Assert.Empty(set.FindNodes(571, new HashSet<uint> { 1617 }, Vector3.Zero));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
