using WoWBuddy.Common.Geometry;

namespace WoWBuddy.Navigation;

/// <summary>Why a path could not be produced.</summary>
public enum PathFailure
{
    /// <summary>No failure.</summary>
    None = 0,

    /// <summary>No navigation data is loaded for the requested map.</summary>
    NoDataForMap,

    /// <summary>The start position is not on or near the navmesh.</summary>
    StartOffMesh,

    /// <summary>The destination is not on or near the navmesh.</summary>
    EndOffMesh,

    /// <summary>Detour found no route between the two.</summary>
    NoRoute,

    /// <summary>A route exists but does not reach the destination.</summary>
    Partial,
}

/// <summary>A route through the world.</summary>
/// <param name="Points">Waypoints from start to destination, in world coordinates.</param>
/// <param name="Failure">Why the path is unusable, when it is.</param>
/// <param name="IsPartial">
/// True when the path is the best available but stops short of the requested destination.
/// </param>
public sealed record NavigationPath(
    IReadOnlyList<Vector3> Points,
    PathFailure Failure = PathFailure.None,
    bool IsPartial = false)
{
    /// <summary>True when there is something to walk.</summary>
    public bool Success => Failure is PathFailure.None or PathFailure.Partial && Points.Count > 0;

    /// <summary>Total ground distance along the path.</summary>
    public float Length
    {
        get
        {
            float total = 0f;
            for (int i = 1; i < Points.Count; i++)
            {
                total += Points[i - 1].Distance(Points[i]);
            }

            return total;
        }
    }

    /// <summary>An empty path carrying a reason.</summary>
    public static NavigationPath Failed(PathFailure failure) => new([], failure);
}

/// <summary>
/// Finds routes through the world.
/// </summary>
/// <remarks>
/// An interface so that the movement layer can be developed and tested without navigation
/// data present, and so that a different provider — a separate navigation server, say — could
/// be substituted without the rest of the bot noticing.
/// </remarks>
public interface INavigationService
{
    /// <summary>True when data for <paramref name="mapId"/> is loaded and queryable.</summary>
    bool IsMapLoaded(int mapId);

    /// <summary>
    /// Finds a walkable route from <paramref name="start"/> to <paramref name="end"/>.
    /// </summary>
    NavigationPath FindPath(int mapId, Vector3 start, Vector3 end);

    /// <summary>
    /// Snaps a position onto the navmesh, or returns null when it is nowhere near it.
    /// </summary>
    /// <remarks>
    /// Useful for validating a profile's hotspots before a bot spends an hour discovering
    /// that one of them is inside a rock.
    /// </remarks>
    Vector3? FindNearestWalkable(int mapId, Vector3 position);
}
