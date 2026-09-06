using WoWBuddy.Navigation.Data;
using Xunit;

namespace WoWBuddy.Navigation.Tests;

/// <summary>
/// Covers how a folder of extracted navigation data is turned into a mesh, and what happens
/// when it is not the folder the user thought it was.
/// </summary>
public sealed class NavMeshLoaderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "wowbuddy-nav-tests", Guid.NewGuid().ToString("N"));

    public NavMeshLoaderTests() => Directory.CreateDirectory(_directory);

    private void WriteParams(int mapId) =>
        File.WriteAllBytes(
            Path.Combine(_directory, MmapFormat.MapFileName(mapId)),
            MmapFileBuilder.BuildNavMeshParams());

    private void WriteTile(int mapId, int tileY, int tileX, MmapFlavour flavour) =>
        File.WriteAllBytes(
            Path.Combine(_directory, MmapFormat.TileFileName(mapId, tileY, tileX)),
            MmapFileBuilder.BuildTile(flavour, MmapFileBuilder.FakePayload()));

    [Fact]
    public void AMissingParametersFileIsReportedClearly()
    {
        NavMeshLoadResult result = new NavMeshLoader(_directory).Load(0);

        Assert.False(result.Success);
        Assert.Contains(result.Problems, p => p.Contains("000.mmap", StringComparison.Ordinal));
    }

    [Fact]
    public void AMapWithNoTilesIsReportedSeparatelyFromAMissingMap()
    {
        // Distinguishing these matters: one means "you did not extract this continent",
        // the other means "your extraction stopped part way".
        WriteParams(0);

        NavMeshLoadResult result = new NavMeshLoader(_directory).Load(0);

        Assert.False(result.Success);
        Assert.Contains(result.Problems, p => p.Contains("no tiles were extracted", StringComparison.Ordinal));
    }

    [Fact]
    public void MixingTilesFromBothExtractorsIsRefused()
    {
        // The most valuable check here. Both files are individually well formed, so nothing
        // but a cross-tile comparison catches this, and loading the matching subset would
        // give a mesh with silent holes.
        WriteParams(0);
        WriteTile(0, 30, 30, MmapFlavour.TrinityCore);
        WriteTile(0, 30, 31, MmapFlavour.AzerothCore);

        NavMeshLoadResult result = new NavMeshLoader(_directory).Load(0);

        Assert.False(result.Success);
        Assert.Contains(result.Problems, p =>
            p.Contains("cannot be mixed", StringComparison.Ordinal));
    }

    [Fact]
    public void TilesBelongingToOtherMapsAreIgnored()
    {
        WriteParams(0);
        WriteTile(0, 30, 30, MmapFlavour.TrinityCore);
        WriteTile(1, 30, 30, MmapFlavour.TrinityCore);
        WriteTile(571, 30, 30, MmapFlavour.TrinityCore);

        NavMeshLoadResult result = new NavMeshLoader(_directory).Load(0);

        // The filler payloads are not real Detour tiles, so all are rejected; what matters is
        // that only the one belonging to map 0 was even considered.
        Assert.Equal(1, result.TilesRejected);
    }

    [Fact]
    public void TooManyUnreadableTilesFailTheWholeMap()
    {
        // A couple of bad tiles is a hole in the map. Dozens is a broken extraction, and
        // saying so saves the user debugging a pathing problem that is really a data problem.
        WriteParams(0);
        for (int i = 0; i < NavMeshLoader.MaxToleratedBadTiles + 2; i++)
        {
            WriteTile(0, 30, 30 + i, MmapFlavour.TrinityCore);
        }

        NavMeshLoadResult result = new NavMeshLoader(_directory).Load(0);

        Assert.False(result.Success);
        Assert.Contains(result.Problems, p =>
            p.Contains("too many to be incidental", StringComparison.Ordinal));
    }

    [Fact]
    public void ACorruptParametersFileIsReportedRatherThanCrashing()
    {
        File.WriteAllBytes(Path.Combine(_directory, MmapFormat.MapFileName(0)), new byte[4]);

        NavMeshLoadResult result = new NavMeshLoader(_directory).Load(0);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Problems);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
