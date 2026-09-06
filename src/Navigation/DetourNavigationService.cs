using System.Collections.Concurrent;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;
using WoWBuddy.Navigation.Data;

namespace WoWBuddy.Navigation;

/// <summary>
/// Finds paths in the navigation meshes the user extracted from their own client.
/// </summary>
/// <remarks>
/// <para>
/// Runs in-process rather than as a separate navigation server. The usual reason for a
/// separate server is that Detour is C++ and the bot is C#; using a permissively licensed C#
/// port removes that reason, and with it a second executable to install, a socket protocol to
/// get wrong, and a process that can die independently of the bot.
/// </para>
/// <para>
/// Maps are loaded on demand and kept, because a character crosses a continent boundary
/// rarely and reloading a whole map's tiles takes long enough to notice.
/// </para>
/// </remarks>
public sealed class DetourNavigationService : INavigationService
{
    /// <summary>
    /// Longest path Detour is asked to produce, in polygons.
    /// </summary>
    /// <remarks>
    /// Cross-continent routes are handled by flight paths and portals rather than by one
    /// enormous mesh query, so a path needing more polygons than this is a sign the request
    /// was unreasonable, not that the limit is too low.
    /// </remarks>
    public const int MaxPathPolygons = 1024;

    /// <summary>Maximum waypoints in the straightened path handed back to the caller.</summary>
    public const int MaxWaypoints = 512;

    private readonly NavMeshLoader _loader;
    private readonly ConcurrentDictionary<int, LoadedMap> _maps = new();
    private readonly IDtQueryFilter _filter = new DtQueryDefaultFilter();

    public DetourNavigationService(NavMeshLoader loader)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    /// <summary>Convenience constructor taking the <c>mmaps</c> folder directly.</summary>
    public DetourNavigationService(string mmapsDirectory)
        : this(new NavMeshLoader(mmapsDirectory))
    {
    }

    /// <inheritdoc />
    public bool IsMapLoaded(int mapId) => GetMap(mapId)?.Query is not null;

    /// <summary>The result of loading a map, for diagnostics and the setup tool.</summary>
    public NavMeshLoadResult? LoadReportFor(int mapId) => GetMap(mapId)?.Report;

    /// <summary>Maps currently held in memory.</summary>
    public IReadOnlyCollection<int> LoadedMaps => _maps.Keys.ToList();

    /// <summary>Forgets a map, releasing its tiles.</summary>
    public void Unload(int mapId) => _maps.TryRemove(mapId, out _);

    /// <inheritdoc />
    public NavigationPath FindPath(int mapId, Vector3 start, Vector3 end)
    {
        LoadedMap? map = GetMap(mapId);
        if (map?.Query is not { } query)
        {
            return NavigationPath.Failed(PathFailure.NoDataForMap);
        }

        RcVec3f startNav = DetourCoordinates.ToNavMesh(start);
        RcVec3f endNav = DetourCoordinates.ToNavMesh(end);
        RcVec3f extents = DetourCoordinates.DefaultSearchExtents;

        if (query.FindNearestPoly(startNav, extents, _filter, out long startRef, out RcVec3f startOnMesh, out _).Failed()
            || startRef == 0)
        {
            return NavigationPath.Failed(PathFailure.StartOffMesh);
        }

        if (query.FindNearestPoly(endNav, extents, _filter, out long endRef, out RcVec3f endOnMesh, out _).Failed()
            || endRef == 0)
        {
            return NavigationPath.Failed(PathFailure.EndOffMesh);
        }

        long[] polygons = new long[MaxPathPolygons];
        DtStatus status = query.FindPath(
            startRef, endRef, startOnMesh, endOnMesh, _filter, polygons, out int polygonCount, MaxPathPolygons);

        if (status.Failed() || polygonCount == 0)
        {
            return NavigationPath.Failed(PathFailure.NoRoute);
        }

        // Detour's polygon path is a corridor, not a route. Straightening pulls a line
        // through it, which is what actually gets walked.
        DtStraightPath[] straight = new DtStraightPath[MaxWaypoints];
        if (query.FindStraightPath(
                startOnMesh, endOnMesh, polygons, polygonCount, straight, out int straightCount, MaxWaypoints, 0)
            .Failed() || straightCount == 0)
        {
            return NavigationPath.Failed(PathFailure.NoRoute);
        }

        var points = new List<Vector3>(straightCount);
        for (int i = 0; i < straightCount; i++)
        {
            points.Add(DetourCoordinates.ToWorld(straight[i].pos));
        }

        // The corridor ending on a different polygon than requested means the destination
        // could not be reached; the path still goes as far as it can, which is usually
        // what the caller wants, but they have to be told.
        bool partial = polygons[polygonCount - 1] != endRef;

        return new NavigationPath(points, partial ? PathFailure.Partial : PathFailure.None, partial);
    }

    /// <inheritdoc />
    public Vector3? FindNearestWalkable(int mapId, Vector3 position)
    {
        LoadedMap? map = GetMap(mapId);
        if (map?.Query is not { } query)
        {
            return null;
        }

        RcVec3f target = DetourCoordinates.ToNavMesh(position);
        if (query.FindNearestPoly(target, DetourCoordinates.DefaultSearchExtents, _filter,
                out long polyRef, out RcVec3f onMesh, out _).Failed() || polyRef == 0)
        {
            return null;
        }

        return DetourCoordinates.ToWorld(onMesh);
    }

    private LoadedMap? GetMap(int mapId) =>
        _maps.GetOrAdd(mapId, id =>
        {
            NavMeshLoadResult result = _loader.Load(id);

            if (!result.Success)
            {
                foreach (string problem in result.Problems)
                {
                    Log.For<DetourNavigationService>().Warning("Map {MapId}: {Problem}", id, problem);
                }

                return new LoadedMap(null, result);
            }

            return new LoadedMap(new DtNavMeshQuery(result.Mesh!), result);
        });

    private sealed record LoadedMap(DtNavMeshQuery? Query, NavMeshLoadResult Report);
}

/// <summary>
/// A navigation service that never has data.
/// </summary>
/// <remarks>
/// The bot has to run without navigation data — to inspect the world, to be developed
/// against, and to fail comprehensibly when a user has not extracted anything yet. This makes
/// that state a normal object rather than a null check scattered through the movement code.
/// </remarks>
public sealed class NullNavigationService : INavigationService
{
    /// <inheritdoc />
    public bool IsMapLoaded(int mapId) => false;

    /// <inheritdoc />
    public NavigationPath FindPath(int mapId, Vector3 start, Vector3 end) =>
        NavigationPath.Failed(PathFailure.NoDataForMap);

    /// <inheritdoc />
    public Vector3? FindNearestWalkable(int mapId, Vector3 position) => null;
}
