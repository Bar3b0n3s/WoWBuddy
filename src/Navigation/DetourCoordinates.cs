using DotRecast.Core.Numerics;
using WoWBuddy.Common.Geometry;

namespace WoWBuddy.Navigation;

/// <summary>
/// Converts between WoW world coordinates and the navmesh's coordinate space.
/// </summary>
/// <remarks>
/// <para>
/// The two do not agree on which axis is which. In the game, X is north, Y is west and Z is
/// up. Recast and Detour assume Y is up, as most graphics code does, so the extractor swaps
/// the axes when it builds the mesh and every consumer has to swap them back:
/// </para>
/// <code>
/// navmesh = (world.Y, world.Z, world.X)
/// world   = (navmesh.Z, navmesh.X, navmesh.Y)
/// </code>
/// <para>
/// This is isolated in one place with its own tests because it is silent when wrong. A
/// swapped path is a perfectly well-formed path to somewhere else, and the resulting bug
/// looks like a pathfinding problem rather than a two-line conversion problem.
/// </para>
/// <para>
/// Source: the same convention TrinityCore's own path generator uses when it hands positions
/// to Detour and reads them back.
/// </para>
/// </remarks>
public static class DetourCoordinates
{
    /// <summary>Converts a world position into navmesh space.</summary>
    public static RcVec3f ToNavMesh(Vector3 world) => new(world.Y, world.Z, world.X);

    /// <summary>Converts a navmesh position back into world space.</summary>
    public static Vector3 ToWorld(RcVec3f navMesh) => new(navMesh.Z, navMesh.X, navMesh.Y);

    /// <summary>
    /// The box, in navmesh space, searched around a point when looking for the polygon under it.
    /// </summary>
    /// <remarks>
    /// Generous vertically and tight horizontally, because the common failure is a character
    /// standing slightly above or below the mesh — on a step, a slope, or mid-jump — rather
    /// than off to one side. Widening the horizontal extent instead would start matching
    /// polygons across walls.
    /// </remarks>
    public static RcVec3f DefaultSearchExtents { get; } = new(6f, 12f, 6f);
}
