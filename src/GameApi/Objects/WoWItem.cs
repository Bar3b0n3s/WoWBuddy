using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;

namespace WoWBuddy.GameApi.Objects;

/// <summary>An item, in a bag or equipped.</summary>
public class WoWItem : WoWObject
{
    internal WoWItem(GameObjectRef reference, Offsets335a.PositionLayout positionLayout)
        : base(reference, positionLayout)
    {
    }

    /// <summary>GUID of whoever owns the item.</summary>
    public WoWGuid OwnerGuid => Descriptors.ReadGuid(UpdateFields335a.Item.Owner);

    /// <summary>How many are in the stack.</summary>
    public uint StackCount => Descriptors.ReadUInt32(UpdateFields335a.Item.StackCount);

    /// <summary>Current durability. Zero for items that have none.</summary>
    public uint Durability => Descriptors.ReadUInt32(UpdateFields335a.Item.Durability);

    public override string ToString() => $"Item entry {Entry} x{StackCount}";
}

/// <summary>A bag. Containers are items with extra fields.</summary>
public sealed class WoWContainer : WoWItem
{
    internal WoWContainer(GameObjectRef reference, Offsets335a.PositionLayout positionLayout)
        : base(reference, positionLayout)
    {
    }

    /// <summary>Number of slots the bag has.</summary>
    public uint SlotCount => Descriptors.ReadUInt32(UpdateFields335a.Container.NumSlots);

    /// <summary>GUID of the item in <paramref name="slot"/>, or zero when empty.</summary>
    public WoWGuid GetSlot(int slot)
    {
        if (slot < 0 || slot >= SlotCount)
        {
            return WoWGuid.Zero;
        }

        // Each slot is a two-field GUID.
        return Descriptors.ReadGuid(UpdateFields335a.Container.Slot1 + (uint)(slot * 2));
    }

    /// <summary>How many slots are empty.</summary>
    public int FreeSlots
    {
        get
        {
            int count = 0;
            uint slots = SlotCount;
            for (int i = 0; i < slots; i++)
            {
                if (GetSlot(i).IsZero)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public override string ToString() => $"Container entry {Entry}, {SlotCount} slots";
}
