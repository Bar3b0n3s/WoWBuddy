using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Arcane Mage.
/// </summary>
/// <remarks>
/// The highest damage of the three mage trees and the worst at grinding, because Arcane Blast
/// costs more each time it is stacked and the tree has no way to make things stop hitting it.
/// The rotation therefore watches its own mana far more carefully than the others do, and drops
/// the stack rather than casting itself dry.
/// </remarks>
public sealed class ArcaneMage : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Arcane Mage";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Mage;

    /// <inheritdoc />
    public override float PullRange => 30f;

    /// <summary>It empties its mana bar faster than any other caster, and stops sooner for it.</summary>
    public override double RestPowerPercent => 55d;

    /// <inheritdoc />
    public override double ReadyPowerPercent => 85d;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Arcane Intellect", c => !c.HasAura(c.Me, "Arcane Intellect"), onSelf: true)
        .Cast("Molten Armor", c => !c.HasAura(c.Me, "Molten Armor"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Arcane Blast");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .Cast("Ice Block", c => c.Me.HealthPercent < 20d, onSelf: true)
        .Cast("Frost Nova", c => c.EnemiesInMelee >= 1 && c.Me.HealthPercent < 60d, onSelf: true)
        .Cast("Evocation", c => c.Me.PowerPercent < 20d, onSelf: true,
            description: "cheaper than sitting down to drink, when it is off cooldown")

        .Cast("Arcane Missiles",
            c => c.HasAura(c.Me, "Missile Barrage"),
            description: "free through the proc, and it drops the Arcane Blast stack")
        .Cast("Arcane Barrage",
            c => c.Me.PowerPercent < 40d,
            description: "the cheap way out of a stack that has become unaffordable")
        .Cast("Arcane Blast", description: "the rotation, until the mana says otherwise");
}
