using WoWBuddy.Common.Geometry;
using WoWBuddy.WorldData;
using Xunit;

namespace WoWBuddy.WorldData.Tests;

/// <summary>
/// Covers the map the bot builds by playing, which is what a user who has no access to a
/// server database actually relies on.
/// </summary>
public sealed class WorldMemoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Vector3 Origin = new(-8900f, 500f, 90f);

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "wowbuddy-memory", Guid.NewGuid().ToString("N"));

    public WorldMemoryTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void RemembersSomethingItHasSeen()
    {
        var memory = new WorldMemory();

        Assert.True(memory.Remember(RememberedKind.Vendor, 555, 0, Origin, "General Goods", Now));
        Assert.Equal(1, memory.Count);
    }

    [Fact]
    public void RefusesToRememberAPositionTheClientCouldNotSupply()
    {
        // The important one. Game object positions depend on an offset worked out at attach;
        // when that fails the position reads as zero, and writing that down would teach the
        // bot to walk to the middle of the map.
        var memory = new WorldMemory();

        Assert.False(memory.Remember(RememberedKind.Node, 1617, 0, Vector3.Zero, "Copper Vein", Now));
        Assert.False(memory.Remember(
            RememberedKind.Node, 1617, 0, new Vector3(float.NaN, 0f, 0f), "Copper Vein", Now));
        Assert.Equal(0, memory.Count);
    }

    [Fact]
    public void DoesNotRecordTheSameThingTwice()
    {
        // Nodes respawn a little off and NPCs wander, so exact coordinates would fill the
        // file with duplicates of one vendor.
        var memory = new WorldMemory();
        memory.Remember(RememberedKind.Vendor, 555, 0, Origin, "General Goods", Now);

        bool second = memory.Remember(
            RememberedKind.Vendor, 555, 0, new Vector3(-8896f, 502f, 90f), "General Goods", Now);

        Assert.False(second);
        Assert.Equal(1, memory.Count);
    }

    [Fact]
    public void TreatsThingsFurtherApartAsSeparatePlaces()
    {
        var memory = new WorldMemory();
        memory.Remember(RememberedKind.Node, 1617, 0, Origin, "Copper Vein", Now);
        memory.Remember(RememberedKind.Node, 1617, 0, new Vector3(-8800f, 500f, 90f), "Copper Vein", Now);

        Assert.Equal(2, memory.Count);
    }

    [Fact]
    public void KeepsTheOriginalPositionWhenSeeingSomethingAgain()
    {
        // Otherwise the recorded position drifts towards wherever the character happened to
        // be standing each time.
        var memory = new WorldMemory();
        memory.Remember(RememberedKind.Vendor, 555, 0, Origin, "General Goods", Now);
        memory.Remember(RememberedKind.Vendor, 555, 0, new Vector3(-8895f, 503f, 90f), "General Goods", Now);

        Assert.Equal(Origin.X, memory.Places[0].X, 2);
    }

    [Fact]
    public void SeparatesPlacesByMap()
    {
        var memory = new WorldMemory();
        memory.Remember(RememberedKind.Vendor, 555, 0, Origin, "A", Now);
        memory.Remember(RememberedKind.Vendor, 555, 1, Origin, "A", Now);

        Assert.Equal(2, memory.Count);
        Assert.Single(memory.Nearest(RememberedKind.Vendor, 0, Origin, Now));
    }

    [Fact]
    public void FindsTheNearestPlaceOfAKind()
    {
        // This is what gives an errand somewhere to go without any external data.
        var memory = new WorldMemory();
        memory.Remember(RememberedKind.Repair, 666, 0, new Vector3(-8700f, 500f, 90f), "Far", Now);
        memory.Remember(RememberedKind.Repair, 667, 0, new Vector3(-8890f, 500f, 90f), "Near", Now);
        memory.Remember(RememberedKind.Vendor, 555, 0, Origin, "Not a repairer", Now);

        RememberedPlace? nearest = memory.NearestOrDefault(RememberedKind.Repair, 0, Origin, Now);

        Assert.Equal("Near", nearest?.Name);
    }

    [Fact]
    public void ReportsNothingWhenItHasNotLearnedAnythingYet()
    {
        // The state a fresh install is in, and the bot has to cope with it rather than
        // assuming a database.
        var memory = new WorldMemory();

        Assert.Null(memory.NearestOrDefault(RememberedKind.Vendor, 0, Origin, Now));
    }

    [Fact]
    public void IgnoresAndForgetsPlacesNotSeenForAVeryLongTime()
    {
        // Servers get patched and NPCs get moved. A remembered place that is no longer there
        // is worse than no memory, because it keeps sending the character back.
        var memory = new WorldMemory();
        memory.Remember(RememberedKind.Vendor, 555, 0, Origin, "Gone", Now);

        DateTimeOffset muchLater = Now + WorldMemory.Staleness + TimeSpan.FromDays(1);

        Assert.Empty(memory.Nearest(RememberedKind.Vendor, 0, Origin, muchLater));
        Assert.Equal(1, memory.Forget(muchLater));
        Assert.Equal(0, memory.Count);
    }

    [Fact]
    public void SurvivesBeingSavedAndLoaded()
    {
        string path = Path.Combine(_directory, "map.json");

        var saved = new WorldMemory();
        saved.Remember(RememberedKind.Node, 1617, 0, Origin, "Copper Vein", Now);
        saved.Remember(RememberedKind.Mailbox, 9999, 0, new Vector3(-8880f, 505f, 90f), "Mailbox", Now);

        Assert.True(saved.Save(path));
        Assert.False(saved.IsDirty);

        WorldMemory loaded = WorldMemory.Load(path);

        Assert.Equal(2, loaded.Count);
        Assert.Equal(Origin.X, loaded.NearestOrDefault(RememberedKind.Node, 0, Origin, Now)!.X, 2);
    }

    [Fact]
    public void StartsEmptyRatherThanFailingOnACorruptFile()
    {
        // Losing the map costs nothing permanent: the bot relearns as it plays.
        string path = Path.Combine(_directory, "broken.json");
        File.WriteAllText(path, "{ this is not json");

        Assert.Equal(0, WorldMemory.Load(path).Count);
    }

    [Fact]
    public void LoadingSomethingThatWasNeverSavedIsNormal()
    {
        Assert.Equal(0, WorldMemory.Load(Path.Combine(_directory, "absent.json")).Count);
    }

    [Fact]
    public void CanBeSeededFromAnExportForPeopleWhoHaveOne()
    {
        // Optional. Someone running their own server can skip the learning; nobody needs to.
        string exportDirectory = Path.Combine(_directory, "export");
        Directory.CreateDirectory(exportDirectory);

        File.WriteAllText(
            Path.Combine(exportDirectory, WorldDataSet.FileNames.GameObjectSpawns),
            "guid\tentry\tmap\tx\ty\tz\n1\t1617\t0\t-8890\t500\t90\n2\t1618\t0\t-8880\t500\t90");

        File.WriteAllText(
            Path.Combine(exportDirectory, WorldDataSet.FileNames.GameObjectTemplates),
            "entry\tname\ttype\tlockId\n1617\tCopper Vein\t3\t1\n1618\tPeacebloom\t3\t2");

        var worldData = new WorldDataSet();
        worldData.LoadFrom(exportDirectory);

        var memory = new WorldMemory();
        int added = memory.SeedFrom(worldData, new HashSet<uint> { 1617 }, Now);

        Assert.Equal(1, added);
        Assert.Equal(1617u, memory.Places[0].Entry);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
