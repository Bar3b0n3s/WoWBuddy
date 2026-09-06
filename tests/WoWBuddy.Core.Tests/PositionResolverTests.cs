using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Attach;
using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;
using WoWBuddy.Core.Tests.Fakes;
using Xunit;

namespace WoWBuddy.Core.Tests;

/// <summary>
/// Covers the mechanism that settles the conflicting position offsets empirically.
/// </summary>
/// <remarks>
/// A client is built that stores positions at one candidate layout, and the resolver is
/// required to identify that layout rather than the other. Both candidates are tested so the
/// result cannot be an artefact of the first one happening to be correct.
/// </remarks>
public sealed class PositionResolverTests
{
    private static readonly Vector3 Origin = new(-8913.23f, 554.63f, 93.79f);

    private static SimulatedClient ClientUsing(Offsets335a.PositionLayout layout, int nearbyUnits = 8)
    {
        var builder = new SimulatedClientBuilder { PlayerPosition = Origin }
            .UsingPositionLayout(layout)
            .WithObjectManager()
            .WithLocalPlayer();

        for (int i = 0; i < nearbyUnits; i++)
        {
            builder.WithCreature(
                (ulong)(i + 1),
                entry: 299,
                level: 10,
                new Vector3(Origin.X + (i * 4f), Origin.Y - (i * 3f), Origin.Z));
        }

        return builder.Build();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Resolve_IdentifiesWhicheverLayoutTheClientActuallyUses(int candidateIndex)
    {
        Offsets335a.PositionLayout expected = Offsets335a.UnitPosition.Candidates[candidateIndex];
        SimulatedClient client = ClientUsing(expected);

        PositionResolution resolution = PositionResolver.Resolve(client, new ObjectManager(client));

        Assert.True(resolution.Success, resolution.Message);
        Assert.True(resolution.Confident, resolution.Message);
        Assert.Equal(expected, resolution.Layout);
    }

    [Fact]
    public void Resolve_ReadsTheCorrectCoordinatesThroughTheResolvedLayout()
    {
        Offsets335a.PositionLayout expected = Offsets335a.UnitPosition.Candidates[1];
        SimulatedClient client = ClientUsing(expected);
        var objectManager = new ObjectManager(client);

        PositionResolution resolution = PositionResolver.Resolve(client, objectManager);
        GameObjectRef player = objectManager.FindLocalPlayer();

        Assert.True(client.TryReadVector3(
            player.Address + (nint)resolution.Layout.PositionBlock, out Vector3 position));
        Assert.Equal(Origin.X, position.X, 2);
        Assert.Equal(Origin.Y, position.Y, 2);
        Assert.Equal(Origin.Z, position.Z, 2);
    }

    [Fact]
    public void Resolve_Fails_WhenNoLayoutProducesAPlausiblePosition()
    {
        // A client that stores nothing at either candidate: what a genuinely wrong offset
        // table looks like. Refusing is the required behaviour.
        SimulatedClient client = new SimulatedClientBuilder()
            .WithObjectManager()
            .WithObject(WoWObjectType.Player, new WoWGuid(0x1234), position: null, d =>
            {
                d.WriteUInt32(UpdateFields335a.Unit.Level, 80);
                d.WriteUInt32(UpdateFields335a.Unit.Health, 100);
                d.WriteUInt32(UpdateFields335a.Unit.MaxHealth, 100);
            })
            .Build();

        var builderManager = new ObjectManager(client);
        PositionResolution resolution = PositionResolver.Resolve(client, builderManager);

        Assert.False(resolution.Success);
    }

    [Fact]
    public void Resolve_SucceedsButIsNotConfident_WhenNothingIsNearby()
    {
        // Standing alone in the wilderness. The player's own position is still evidence, but
        // there is nothing to corroborate it with, and the report must say so.
        SimulatedClient client = ClientUsing(Offsets335a.UnitPosition.Candidates[0], nearbyUnits: 0);

        PositionResolution resolution = PositionResolver.Resolve(client, new ObjectManager(client));

        Assert.True(resolution.Success, resolution.Message);
        Assert.False(resolution.Confident);
        Assert.Contains("too few", resolution.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_Fails_WhenTheLocalPlayerIsMissing()
    {
        SimulatedClient client = new SimulatedClientBuilder()
            .WithObjectManager()
            .WithCreature(0x01, entry: 299, level: 5, Origin)
            .Build();

        PositionResolution resolution = PositionResolver.Resolve(client, new ObjectManager(client));

        Assert.False(resolution.Success);
    }
}
