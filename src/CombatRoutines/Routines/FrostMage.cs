using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Frost Mage.
/// </summary>
/// <remarks>
/// The caster case, and the one that shows why pull range matters: a mage opens from thirty
/// yards and wants the target to walk to it, which is a completely different shape of fight
/// from a warrior's. Frost is the grinding specialisation because its slows mean fewer hits
/// taken and therefore less time spent drinking.
/// </remarks>
public sealed class FrostMage : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Frost Mage";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Mage;

    /// <summary>Frostbolt's range, and the whole point of the specialisation.</summary>
    public override float PullRange => 30f;

    /// <summary>Cloth, so it stops to recover earlier than a plate wearer would.</summary>
    public override double RestHealthPercent => 70d;

    /// <inheritdoc />
    public override double RestPowerPercent => 50d;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Frost Armor", c => !c.HasAura(c.Me, "Frost Armor"), onSelf: true)
        .Cast("Arcane Intellect", c => !c.HasAura(c.Me, "Arcane Intellect"), onSelf: true)
        .Cast("Molten Armor", c => false, onSelf: true, description: "disabled; Frost Armor is preferred while grinding");

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Frostbolt", description: "opener; the slow means fewer hits taken on the way in");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .Cast("Ice Block",
            c => c.Me.HealthPercent < 15d,
            onSelf: true,
            description: "last resort; the bot base should be running away by now")
        .Cast("Frost Nova",
            c => c.EnemiesInMelee > 0 && c.Me.HealthPercent < 60d,
            description: "buys room when something reaches melee")
        .Cast("Ice Lance",
            c => c.Target is not null && c.HasAura(c.Target, "Frost Nova"),
            description: "hits much harder into a freeze")
        .Cast("Fire Blast",
            c => c.HasAura(c.Me, "Fingers of Frost"),
            description: "instant, so it does not waste the proc")
        .Cast("Frostbolt", description: "filler");

    /// <inheritdoc />
    protected override Rotation RestRotation { get; } = new Rotation()
        .Cast("Evocation",
            c => c.Me.PowerPercent < 20d && !c.IsCasting,
            onSelf: true,
            description: "far faster than drinking when it is available");
}
