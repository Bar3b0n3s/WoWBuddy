using System.Buffers.Binary;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Io;

namespace WoWBuddy.Navigation.Data;

/// <summary>The header at the front of a <c>.mmtile</c> file.</summary>
/// <param name="Flavour">Which extractor wrote it.</param>
/// <param name="GeneratorVersion">The generator version it declares.</param>
/// <param name="DetourVersion">The Detour tile format version it declares.</param>
/// <param name="PayloadSize">Bytes of Detour tile data following the header.</param>
/// <param name="UsesLiquids">Whether the tile was built with liquid surfaces included.</param>
public readonly record struct MmapTileHeader(
    MmapFlavour Flavour,
    uint GeneratorVersion,
    uint DetourVersion,
    uint PayloadSize,
    bool UsesLiquids)
{
    /// <summary>Bytes of header preceding the payload.</summary>
    public int HeaderSize => MmapFormat.HeaderSizeFor(Flavour);
}

/// <summary>Why a navigation data file could not be read.</summary>
public enum MmapReadError
{
    /// <summary>Nothing went wrong.</summary>
    None = 0,

    /// <summary>The file is shorter than its own header.</summary>
    Truncated,

    /// <summary>The file does not start with the expected magic.</summary>
    BadMagic,

    /// <summary>The generator version is not one this bot knows how to read.</summary>
    UnknownGeneratorVersion,

    /// <summary>The Detour tile format version is not the one 3.3.5 data uses.</summary>
    UnsupportedDetourVersion,

    /// <summary>The declared payload is larger than the bytes actually present.</summary>
    PayloadSizeMismatch,

    /// <summary>Detour rejected the tile data itself.</summary>
    MalformedTile,
}

/// <summary>
/// Reads the navigation data files the user extracted from their own client.
/// </summary>
/// <remarks>
/// Every method reports failure rather than throwing on malformed input. These files come
/// from an extractor the bot did not run, on data the bot has never seen, and a half-finished
/// extraction is a completely ordinary thing to encounter.
/// </remarks>
public static class MmapReader
{
    /// <summary>
    /// Reads the Detour navmesh parameters from a <c>.mmap</c> file.
    /// </summary>
    /// <remarks>
    /// The file is exactly one <c>dtNavMeshParams</c> and nothing else: origin, tile size and
    /// the capacities the mesh was built for.
    /// </remarks>
    public static bool TryReadNavMeshParams(
        ReadOnlySpan<byte> bytes,
        out DtNavMeshParams parameters,
        out MmapReadError error)
    {
        parameters = default;

        if (bytes.Length < MmapFormat.NavMeshParamsSize)
        {
            error = MmapReadError.Truncated;
            return false;
        }

        parameters = new DtNavMeshParams
        {
            orig = new RcVec3f(
                BinaryPrimitives.ReadSingleLittleEndian(bytes),
                BinaryPrimitives.ReadSingleLittleEndian(bytes[4..]),
                BinaryPrimitives.ReadSingleLittleEndian(bytes[8..])),
            tileWidth = BinaryPrimitives.ReadSingleLittleEndian(bytes[12..]),
            tileHeight = BinaryPrimitives.ReadSingleLittleEndian(bytes[16..]),
            maxTiles = BinaryPrimitives.ReadInt32LittleEndian(bytes[20..]),
            maxPolys = BinaryPrimitives.ReadInt32LittleEndian(bytes[24..]),
        };

        error = MmapReadError.None;
        return true;
    }

    /// <summary>
    /// Reads a tile header and identifies which extractor produced it.
    /// </summary>
    /// <remarks>
    /// The first twenty bytes are the same in both flavours, and the generator version inside
    /// them is what says how many more bytes of header there are. That is why detection is
    /// possible at all, and why it has to happen before the payload can be located.
    /// </remarks>
    public static bool TryReadTileHeader(
        ReadOnlySpan<byte> bytes,
        out MmapTileHeader header,
        out MmapReadError error)
    {
        header = default;

        if (bytes.Length < MmapFormat.CommonHeaderSize)
        {
            error = MmapReadError.Truncated;
            return false;
        }

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if (magic != MmapFormat.Magic)
        {
            error = MmapReadError.BadMagic;
            return false;
        }

        uint detourVersion = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        uint generatorVersion = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);
        uint payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]);
        bool usesLiquids = bytes[16] != 0;

        MmapFlavour flavour = MmapFormat.FlavourFor(generatorVersion);
        if (flavour == MmapFlavour.Unknown)
        {
            error = MmapReadError.UnknownGeneratorVersion;
            return false;
        }

        if (detourVersion != MmapFormat.DetourNavMeshVersion)
        {
            error = MmapReadError.UnsupportedDetourVersion;
            return false;
        }

        header = new MmapTileHeader(flavour, generatorVersion, detourVersion, payloadSize, usesLiquids);

        if (bytes.Length < header.HeaderSize + payloadSize)
        {
            error = MmapReadError.PayloadSizeMismatch;
            return false;
        }

        error = MmapReadError.None;
        return true;
    }

    /// <summary>
    /// Reads a whole <c>.mmtile</c> file into a Detour tile.
    /// </summary>
    /// <remarks>
    /// The payload is a Detour tile serialised by the extractor's C++ Detour, read back by
    /// the C# port. That works because the port reads the original binary layout, and because
    /// both sides agree on six vertices per polygon; passing a different number here would
    /// misread every polygon in the tile rather than fail.
    /// </remarks>
    public static bool TryReadTile(
        ReadOnlySpan<byte> bytes,
        out DtMeshData? tile,
        out MmapTileHeader header,
        out MmapReadError error)
    {
        tile = null;

        if (!TryReadTileHeader(bytes, out header, out error))
        {
            return false;
        }

        return TryReadTilePayload(bytes, header, out tile, out error);
    }

    /// <summary>
    /// Reads the Detour tile that follows an already-parsed header.
    /// </summary>
    /// <remarks>
    /// Separate from the header read so that a caller can act on what the header says before
    /// committing to the payload. That matters for the flavour check: which extractor wrote a
    /// tile is knowable from its header alone, and must stay knowable even when the tile
    /// itself turns out to be corrupt.
    /// </remarks>
    public static bool TryReadTilePayload(
        ReadOnlySpan<byte> bytes,
        MmapTileHeader header,
        out DtMeshData? tile,
        out MmapReadError error)
    {
        tile = null;

        if (bytes.Length < header.HeaderSize + header.PayloadSize)
        {
            error = MmapReadError.PayloadSizeMismatch;
            return false;
        }

        byte[] payload = bytes.Slice(header.HeaderSize, (int)header.PayloadSize).ToArray();

        try
        {
            tile = new DtMeshDataReader().Read(new RcByteBuffer(payload), MmapFormat.VertsPerPolygon);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or IndexOutOfRangeException
                                      or ArgumentOutOfRangeException or InvalidOperationException)
        {
            error = MmapReadError.MalformedTile;
            return false;
        }

        if (tile is null)
        {
            error = MmapReadError.MalformedTile;
            return false;
        }

        error = MmapReadError.None;
        return true;
    }

    /// <summary>A sentence explaining a read failure, suitable for showing a user.</summary>
    public static string Describe(MmapReadError error, string fileName) => error switch
    {
        MmapReadError.None => $"{fileName} read successfully.",
        MmapReadError.Truncated =>
            $"{fileName} is shorter than its own header. The extraction was probably interrupted; re-run it.",
        MmapReadError.BadMagic =>
            $"{fileName} does not start with the expected 'MMAP' marker, so it is not navigation data.",
        MmapReadError.UnknownGeneratorVersion =>
            $"{fileName} was written by an extractor this bot does not recognise. Supported: " +
            $"TrinityCore (v{MmapFormat.TrinityCoreVersion}) and AzerothCore (v{MmapFormat.AzerothCoreVersion}).",
        MmapReadError.UnsupportedDetourVersion =>
            $"{fileName} declares a Detour tile version other than {MmapFormat.DetourNavMeshVersion}, " +
            "which is what 3.3.5 navigation data uses. It may have been built for a different expansion.",
        MmapReadError.PayloadSizeMismatch =>
            $"{fileName} claims more tile data than the file contains. It is truncated; re-extract it.",
        MmapReadError.MalformedTile =>
            $"{fileName} has a valid header but Detour could not read the tile inside it.",
        _ => $"{fileName} could not be read.",
    };
}
