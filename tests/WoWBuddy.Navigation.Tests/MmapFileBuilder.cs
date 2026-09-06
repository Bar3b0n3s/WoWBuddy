using System.Buffers.Binary;
using WoWBuddy.Navigation.Data;

namespace WoWBuddy.Navigation.Tests;

/// <summary>
/// Writes navigation data files in the exact layouts the two extractors produce.
/// </summary>
/// <remarks>
/// <para>
/// The reader has to cope with data produced by tools this project does not ship and cannot
/// run here, so the tests reproduce their output byte for byte from the published format
/// instead. That is what makes it possible to check the AzerothCore path at all: without it,
/// the 36-byte difference in header size would only be discovered by a user.
/// </para>
/// <para>
/// The Detour payload is opaque filler. These tests are about the wrapper — magic, versions,
/// header size and payload location — because that is the part that differs between the two
/// and the part this project is responsible for.
/// </para>
/// </remarks>
public static class MmapFileBuilder
{
    /// <summary>Writes a <c>.mmap</c> parameters file.</summary>
    public static byte[] BuildNavMeshParams(
        float originX = -17066.666f,
        float originY = 0f,
        float originZ = -17066.666f,
        float tileWidth = 533.33333f,
        float tileHeight = 533.33333f,
        int maxTiles = 4096,
        int maxPolys = 32768)
    {
        byte[] bytes = new byte[MmapFormat.NavMeshParamsSize];
        Span<byte> span = bytes;

        BinaryPrimitives.WriteSingleLittleEndian(span, originX);
        BinaryPrimitives.WriteSingleLittleEndian(span[4..], originY);
        BinaryPrimitives.WriteSingleLittleEndian(span[8..], originZ);
        BinaryPrimitives.WriteSingleLittleEndian(span[12..], tileWidth);
        BinaryPrimitives.WriteSingleLittleEndian(span[16..], tileHeight);
        BinaryPrimitives.WriteInt32LittleEndian(span[20..], maxTiles);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], maxPolys);

        return bytes;
    }

    /// <summary>
    /// Writes a <c>.mmtile</c> in the layout of the given extractor.
    /// </summary>
    /// <param name="flavour">Which extractor's layout to imitate.</param>
    /// <param name="payload">The Detour tile bytes to place after the header.</param>
    /// <param name="magic">Overridden to test rejection of non-navigation files.</param>
    /// <param name="detourVersion">Overridden to test rejection of other expansions' data.</param>
    /// <param name="declaredPayloadSize">Overridden to test truncation handling.</param>
    public static byte[] BuildTile(
        MmapFlavour flavour,
        ReadOnlySpan<byte> payload,
        uint? magic = null,
        uint? detourVersion = null,
        uint? generatorVersion = null,
        uint? declaredPayloadSize = null)
    {
        int headerSize = flavour switch
        {
            MmapFlavour.TrinityCore => MmapFormat.TrinityCoreHeaderSize,
            MmapFlavour.AzerothCore => MmapFormat.AzerothCoreHeaderSize,
            _ => MmapFormat.TrinityCoreHeaderSize,
        };

        byte[] bytes = new byte[headerSize + payload.Length];
        Span<byte> span = bytes;

        BinaryPrimitives.WriteUInt32LittleEndian(span, magic ?? MmapFormat.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(
            span[4..], detourVersion ?? MmapFormat.DetourNavMeshVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(
            span[8..],
            generatorVersion ?? (flavour == MmapFlavour.AzerothCore
                ? MmapFormat.AzerothCoreVersion
                : MmapFormat.TrinityCoreVersion));
        BinaryPrimitives.WriteUInt32LittleEndian(
            span[12..], declaredPayloadSize ?? (uint)payload.Length);
        span[16] = 1; // usesLiquids
        // span[17..20] is padding, left zero as the extractors do.

        // AzerothCore follows the common header with a 36-byte record of the Recast settings
        // the tile was built with. Its contents do not matter to a reader; its size does.
        if (flavour == MmapFlavour.AzerothCore)
        {
            BinaryPrimitives.WriteSingleLittleEndian(span[20..], 60f); // walkableSlopeAngle
            span[24] = 2;  // walkableRadius
            span[25] = 6;  // walkableHeight
            span[26] = 1;  // walkableClimb
        }

        payload.CopyTo(span[headerSize..]);
        return bytes;
    }

    /// <summary>Filler standing in for real Detour tile bytes.</summary>
    public static byte[] FakePayload(int length = 64, byte seed = 0xAB)
    {
        byte[] payload = new byte[length];
        for (int i = 0; i < length; i++)
        {
            payload[i] = (byte)(seed + i);
        }

        return payload;
    }
}
