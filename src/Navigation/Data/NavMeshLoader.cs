using DotRecast.Detour;
using WoWBuddy.Common.Logging;

namespace WoWBuddy.Navigation.Data;

/// <summary>What loading one map's navigation data produced.</summary>
/// <param name="Mesh">The assembled navmesh, or null on failure.</param>
/// <param name="Flavour">Which extractor produced the data.</param>
/// <param name="TilesLoaded">How many tiles were added.</param>
/// <param name="TilesRejected">How many tile files were present but unusable.</param>
/// <param name="Problems">One line per problem, for showing the user.</param>
public sealed record NavMeshLoadResult(
    DtNavMesh? Mesh,
    MmapFlavour Flavour,
    int TilesLoaded,
    int TilesRejected,
    IReadOnlyList<string> Problems)
{
    /// <summary>True when a usable mesh was produced.</summary>
    public bool Success => Mesh is not null && TilesLoaded > 0;
}

/// <summary>
/// Builds a Detour navmesh from the navigation data a user extracted from their own client.
/// </summary>
/// <remarks>
/// <para>
/// No navigation data ships with this project and none ever can: it is derived from
/// Blizzard's game files. The user runs an extractor from TrinityCore or AzerothCore against
/// their own installation, and this reads the result.
/// </para>
/// <para>
/// <b>Mixed data is refused.</b> If a folder contains tiles from both extractors, the most
/// likely explanation is that a partial extraction was overwritten by another, and the parts
/// were not built from the same settings. Loading the subset that happens to match would give
/// a mesh with silent holes in it, and a bot that walks into a hole looks exactly like a bot
/// with a bug somewhere else entirely.
/// </para>
/// </remarks>
public sealed class NavMeshLoader
{
    /// <summary>
    /// Tiles that may be rejected before the whole map is treated as unusable.
    /// </summary>
    /// <remarks>
    /// A handful of bad tiles is survivable: the bot simply cannot path through those squares.
    /// A large number means the extraction is broken, and pretending otherwise wastes the
    /// user's time chasing a pathing bug that is really a data problem.
    /// </remarks>
    public const int MaxToleratedBadTiles = 8;

    private readonly string _mmapsDirectory;

    /// <param name="mmapsDirectory">The <c>mmaps</c> folder holding the extracted data.</param>
    public NavMeshLoader(string mmapsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mmapsDirectory);
        _mmapsDirectory = mmapsDirectory;
    }

    /// <summary>The folder this loader reads from.</summary>
    public string Directory => _mmapsDirectory;

    /// <summary>Loads every tile of one map into a navmesh.</summary>
    public NavMeshLoadResult Load(int mapId)
    {
        var problems = new List<string>();

        string paramsPath = Path.Combine(_mmapsDirectory, MmapFormat.MapFileName(mapId));
        if (!File.Exists(paramsPath))
        {
            problems.Add(
                $"{MmapFormat.MapFileName(mapId)} is missing from {_mmapsDirectory}. " +
                "That map was not extracted, or the folder is not an mmaps folder.");
            return new NavMeshLoadResult(null, MmapFlavour.Unknown, 0, 0, problems);
        }

        byte[] paramBytes;
        try
        {
            paramBytes = File.ReadAllBytes(paramsPath);
        }
        catch (IOException ex)
        {
            problems.Add($"Could not read {paramsPath}: {ex.Message}");
            return new NavMeshLoadResult(null, MmapFlavour.Unknown, 0, 0, problems);
        }

        if (!MmapReader.TryReadNavMeshParams(paramBytes, out DtNavMeshParams navParams, out MmapReadError paramError))
        {
            problems.Add(MmapReader.Describe(paramError, MmapFormat.MapFileName(mapId)));
            return new NavMeshLoadResult(null, MmapFlavour.Unknown, 0, 0, problems);
        }

        var mesh = new DtNavMesh();
        if (mesh.Init(in navParams, MmapFormat.VertsPerPolygon).Failed())
        {
            problems.Add(
                $"Detour rejected the parameters in {MmapFormat.MapFileName(mapId)} " +
                $"(max tiles {navParams.maxTiles}, max polys {navParams.maxPolys}).");
            return new NavMeshLoadResult(null, MmapFlavour.Unknown, 0, 0, problems);
        }

        return LoadTiles(mapId, mesh, problems);
    }

    private NavMeshLoadResult LoadTiles(int mapId, DtNavMesh mesh, List<string> problems)
    {
        string[] tilePaths;
        try
        {
            tilePaths = System.IO.Directory
                .EnumerateFiles(_mmapsDirectory, "*" + MmapFormat.TileExtension)
                .Where(path => MmapFormat.IsTileOfMap(Path.GetFileName(path), mapId))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }
        catch (IOException ex)
        {
            problems.Add($"Could not list tiles in {_mmapsDirectory}: {ex.Message}");
            return new NavMeshLoadResult(null, MmapFlavour.Unknown, 0, 0, problems);
        }

        if (tilePaths.Length == 0)
        {
            problems.Add(
                $"No {MmapFormat.TileExtension} files for map {mapId} in {_mmapsDirectory}. " +
                "The parameters file exists but no tiles were extracted.");
            return new NavMeshLoadResult(null, MmapFlavour.Unknown, 0, 0, problems);
        }

        MmapFlavour flavour = MmapFlavour.Unknown;
        int loaded = 0;
        int rejected = 0;

        foreach (string path in tilePaths)
        {
            string name = Path.GetFileName(path);

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (IOException ex)
            {
                problems.Add($"Could not read {name}: {ex.Message}");
                rejected++;
                continue;
            }

            // The header is read first and on its own, because which extractor wrote a tile
            // has to be knowable even when the tile itself is corrupt. A folder holding both
            // flavours is a different problem from a folder holding bad tiles, and the user
            // needs to be told which one they have.
            if (!MmapReader.TryReadTileHeader(bytes, out MmapTileHeader header, out MmapReadError headerError))
            {
                problems.Add(MmapReader.Describe(headerError, name));
                rejected++;
                continue;
            }

            // The first readable header fixes the flavour for the whole map.
            if (flavour == MmapFlavour.Unknown)
            {
                flavour = header.Flavour;
            }
            else if (header.Flavour != flavour)
            {
                problems.Add(
                    $"{name} was written by the {header.Flavour} extractor but earlier tiles for this map " +
                    $"came from {flavour}. Navigation data from the two cannot be mixed: delete the " +
                    "mmaps folder and extract it once, with one extractor.");
                return new NavMeshLoadResult(null, MmapFlavour.Unknown, loaded, rejected + 1, problems);
            }

            if (!MmapReader.TryReadTilePayload(bytes, header, out DtMeshData? tile, out MmapReadError error))
            {
                problems.Add(MmapReader.Describe(error, name));
                rejected++;
                continue;
            }

            if (mesh.AddTile(tile!, 0, 0, out _).Failed())
            {
                problems.Add($"Detour refused to add {name} to the mesh.");
                rejected++;
                continue;
            }

            loaded++;
        }

        if (rejected > MaxToleratedBadTiles)
        {
            problems.Add(
                $"{rejected} of {tilePaths.Length} tiles for map {mapId} could not be read. " +
                "That is too many to be incidental; re-run the extractor.");
            return new NavMeshLoadResult(null, flavour, loaded, rejected, problems);
        }

        Log.For<NavMeshLoader>().Information(
            "Loaded map {MapId}: {Loaded} tile(s) from {Flavour} data, {Rejected} rejected",
            mapId, loaded, flavour, rejected);

        return new NavMeshLoadResult(mesh, flavour, loaded, rejected, problems);
    }
}
