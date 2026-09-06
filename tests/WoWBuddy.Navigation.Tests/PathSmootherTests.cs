using WoWBuddy.Common.Geometry;
using WoWBuddy.Navigation;
using Xunit;

namespace WoWBuddy.Navigation.Tests;

public sealed class PathSmootherTests
{
    [Fact]
    public void CollinearWaypointsAlongAStraightRunAreRemoved()
    {
        // What Detour produces along a straight corridor: a waypoint per polygon edge.
        List<Vector3> path =
        [
            new(0f, 0f, 0f), new(10f, 0f, 0f), new(20f, 0f, 0f), new(30f, 0f, 0f), new(40f, 0f, 0f),
        ];

        IReadOnlyList<Vector3> result = PathSmoother.RemoveCollinearWaypoints(path);

        Assert.Equal(2, result.Count);
        Assert.Equal(path[0], result[0]);
        Assert.Equal(path[^1], result[^1]);
    }

    [Fact]
    public void RealCornersAreKept()
    {
        List<Vector3> path = [new(0f, 0f, 0f), new(10f, 0f, 0f), new(10f, 10f, 0f)];

        IReadOnlyList<Vector3> result = PathSmoother.RemoveCollinearWaypoints(path);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void HeightChangesAlongAStraightSlopeDoNotCountAsCorners()
    {
        // Collinearity is judged horizontally on purpose: a ramp is a straight run even
        // though every waypoint is at a different height.
        List<Vector3> path =
        [
            new(0f, 0f, 0f), new(10f, 0f, 5f), new(20f, 0f, 10f), new(30f, 0f, 15f),
        ];

        IReadOnlyList<Vector3> result = PathSmoother.RemoveCollinearWaypoints(path);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void WaypointsTooCloseTogetherAreDropped()
    {
        List<Vector3> path =
        [
            new(0f, 0f, 0f), new(0.4f, 0f, 0f), new(0.8f, 0f, 0f), new(50f, 0f, 0f),
        ];

        IReadOnlyList<Vector3> result = PathSmoother.RemoveCloseWaypoints(path, minimumSpacing: 2f);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void TheDestinationIsNeverDroppedHoweverCloseItIs()
    {
        // Arriving is the point of the path; a destination that sits just past the previous
        // waypoint must still be walked to.
        List<Vector3> path = [new(0f, 0f, 0f), new(50f, 0f, 0f), new(50.1f, 0f, 0f)];

        IReadOnlyList<Vector3> result = PathSmoother.RemoveCloseWaypoints(path, minimumSpacing: 5f);

        Assert.Equal(path[^1], result[^1]);
    }

    [Fact]
    public void ShortPathsArePassedThroughUntouched()
    {
        List<Vector3> path = [new(0f, 0f, 0f), new(10f, 0f, 0f)];

        Assert.Equal(2, PathSmoother.Simplify(path).Count);
        Assert.Single(PathSmoother.Simplify([new Vector3(1f, 2f, 3f)]));
        Assert.Empty(PathSmoother.Simplify([]));
    }

    [Fact]
    public void SimplifyKeepsTheEndpointsWhateverElseItRemoves()
    {
        List<Vector3> path =
        [
            new(-100f, 50f, 10f), new(-90f, 50f, 10f), new(-80f, 50f, 10f),
            new(-80f, 60f, 10f), new(-80f, 70f, 10f),
        ];

        IReadOnlyList<Vector3> result = PathSmoother.Simplify(path);

        Assert.Equal(path[0], result[0]);
        Assert.Equal(path[^1], result[^1]);
        Assert.True(result.Count < path.Count);
    }

    [Fact]
    public void SubdivideCapsHowFarTheBotWalksWithoutACheckpoint()
    {
        List<Vector3> path = [new(0f, 0f, 0f), new(100f, 0f, 0f)];

        IReadOnlyList<Vector3> result = PathSmoother.Subdivide(path, maximumSpacing: 25f);

        Assert.Equal(5, result.Count);
        for (int i = 1; i < result.Count; i++)
        {
            Assert.True(result[i - 1].Distance(result[i]) <= 25.01f);
        }
    }

    [Fact]
    public void SubdivideLeavesShortLegsAlone()
    {
        List<Vector3> path = [new(0f, 0f, 0f), new(5f, 0f, 0f), new(10f, 0f, 0f)];

        Assert.Equal(3, PathSmoother.Subdivide(path, maximumSpacing: 25f).Count);
    }

    [Fact]
    public void SubdivideInterpolatesHeightAlongTheLeg()
    {
        List<Vector3> path = [new(0f, 0f, 0f), new(0f, 0f, 100f)];

        IReadOnlyList<Vector3> result = PathSmoother.Subdivide(path, maximumSpacing: 50f);

        Assert.Equal(3, result.Count);
        Assert.Equal(50f, result[1].Z, 2);
    }

    [Fact]
    public void SubdivideRejectsANonPositiveSpacing() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PathSmoother.Subdivide([new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f)], 0f));
}
