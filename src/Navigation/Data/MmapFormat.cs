namespace WoWBuddy.Navigation.Data;

/// <summary>
/// Which server project's extractor produced a set of navigation data.
/// </summary>
/// <remarks>
/// The two are not interchangeable, which is the single most common reason navigation data
/// fails to load. See <see cref="MmapFormat"/>.
/// </remarks>
public enum MmapFlavour
{
    /// <summary>Not recognised as either flavour.</summary>
    Unknown = 0,

    /// <summary>TrinityCore's <c>mmaps_generator</c>, generator version 15.</summary>
    TrinityCore = 1,

    /// <summary>AzerothCore's <c>mmaps_generator</c>, generator version 20.</summary>
    AzerothCore = 2,
}

/// <summary>
/// The on-disk layout of the navigation data the user extracts from their own client.
/// </summary>
/// <remarks>
/// <para>
/// Navigation meshes are not shipped with this project and never can be: they are derived
/// from Blizzard's game files. Users generate them with the extractor from a server project,
/// and <b>the two common extractors do not produce the same files</b>.
/// </para>
/// <para>
/// Both write a <c>mmaps/</c> folder containing one <c>.mmap</c> per map holding Detour's
/// navmesh parameters, and one <c>.mmtile</c> per grid tile holding a serialised Detour tile
/// behind a header. The header is where they diverge:
/// </para>
/// <list type="table">
/// <listheader><term>Flavour</term><description>Generator version and header size</description></listheader>
/// <item>
/// <term>TrinityCore</term>
/// <description>
/// Version 15, a 20-byte header: magic, Detour version, generator version, payload size,
/// a liquids flag and three padding bytes.
/// </description>
/// </item>
/// <item>
/// <term>AzerothCore</term>
/// <description>
/// Version 20, a 56-byte header: the same first 20 bytes followed by a 36-byte record of the
/// Recast settings the tile was built with.
/// </description>
/// </item>
/// </list>
/// <para>
/// A reader that assumes one and is given the other does not fail cleanly: it takes 36 bytes
/// of Recast configuration as the start of the Detour tile and produces nonsense. So the
/// flavour is detected from the generator version in the header and the payload is read from
/// the right offset, and a folder containing both is rejected rather than half-loaded.
/// </para>
/// <para>
/// Sources: TrinityCore's and AzerothCore's <c>MapDefines.h</c> and <c>MMapManager.cpp</c>.
/// These are GPL projects and no code was taken from them; the file layout is a fact about a
/// data format, and is recorded here so the bot can read data the user generated.
/// </para>
/// </remarks>
public static class MmapFormat
{
    /// <summary>Magic at the start of every tile header: the ASCII bytes <c>MMAP</c>.</summary>
    public const uint Magic = 0x4D4D4150;

    /// <summary>Generator version written by TrinityCore's extractor for 3.3.5.</summary>
    public const uint TrinityCoreVersion = 15;

    /// <summary>Generator version written by AzerothCore's extractor.</summary>
    public const uint AzerothCoreVersion = 20;

    /// <summary>Size of the TrinityCore tile header.</summary>
    public const int TrinityCoreHeaderSize = 20;

    /// <summary>
    /// Size of the AzerothCore tile header: the common 20 bytes plus a 36-byte Recast
    /// configuration record.
    /// </summary>
    public const int AzerothCoreHeaderSize = 56;

    /// <summary>Bytes of header common to both flavours, and enough to identify which it is.</summary>
    public const int CommonHeaderSize = 20;

    /// <summary>
    /// Size of Detour's <c>dtNavMeshParams</c>, the entire contents of a <c>.mmap</c> file:
    /// three origin floats, two tile dimensions, and two integer capacities.
    /// </summary>
    public const int NavMeshParamsSize = 28;

    /// <summary>
    /// Vertices per navmesh polygon. Both extractors build with Detour's default of six, and
    /// the tile reader has to be told the same number or it will misread every polygon.
    /// </summary>
    public const int VertsPerPolygon = 6;

    /// <summary>Detour's own tile format version, which both flavours share.</summary>
    public const int DetourNavMeshVersion = 7;

    /// <summary>File extension of the per-map parameters file.</summary>
    public const string MapExtension = ".mmap";

    /// <summary>File extension of a per-tile navmesh file.</summary>
    public const string TileExtension = ".mmtile";

    /// <summary>The folder inside the data directory that holds navigation data.</summary>
    public const string FolderName = "mmaps";

    /// <summary>The flavour that writes a given generator version, if it is one we know.</summary>
    public static MmapFlavour FlavourFor(uint generatorVersion) => generatorVersion switch
    {
        TrinityCoreVersion => MmapFlavour.TrinityCore,
        AzerothCoreVersion => MmapFlavour.AzerothCore,
        _ => MmapFlavour.Unknown,
    };

    /// <summary>Size of the tile header a given flavour writes.</summary>
    public static int HeaderSizeFor(MmapFlavour flavour) => flavour switch
    {
        MmapFlavour.TrinityCore => TrinityCoreHeaderSize,
        MmapFlavour.AzerothCore => AzerothCoreHeaderSize,
        _ => throw new ArgumentOutOfRangeException(nameof(flavour), flavour, "Unknown navigation data flavour."),
    };

    /// <summary>The name of the parameters file for a map, for example <c>000.mmap</c>.</summary>
    public static string MapFileName(int mapId) => $"{mapId:D3}{MapExtension}";

    /// <summary>
    /// The name of a tile file, for example <c>00043032.mmtile</c>.
    /// </summary>
    /// <remarks>
    /// The extractor writes the grid coordinates in the order (Y, X). Tile files are normally
    /// found by enumeration rather than by name, and each tile's own Detour header carries its
    /// coordinates, so nothing depends on getting this order right; it exists so that a
    /// specific tile can be named in a diagnostic.
    /// </remarks>
    public static string TileFileName(int mapId, int tileY, int tileX) =>
        $"{mapId:D3}{tileY:D2}{tileX:D2}{TileExtension}";

    /// <summary>True when a file name looks like a tile belonging to <paramref name="mapId"/>.</summary>
    public static bool IsTileOfMap(string fileName, int mapId)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        // "MMMYYXX.mmtile": three digits of map id then four of grid position.
        if (!fileName.EndsWith(TileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string stem = Path.GetFileNameWithoutExtension(fileName);
        return stem.Length == 7
            && int.TryParse(stem.AsSpan(0, 3), out int id)
            && id == mapId;
    }
}
