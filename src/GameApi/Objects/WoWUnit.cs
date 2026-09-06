using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;
using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.GameApi.Objects;

/// <summary>
/// A creature, NPC or pet: anything with the unit descriptor block.
/// </summary>
/// <remarks>
/// Players are units too and derive from this. Everything here reads from the descriptor
/// array, whose indices are protocol-defined, apart from position and facing, which come
/// from the layout resolved at attach.
/// </remarks>
public class WoWUnit : WoWObject
{
    internal WoWUnit(GameObjectRef reference, Offsets335a.PositionLayout positionLayout)
        : base(reference, positionLayout)
    {
    }

    /// <inheritdoc />
    public override Vector3 Position =>
        Memory.TryReadVector3(Address + (nint)PositionLayout.PositionBlock, out Vector3 position)
            ? position
            : Vector3.Zero;

    /// <summary>Facing in radians, counter-clockwise from the +X axis.</summary>
    public float Facing => Memory.ReadOrDefault<float>(Address + (nint)PositionLayout.Facing);

    /// <summary>Current health.</summary>
    public uint Health => Descriptors.ReadUInt32(UpdateFields335a.Unit.Health);

    /// <summary>Maximum health.</summary>
    public uint MaxHealth => Descriptors.ReadUInt32(UpdateFields335a.Unit.MaxHealth);

    /// <summary>Health as a percentage, 0 to 100. Zero when max health is unknown.</summary>
    public double HealthPercent
    {
        get
        {
            uint max = MaxHealth;
            return max == 0 ? 0d : Health * 100d / max;
        }
    }

    /// <summary>Unit level.</summary>
    public int Level => Descriptors.ReadInt32(UpdateFields335a.Unit.Level);

    /// <summary>Race, from the packed bytes field.</summary>
    public WoWRace Race => (WoWRace)Descriptors.ReadByte(UpdateFields335a.Unit.Bytes0, 0);

    /// <summary>Class, from the packed bytes field.</summary>
    public WoWClass Class => (WoWClass)Descriptors.ReadByte(UpdateFields335a.Unit.Bytes0, 1);

    /// <summary>The unit's active power type, which selects which power field to read.</summary>
    public PowerType PowerType => (PowerType)Descriptors.ReadByte(UpdateFields335a.Unit.Bytes0, 3);

    /// <summary>Current value of the unit's active power bar.</summary>
    public uint Power => ReadPower(UpdateFields335a.Unit.Power1);

    /// <summary>Maximum value of the unit's active power bar.</summary>
    public uint MaxPower => ReadPower(UpdateFields335a.Unit.MaxPower1);

    /// <summary>Active power as a percentage, 0 to 100.</summary>
    public double PowerPercent
    {
        get
        {
            uint max = MaxPower;
            return max == 0 ? 0d : Power * 100d / max;
        }
    }

    /// <summary>Faction template id, which decides who may attack whom.</summary>
    public uint FactionTemplate => Descriptors.ReadUInt32(UpdateFields335a.Unit.FactionTemplate);

    /// <summary>Unit flags.</summary>
    public UnitFlags Flags => (UnitFlags)Descriptors.ReadUInt32(UpdateFields335a.Unit.Flags);

    /// <summary>Transient state flags: lootable, tapped, and similar.</summary>
    public UnitDynamicFlags DynamicFlags =>
        (UnitDynamicFlags)Descriptors.ReadUInt32(UpdateFields335a.Unit.DynamicFlags);

    /// <summary>Service flags: vendor, repair, trainer, flight master.</summary>
    public NpcFlags NpcFlags => (NpcFlags)Descriptors.ReadUInt32(UpdateFields335a.Unit.NpcFlags);

    /// <summary>GUID of the unit's current target.</summary>
    public WoWGuid TargetGuid => Descriptors.ReadGuid(UpdateFields335a.Unit.Target);

    /// <summary>GUID of whatever summoned this unit, for pets and totems.</summary>
    public WoWGuid SummonedByGuid => Descriptors.ReadGuid(UpdateFields335a.Unit.SummonedBy);

    /// <summary>Display id of the unit's mount. Zero when not mounted.</summary>
    public uint MountDisplayId => Descriptors.ReadUInt32(UpdateFields335a.Unit.MountDisplayId);

    /// <summary>Standing, sitting, or one of the other postures.</summary>
    public StandState StandState => (StandState)Descriptors.ReadByte(UpdateFields335a.Unit.Bytes1, 0);

    /// <summary>Bounding radius, part of the melee range calculation.</summary>
    public float BoundingRadius => Descriptors.ReadSingle(UpdateFields335a.Unit.BoundingRadius);

    /// <summary>Combat reach, part of the melee range calculation.</summary>
    public float CombatReach => Descriptors.ReadSingle(UpdateFields335a.Unit.CombatReach);

    /// <summary>True when the unit's health has reached zero.</summary>
    public bool IsDead => Health == 0;

    /// <summary>True when the unit is alive.</summary>
    public bool IsAlive => Health > 0;

    /// <summary>True when the unit is in combat.</summary>
    public bool IsInCombat => Flags.HasFlag(UnitFlags.InCombat);

    /// <summary>True when the unit is mounted.</summary>
    public bool IsMounted => MountDisplayId != 0 || Flags.HasFlag(UnitFlags.Mount);

    /// <summary>True when the unit is swimming, which changes how it must be pathed.</summary>
    public bool IsSwimming => Flags.HasFlag(UnitFlags.Swimming);

    /// <summary>True when the unit is sitting, as it must be to eat or drink.</summary>
    public bool IsSitting => StandState != StandState.Stand;

    /// <summary>True when the unit cannot be selected by a player.</summary>
    public bool IsNotSelectable => Flags.HasFlag(UnitFlags.NotSelectable);

    /// <summary>True when the unit has loot on it right now.</summary>
    public bool IsLootable => DynamicFlags.HasFlag(UnitDynamicFlags.Lootable);

    /// <summary>True when the corpse can be skinned.</summary>
    public bool IsSkinnable => Flags.HasFlag(UnitFlags.Skinnable);

    /// <summary>True when this unit is a vendor.</summary>
    public bool IsVendor => NpcFlags.HasFlag(NpcFlags.Vendor);

    /// <summary>True when this unit can repair.</summary>
    public bool CanRepair => NpcFlags.HasFlag(NpcFlags.Repair);

    /// <summary>True when this unit is a flight master.</summary>
    public bool IsFlightMaster => NpcFlags.HasFlag(NpcFlags.FlightMaster);

    /// <summary>True when this unit trains anything.</summary>
    public bool IsTrainer =>
        (NpcFlags & (NpcFlags.Trainer | NpcFlags.ClassTrainer | NpcFlags.ProfessionTrainer)) != 0;

    /// <summary>
    /// Distance at which this unit can be reached in melee by <paramref name="other"/>.
    /// </summary>
    /// <remarks>
    /// The client's own melee range is the sum of both units' combat reaches, with a floor
    /// that keeps small mobs reachable. Getting this right matters because a bot that stops
    /// a quarter of a yard too far away simply never lands a hit.
    /// </remarks>
    public float MeleeRangeTo(WoWUnit other)
    {
        ArgumentNullException.ThrowIfNull(other);
        const float minimumMeleeRange = 5f;
        return MathF.Max(CombatReach + other.CombatReach, minimumMeleeRange);
    }

    /// <summary>Flat distance from this unit to <paramref name="other"/>.</summary>
    public float DistanceTo(WoWUnit other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Position.Distance(other.Position);
    }

    private uint ReadPower(uint firstIndex)
    {
        int index = (int)PowerType;
        // Seven power fields exist; anything else means the packed byte was misread, and
        // returning zero is safer than reading past the end of the block.
        return index is >= 0 and <= 6 ? Descriptors.ReadUInt32(firstIndex + (uint)index) : 0u;
    }

    public override string ToString() => $"{Type} {Guid} level {Level} {Health}/{MaxHealth}hp";
}
