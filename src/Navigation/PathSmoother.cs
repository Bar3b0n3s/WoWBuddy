using WoWBuddy.Common.Geometry;

namespace WoWBuddy.Navigation;

/// <summary>
/// Tidies a raw navmesh path into something worth walking.
/// </summary>
/// <remarks>
/// <para>
/// Detour's straightened path is geometrically correct and often unpleasant: it hugs polygon
/// edges, and it emits waypoints wherever the corridor bends even slightly. Walked literally,
/// that produces a character that clips walls and stutters through gentle curves — which is
/// both slower and conspicuous.
/// </para>
/// <para>
/// The operations here are pure geometry with no dependency on the client, so they are fully
/// testable, and they are kept separate from path <em>finding</em> so that the same tidying
/// applies to recorded profile paths as to generated ones.
/// </para>
/// </remarks>
public static class PathSmoother
{
    /// <summary>
    /// Default distance below which a waypoint is considered redundant.
    /// </summary>
    /// <remarks>
    /// Roughly a character's own width. Waypoints closer together than this cannot be
    /// distinguished by the movement system anyway, since it stops a little short of each.
    /// </remarks>
    public const float DefaultMinimumSpacing = 2f;

    /// <summary>
    /// Default angle, in degrees, below which a bend is treated as straight.
    /// </summary>
    /// <remarks>
    /// Small enough to preserve real corners, large enough to collapse the sawtooth that
    /// polygon-edge following produces along a straight corridor.
    /// </remarks>
    public const float DefaultCollinearToleranceDegrees = 8f;

    /// <summary>
    /// Removes waypoints that sit closer than <paramref name="minimumSpacing"/> to the one
    /// before them, always keeping the first and last.
    /// </summary>
    public static IReadOnlyList<Vector3> RemoveCloseWaypoints(
        IReadOnlyList<Vector3> points,
        float minimumSpacing = DefaultMinimumSpacing)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count <= 2)
        {
            return points;
        }

        var result = new List<Vector3>(points.Count) { points[0] };

        for (int i = 1; i < points.Count - 1; i++)
        {
            if (points[i].Distance(result[^1]) >= minimumSpacing)
            {
                result.Add(points[i]);
            }
        }

        // The destination is never dropped, however close it is to its predecessor: arriving
        // is the entire point of the path.
        result.Add(points[^1]);
        return result;
    }

    /// <summary>
    /// Removes waypoints that lie on a straight line between their neighbours.
    /// </summary>
    /// <remarks>
    /// Measured in the horizontal plane. Height changes along a slope are continuous and
    /// would defeat a three-dimensional collinearity test, while the thing being removed —
    /// zig-zag along a straight corridor — is purely horizontal.
    /// </remarks>
    public static IReadOnlyList<Vector3> RemoveCollinearWaypoints(
        IReadOnlyList<Vector3> points,
        float toleranceDegrees = DefaultCollinearToleranceDegrees)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count <= 2)
        {
            return points;
        }

        float tolerance = toleranceDegrees * MathF.PI / 180f;
        var result = new List<Vector3>(points.Count) { points[0] };

        for (int i = 1; i < points.Count - 1; i++)
        {
            float incoming = result[^1].FacingTo(points[i]);
            float outgoing = points[i].FacingTo(points[i + 1]);

            if (Vector3.AngleDelta(incoming, outgoing) > tolerance)
            {
                result.Add(points[i]);
            }
        }

        result.Add(points[^1]);
        return result;
    }

    /// <summary>Applies both simplifications, collinear first.</summary>
    /// <remarks>
    /// Order matters. Collapsing straight runs first turns a long sawtooth into two points;
    /// removing close waypoints first would eat the detail that identifies the run as
    /// straight in the first place.
    /// </remarks>
    public static IReadOnlyList<Vector3> Simplify(
        IReadOnlyList<Vector3> points,
        float minimumSpacing = DefaultMinimumSpacing,
        float toleranceDegrees = DefaultCollinearToleranceDegrees)
    {
        IReadOnlyList<Vector3> straightened = RemoveCollinearWaypoints(points, toleranceDegrees);
        return RemoveCloseWaypoints(straightened, minimumSpacing);
    }

    /// <summary>
    /// Inserts intermediate waypoints so no leg is longer than <paramref name="maximumSpacing"/>.
    /// </summary>
    /// <remarks>
    /// The opposite of simplification, and needed for a different reason: click-to-move walks
    /// toward a single point, so a very long leg means a long stretch with no opportunity to
    /// notice a problem. Splitting it gives the bot regular checkpoints at which to compare
    /// where it is with where it should be.
    /// </remarks>
    public static IReadOnlyList<Vector3> Subdivide(IReadOnlyList<Vector3> points, float maximumSpacing)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSpacing);

        if (points.Count < 2)
        {
            return points;
        }

        var result = new List<Vector3> { points[0] };

        for (int i = 1; i < points.Count; i++)
        {
            Vector3 from = points[i - 1];
            Vector3 to = points[i];
            float distance = from.Distance(to);

            int segments = (int)MathF.Ceiling(distance / maximumSpacing);
            for (int s = 1; s < segments; s++)
            {
                float t = (float)s / segments;
                result.Add(new Vector3(
                    from.X + ((to.X - from.X) * t),
                    from.Y + ((to.Y - from.Y) * t),
                    from.Z + ((to.Z - from.Z) * t)));
            }

            result.Add(to);
        }

        return result;
    }
}
