using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Fire Mage.
/// </summary>
/// <remarks>
/// Living Bomb and Fireball, with Pyroblast taken only when Hot Streak makes it instant — a hard
/// cast Pyroblast in a grinding rotation is six seconds of standing still, which is how a mage
/// dies. Scorch is the mobile filler.
/// </remarks>
public sealed class FireMage : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Fire Mage";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Mage;

    /// <inheritdoc />
    public override float PullRange => 30f;

    /// <inheritdoc />
    public override double RestPowerPercent => 40d;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Arcane Intellect", c => !c.HasAura(c.Me, "Arcane Intellect"), onSelf: true)
        .Cast("Molten Armor", c => !c.HasAura(c.Me, "Molten Armor"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Living Bomb")
        .Cast("Fireball");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .Cast("Ice Block", c => c.Me.HealthPercent < 20d, onSelf: true)
        .Cast("Frost Nova", c => c.EnemiesInMelee >= 1 && c.Me.HealthPercent < 60d, onSelf: true)

        .Cast("Pyroblast",
            c => c.HasAura(c.Me, "Hot Streak"),
            description: "only when the proc makes it instant")
        .Cast("Living Bomb",
            c => c.Target is not null && !c.HasAura(c.Target, "Living Bomb"))
        .Cast("Flamestrike", c => c.EnemiesInMelee >= 3)
        .Cast("Fireball", description: "the main cast")
        .Cast("Scorch", description: "the one that can be cast on the move");
}
