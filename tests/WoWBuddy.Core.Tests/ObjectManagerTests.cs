using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;
using WoWBuddy.Core.Tests.Fakes;
using Xunit;

namespace WoWBuddy.Core.Tests;

/// <summary>
/// Exercises the object-manager walk against a simulated client laid out with the real
/// offset table.
/// </summary>
public sealed class ObjectManagerTests
{
    [Fact]
    public void ResolveManager_ReturnsZero_WhenNotInWorld()
    {
        // No object manager written: the state at the login and character-select screens.
        var client = new SimulatedClientBuilder().Build();
        var objectManager = new ObjectManager(client);

        Assert.Equal(0, objectManager.ResolveManager());
        Assert.False(objectManager.IsInWorld);
    }

    [Fact]
    public void ResolveManager_FollowsTheFullPointerChain()
    {
        var builder = new SimulatedClientBuilder().WithObjectManager();
        SimulatedClient client = builder.Build();
        var objectManager = new ObjectManager(client);

        Assert.Equal(builder.ManagerAddress, objectManager.ResolveManager());
        Assert.True(objectManager.IsInWorld);
    }

    [Fact]
    public void EnumerateObjects_ReturnsEveryLinkedObject()
    {
        SimulatedClient client = new SimulatedClientBuilder()
            .WithObjectManager()
            .WithLocalPlayer()
            .WithCreature(0x01, entry: 299, level: 5, new Vector3(-8900f, 550f, 93f))
            .WithCreature(0x02, entry: 300, level: 6, new Vector3(-8890f, 545f, 93f))
            .Build();

        List<GameObjectRef> objects = new ObjectManager(client).EnumerateObjects().ToList();

        Assert.Equal(3, objects.Count);
        Assert.Single(objects, o => o.Type == WoWObjectType.Player);
        Assert.Equal(2, objects.Count(o => o.Type == WoWObjectType.Unit));
    }

    [Fact]
    public void EnumerateObjects_StopsAtAnOddTerminator()
    {
        // The client marks the end of the list with a non-aligned pointer as well as with
        // null. Treating that as an object address would read garbage.
        SimulatedClient client = new SimulatedClientBuilder()
            .WithObjectManager()
            .WithLocalPlayer()
            .Build(terminator: 0x11111111);

        List<GameObjectRef> objects = new ObjectManager(client).EnumerateObjects().ToList();

        Assert.Single(objects);
    }

    [Fact]
    public void EnumerateObjects_BreaksOutOfACycleInsteadOfHanging()
    {
        SimulatedClient client = new SimulatedClientBuilder()
            .WithObjectManager()
            .WithLocalPlayer()
            .WithCreature(0x01, entry: 299, level: 5, new Vector3(-8900f, 550f, 93f))
            .BuildWithCycle();

        List<GameObjectRef> objects = new ObjectManager(client).EnumerateObjects().ToList();

        // Both real objects are still reported; the walk stops when it returns to the head.
        Assert.Equal(2, objects.Count);
    }

    [Fact]
    public void EnumerateObjects_SkipsObjectsWithAnImpossibleTypeTag()
    {
        var builder = new SimulatedClientBuilder()
            .WithObjectManager()
            .WithLocalPlayer()
            .WithCreature(0x01, entry: 299, level: 5, new Vector3(-8900f, 550f, 93f));

        SimulatedClient client = builder.Build();

        // Corrupt the creature's type tag the way a torn read would.
        nint creatureAddress = client.ObjectAddresses[1];
        client.WriteUInt32(creatureAddress + (nint)Offsets335a.Object.Type, 0xDEAD);

        List<GameObjectRef> objects = new ObjectManager(client).EnumerateObjects().ToList();

        // The bad object is dropped, but the walk continues rather than ending the world.
        Assert.Single(objects);
        Assert.Equal(WoWObjectType.Player, objects[0].Type);
    }

    [Fact]
    public void GetLocalPlayerGuid_ReadsTheManagerHeader()
    {
        var builder = new SimulatedClientBuilder().WithObjectManager().WithLocalPlayer(guidLow: 0xABCD);
        SimulatedClient client = builder.Build();

        Assert.Equal(builder.LocalPlayerGuid, new ObjectManager(client).GetLocalPlayerGuid());
    }

    [Fact]
    public void FindLocalPlayer_ResolvesTheHeaderGuidToAPlayerObject()
    {
        SimulatedClient client = new SimulatedClientBuilder()
            .WithObjectManager()
            .WithCreature(0x01, entry: 299, level: 5, new Vector3(-8900f, 550f, 93f))
            .WithLocalPlayer(guidLow: 0xABCD)
            .Build();

        GameObjectRef player = new ObjectManager(client).FindLocalPlayer();

        Assert.True(player.IsValid);
        Assert.Equal(WoWObjectType.Player, player.Type);
    }

    [Fact]
    public void DescriptorGuidMatches_IsTrue_WhenTheDescriptorPointerIsCorrect()
    {
        SimulatedClient client = new SimulatedClientBuilder()
            .WithObjectManager()
            .WithLocalPlayer()
            .Build();

        GameObjectRef player = new ObjectManager(client).FindLocalPlayer();

        Assert.True(player.DescriptorGuidMatches());
    }

    [Fact]
    public void DescriptorGuidMatches_IsFalse_WhenTheDescriptorPointerIsWrong()
    {
        // The whole point of the check: a descriptor pointer aimed somewhere else cannot
        // produce a matching GUID.
        SimulatedClient client = new SimulatedClientBuilder()
            .WithObjectManager()
            .WithLocalPlayer()
            .Build();

        nint playerAddress = client.ObjectAddresses[0];
        nint elsewhere = client.Allocate(0x100);
        client.WritePointer(playerAddress + (nint)Offsets335a.Object.Descriptors, elsewhere);

        GameObjectRef player = new ObjectManager(client).FindLocalPlayer();

        Assert.False(player.DescriptorGuidMatches());
    }

    [Fact]
    public void EnumerateObjects_StopsWhenAnObjectIsFreedMidWalk()
    {
        var builder = new SimulatedClientBuilder()
            .WithObjectManager()
            .WithLocalPlayer()
            .WithCreature(0x01, entry: 299, level: 5, new Vector3(-8900f, 550f, 93f));

        SimulatedClient client = builder.Build();

        // Simulate the client destroying the second object between the head read and the walk.
        client.Unmap(client.ObjectAddresses[1], 0x1000);

        List<GameObjectRef> objects = new ObjectManager(client).EnumerateObjects().ToList();

        Assert.Single(objects);
    }
}
