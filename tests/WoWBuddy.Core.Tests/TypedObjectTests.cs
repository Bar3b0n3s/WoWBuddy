using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;
using WoWBuddy.Core.Tests.Fakes;
using WoWBuddy.GameApi.Enums;
using Xunit;

namespace WoWBuddy.Core.Tests;

/// <summary>
/// Checks that the typed layer decodes descriptor fields the way the protocol defines them.
/// </summary>
public sealed class TypedObjectTests
{
    [Fact]
    public void PackedBytesFieldDecodesRaceClassAndPowerType()
    {
        SimulatedClient client = new SimulatedClientBuilder()
            .WithObjectManager()
            .WithLocalPlayer()
            .Build();

        GameObjectRef player = new ObjectManager(client).FindLocalPlayer();
        DescriptorTable descriptors = player.Descriptors;

        // The builder wrote Human / Rogue / Energy into the packed field.
        Assert.Equal((byte)WoWRace.Human, descriptors.ReadByte(UpdateFields335a.Unit.Bytes0, 0));
        Assert.Equal((byte)WoWClass.Rogue, descriptors.ReadByte(UpdateFields335a.Unit.Bytes0, 1));
        Assert.Equal((byte)PowerType.Energy, descriptors.ReadByte(UpdateFields335a.Unit.Bytes0, 3));
    }

    [Fact]
    public void GuidsSpanTwoDescriptorIndices()
    {
        var target = new WoWGuid(0xF130_0012_3456_789AUL);

        SimulatedClient client = new SimulatedClientBuilder()
            .WithObjectManager()
            .WithObject(WoWObjectType.Player, new WoWGuid(0x1234), new Vector3(1f, 2f, 3f),
                d => d.WriteGuid(UpdateFields335a.Unit.Target, target))
            .Build();

        GameObjectRef player = new ObjectManager(client).EnumerateObjects().Single();

        Assert.Equal(target, player.Descriptors.ReadGuid(UpdateFields335a.Unit.Target));
    }

    [Fact]
    public void GuidExposesTheCreatureEntryPackedIntoIt()
    {
        // The 3.3.5 server builds a GUID as counter | (entry << 24) | (high << 48).
        const uint entry = 299;
        var guid = new WoWGuid(0x42UL | ((ulong)entry << 24) | ((ulong)WoWGuidType.Creature << 48));

        Assert.Equal(entry, guid.Entry);
        Assert.Equal(WoWGuidType.Creature, guid.Type);
        Assert.True(guid.IsCreature);
        Assert.False(guid.IsPlayer);
    }

    [Fact]
    public void PlayerGuidsHaveAZeroHighWord()
    {
        var guid = new WoWGuid(0x0000_0000_0000_1234UL);

        Assert.True(guid.IsPlayer);
        Assert.Equal(WoWGuidType.Player, guid.Type);
        Assert.Equal(0u, guid.Entry);
    }

    [Fact]
    public void DescriptorByteIndexIsBoundsChecked()
    {
        SimulatedClient client = new SimulatedClientBuilder()
            .WithObjectManager()
            .WithLocalPlayer()
            .Build();

        DescriptorTable descriptors = new ObjectManager(client).FindLocalPlayer().Descriptors;

        Assert.Throws<ArgumentOutOfRangeException>(() => descriptors.ReadByte(UpdateFields335a.Unit.Bytes0, 4));
    }

    [Fact]
    public void ByteOffsetIsFourTimesTheFieldIndex()
    {
        Assert.Equal(0x60u, UpdateFields335a.ByteOffset(UpdateFields335a.Unit.Health));
        Assert.Equal(0x80u, UpdateFields335a.ByteOffset(UpdateFields335a.Unit.MaxHealth));
        Assert.Equal(0xD8u, UpdateFields335a.ByteOffset(UpdateFields335a.Unit.Level));
    }

    [Fact]
    public void RebaseIsANoOpAtTheDefaultImageBase()
    {
        nint address = Offsets335a.Rebase(
            Offsets335a.ObjectManager.ClientConnection, (nint)Offsets335a.DefaultImageBase);

        Assert.Equal((nint)Offsets335a.ObjectManager.ClientConnection, address);
    }

    [Fact]
    public void RebaseShiftsAddressesWhenTheModuleMoved()
    {
        // Private-server launchers occasionally rebase the client. Static addresses must
        // follow the module rather than being used raw.
        const nint relocated = 0x00500000;
        nint address = Offsets335a.Rebase(Offsets335a.ObjectManager.ClientConnection, relocated);

        Assert.Equal(
            relocated + (nint)(Offsets335a.ObjectManager.ClientConnection - Offsets335a.DefaultImageBase),
            address);
    }
}
