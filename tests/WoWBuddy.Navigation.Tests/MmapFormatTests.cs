using WoWBuddy.Navigation.Data;
using Xunit;

namespace WoWBuddy.Navigation.Tests;

/// <summary>
/// Covers reading navigation data produced by either supported extractor.
/// </summary>
/// <remarks>
/// The two formats differ only in how much header sits in front of the Detour tile, which
/// makes getting it wrong silent: a reader that assumes TrinityCore's 20 bytes and is handed
/// AzerothCore's file takes 36 bytes of Recast settings as the start of the mesh. These tests
/// exist so that neither flavour can regress unnoticed.
/// </remarks>
public sealed class MmapFormatTests
{
    [Fact]
    public void NavMeshParamsRoundTripFromTheOnDiskLayout()
    {
        byte[] bytes = MmapFileBuilder.BuildNavMeshParams(
            originX: -17066.666f, originY: 0f, originZ: -17066.666f,
            tileWidth: 533.33333f, tileHeight: 533.33333f, maxTiles: 4096, maxPolys: 32768);

        Assert.True(MmapReader.TryReadNavMeshParams(bytes, out var parameters, out MmapReadError error));

        Assert.Equal(MmapReadError.None, error);
        Assert.Equal(-17066.666f, parameters.orig.X, 2);
        Assert.Equal(533.33333f, parameters.tileWidth, 3);
        Assert.Equal(4096, parameters.maxTiles);
        Assert.Equal(32768, parameters.maxPolys);
    }

    [Fact]
    public void NavMeshParamsFileIsExactlyTwentyEightBytes()
    {
        // Three origin floats, two tile dimensions, two capacities. The whole file.
        Assert.Equal(28, MmapFormat.NavMeshParamsSize);
        Assert.Equal(28, MmapFileBuilder.BuildNavMeshParams().Length);
    }

    [Fact]
    public void ATruncatedParamsFileIsRejected()
    {
        byte[] bytes = MmapFileBuilder.BuildNavMeshParams()[..20];

        Assert.False(MmapReader.TryReadNavMeshParams(bytes, out _, out MmapReadError error));
        Assert.Equal(MmapReadError.Truncated, error);
    }

    [Theory]
    [InlineData(MmapFlavour.TrinityCore, MmapFormat.TrinityCoreHeaderSize, MmapFormat.TrinityCoreVersion)]
    [InlineData(MmapFlavour.AzerothCore, MmapFormat.AzerothCoreHeaderSize, MmapFormat.AzerothCoreVersion)]
    public void BothExtractorsAreRecognisedAndTheirHeaderSizesDiffer(
        MmapFlavour flavour, int expectedHeaderSize, uint expectedVersion)
    {
        byte[] payload = MmapFileBuilder.FakePayload();
        byte[] file = MmapFileBuilder.BuildTile(flavour, payload);

        Assert.True(MmapReader.TryReadTileHeader(file, out MmapTileHeader header, out MmapReadError error));

        Assert.Equal(MmapReadError.None, error);
        Assert.Equal(flavour, header.Flavour);
        Assert.Equal(expectedVersion, header.GeneratorVersion);
        Assert.Equal(expectedHeaderSize, header.HeaderSize);
        Assert.Equal((uint)payload.Length, header.PayloadSize);
        Assert.True(header.UsesLiquids);
    }

    [Fact]
    public void TheAzerothCoreHeaderIsThirtySixBytesLongerThanTrinityCores()
    {
        // The whole reason the two cannot be read by the same fixed offset.
        Assert.Equal(36, MmapFormat.AzerothCoreHeaderSize - MmapFormat.TrinityCoreHeaderSize);
    }

    [Theory]
    [InlineData(MmapFlavour.TrinityCore)]
    [InlineData(MmapFlavour.AzerothCore)]
    public void ThePayloadIsFoundAtTheRightOffsetForEachFlavour(MmapFlavour flavour)
    {
        // The decisive check. The payload here is a recognisable pattern, so reading it from
        // the wrong offset produces different bytes rather than an error.
        byte[] payload = MmapFileBuilder.FakePayload(length: 32, seed: 0x10);
        byte[] file = MmapFileBuilder.BuildTile(flavour, payload);

        Assert.True(MmapReader.TryReadTileHeader(file, out MmapTileHeader header, out _));

        byte[] extracted = file.AsSpan(header.HeaderSize, (int)header.PayloadSize).ToArray();
        Assert.Equal(payload, extracted);
    }

    [Fact]
    public void ATrinityCoreReadOfAnAzerothCoreFileWouldHaveTakenTheWrongBytes()
    {
        // Demonstrates the failure this format detection prevents, rather than merely
        // asserting that detection works.
        byte[] payload = MmapFileBuilder.FakePayload(length: 32, seed: 0x10);
        byte[] file = MmapFileBuilder.BuildTile(MmapFlavour.AzerothCore, payload);

        byte[] asIfTrinityCore = file.AsSpan(MmapFormat.TrinityCoreHeaderSize, 32).ToArray();

        Assert.NotEqual(payload, asIfTrinityCore);
    }

    [Fact]
    public void AFileThatIsNotNavigationDataIsRejected()
    {
        byte[] file = MmapFileBuilder.BuildTile(
            MmapFlavour.TrinityCore, MmapFileBuilder.FakePayload(), magic: 0x12345678);

        Assert.False(MmapReader.TryReadTileHeader(file, out _, out MmapReadError error));
        Assert.Equal(MmapReadError.BadMagic, error);
    }

    [Fact]
    public void AnUnknownExtractorVersionIsRejectedWithBothSupportedVersionsNamed()
    {
        byte[] file = MmapFileBuilder.BuildTile(
            MmapFlavour.TrinityCore, MmapFileBuilder.FakePayload(), generatorVersion: 9);

        Assert.False(MmapReader.TryReadTileHeader(file, out _, out MmapReadError error));
        Assert.Equal(MmapReadError.UnknownGeneratorVersion, error);

        string message = MmapReader.Describe(error, "00043032.mmtile");
        Assert.Contains("TrinityCore", message, StringComparison.Ordinal);
        Assert.Contains("AzerothCore", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DataBuiltForADifferentDetourVersionIsRejected()
    {
        byte[] file = MmapFileBuilder.BuildTile(
            MmapFlavour.TrinityCore, MmapFileBuilder.FakePayload(), detourVersion: 16);

        Assert.False(MmapReader.TryReadTileHeader(file, out _, out MmapReadError error));
        Assert.Equal(MmapReadError.UnsupportedDetourVersion, error);
    }

    [Fact]
    public void ATileClaimingMoreDataThanItContainsIsRejected()
    {
        // What an interrupted extraction leaves behind.
        byte[] file = MmapFileBuilder.BuildTile(
            MmapFlavour.TrinityCore, MmapFileBuilder.FakePayload(64), declaredPayloadSize: 4096);

        Assert.False(MmapReader.TryReadTileHeader(file, out _, out MmapReadError error));
        Assert.Equal(MmapReadError.PayloadSizeMismatch, error);
    }

    [Fact]
    public void AFileShorterThanTheCommonHeaderIsRejected()
    {
        Assert.False(MmapReader.TryReadTileHeader(new byte[8], out _, out MmapReadError error));
        Assert.Equal(MmapReadError.Truncated, error);
    }

    [Fact]
    public void GarbageThatPassesTheHeaderIsStillRejectedByDetour()
    {
        // The header can be perfectly well formed and the tile inside still be nonsense.
        byte[] file = MmapFileBuilder.BuildTile(MmapFlavour.TrinityCore, MmapFileBuilder.FakePayload(64));

        Assert.False(MmapReader.TryReadTile(file, out _, out _, out MmapReadError error));
        Assert.Equal(MmapReadError.MalformedTile, error);
    }

    [Theory]
    [InlineData(0, "000.mmap")]
    [InlineData(1, "001.mmap")]
    [InlineData(571, "571.mmap")]
    public void MapFileNamesArePaddedToThreeDigits(int mapId, string expected) =>
        Assert.Equal(expected, MmapFormat.MapFileName(mapId));

    [Theory]
    [InlineData(0, 43, 32, "0004332.mmtile")]
    [InlineData(571, 7, 8, "5710708.mmtile")]
    public void TileFileNamesPutTheMapIdFirst(int mapId, int tileY, int tileX, string expected) =>
        Assert.Equal(expected, MmapFormat.TileFileName(mapId, tileY, tileX));

    [Theory]
    [InlineData("0004332.mmtile", 0, true)]
    [InlineData("0004332.mmtile", 1, false)]
    [InlineData("5714332.mmtile", 571, true)]
    [InlineData("000.mmap", 0, false)]
    [InlineData("readme.txt", 0, false)]
    public void TileFilesAreMatchedToTheirMap(string fileName, int mapId, bool expected) =>
        Assert.Equal(expected, MmapFormat.IsTileOfMap(fileName, mapId));
}
