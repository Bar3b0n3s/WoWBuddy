using DotRecast.Core.Numerics;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Navigation;
using Xunit;

namespace WoWBuddy.Navigation.Tests;

/// <summary>
/// Covers the axis swap between game and navmesh space.
/// </summary>
/// <remarks>
/// Two lines of code that are silent when wrong: a swapped path is a well-formed path to the
/// wrong place, and the resulting bug looks like a pathfinding problem. Worth its own tests.
/// </remarks>
public sealed class DetourCoordinatesTests
{
    [Fact]
    public void WorldToNavMeshSwapsAxesTheWayTheExtractorDoes()
    {
        // Game space is X north, Y west, Z up. Recast assumes Y is up, so the extractor
        // stores (worldY, worldZ, worldX) and every reader has to match.
        var world = new Vector3(1f, 2f, 3f);

        RcVec3f nav = DetourCoordinates.ToNavMesh(world);

        Assert.Equal(2f, nav.X);
        Assert.Equal(3f, nav.Y);
        Assert.Equal(1f, nav.Z);
    }

    [Fact]
    public void NavMeshToWorldReversesTheSwap()
    {
        var nav = new RcVec3f(2f, 3f, 1f);

        Vector3 world = DetourCoordinates.ToWorld(nav);

        Assert.Equal(1f, world.X);
        Assert.Equal(2f, world.Y);
        Assert.Equal(3f, world.Z);
    }

    [Theory]
    [InlineData(-8913.23f, 554.63f, 93.79f)]   // Stormwind
    [InlineData(1629.36f, -4373.39f, 31.26f)]  // Orgrimmar
    [InlineData(5804.15f, 624.77f, 647.76f)]   // Dalaran
    [InlineData(0f, 0f, 0f)]
    public void ConversionRoundTripsExactly(float x, float y, float z)
    {
        var original = new Vector3(x, y, z);

        Vector3 result = DetourCoordinates.ToWorld(DetourCoordinates.ToNavMesh(original));

        Assert.Equal(original, result);
    }

    [Fact]
    public void SearchExtentsAreTallerThanTheyAreWide()
    {
        // The usual reason a position misses the mesh is being above or below it, on a step
        // or mid-jump. Widening horizontally instead would match polygons through walls.
        RcVec3f extents = DetourCoordinates.DefaultSearchExtents;

        Assert.True(extents.Y > extents.X);
        Assert.True(extents.Y > extents.Z);
    }
}
