using WoWBuddy.Common.Geometry;
using Xunit;

namespace WoWBuddy.Common.Tests;

public sealed class GeometryTests
{
    [Fact]
    public void Distance2DIgnoresHeight()
    {
        var a = new Vector3(0f, 0f, 0f);
        var b = new Vector3(3f, 4f, 1000f);

        Assert.Equal(5f, a.Distance2D(b), 4);
        Assert.True(a.Distance(b) > 1000f);
    }

    [Fact]
    public void FacingToIsMeasuredCounterClockwiseFromPositiveX()
    {
        var origin = new Vector3(0f, 0f, 0f);

        Assert.Equal(0f, origin.FacingTo(new Vector3(10f, 0f, 0f)), 4);
        Assert.Equal(MathF.PI / 2f, origin.FacingTo(new Vector3(0f, 10f, 0f)), 4);
        Assert.Equal(MathF.PI, origin.FacingTo(new Vector3(-10f, 0f, 0f)), 4);
    }

    [Fact]
    public void FacingToIsAlwaysNormalisedIntoZeroToTwoPi()
    {
        var origin = new Vector3(0f, 0f, 0f);

        // Pointing down -Y would give a negative atan2 result if it were not normalised.
        float facing = origin.FacingTo(new Vector3(0f, -10f, 0f));

        Assert.InRange(facing, 0f, MathF.PI * 2f);
        Assert.Equal(3f * MathF.PI / 2f, facing, 4);
    }

    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(0.1f, 6.2f, 0.183185f)]     // wraps across zero rather than going the long way
    [InlineData(0f, MathF.PI, MathF.PI)]
    public void AngleDeltaTakesTheShortWayRound(float a, float b, float expected)
    {
        Assert.Equal(expected, Vector3.AngleDelta(a, b), 3);
    }

    [Fact]
    public void AngleDeltaNeverExceedsPi()
    {
        for (float a = 0f; a < 7f; a += 0.37f)
        {
            for (float b = 0f; b < 7f; b += 0.41f)
            {
                Assert.InRange(Vector3.AngleDelta(a, b), 0f, MathF.PI + 0.0001f);
            }
        }
    }

    [Fact]
    public void WorldBoundsRejectsCoordinatesOffTheMapGrid()
    {
        // A map is 64 tiles of 533.33 yards, so nothing legal exceeds 17066.66 on X or Y.
        Assert.True(WorldBounds.IsPlausible(new Vector3(-8913f, 554f, 93f)));
        Assert.False(WorldBounds.IsPlausible(new Vector3(WorldBounds.MaxCoordinate + 1f, 0f, 0f)));
        Assert.False(WorldBounds.IsPlausible(new Vector3(0f, WorldBounds.MaxCoordinate + 1f, 0f)));
    }

    [Fact]
    public void WorldBoundsRejectsTheValuesABadReadProduces()
    {
        Assert.False(WorldBounds.IsPlausible(Vector3.Zero));
        Assert.False(WorldBounds.IsPlausible(new Vector3(float.NaN, 0f, 0f)));
        Assert.False(WorldBounds.IsPlausible(new Vector3(float.PositiveInfinity, 0f, 0f)));
        Assert.False(WorldBounds.IsPlausible(new Vector3(0f, 0f, float.NaN)));
    }

    [Fact]
    public void MaxCoordinateMatchesTheMapGridDefinition()
    {
        Assert.Equal(17066.666f, WorldBounds.MaxCoordinate, 2);
    }
}
