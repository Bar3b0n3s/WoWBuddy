using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Combat Rogue.
/// </summary>
/// <remarks>
/// The straightforward rogue, and the one that grinds best: no positional requirements, no
/// poison bookkeeping worth the name, and a rotation that is Sinister Strike until there are
/// enough combo points to spend. Blade Flurry and Adrenaline Rush are held for when there is
/// more than one thing to kill, because using them on a single target wastes most of them.
/// </remarks>
public sealed class CombatRogue : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Combat Rogue";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Rogue;

    /// <inheritdoc />
    public override bool UsesDrinkablePower => false;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Slice and Dice", c => c.ComboPoints >= 1 && !c.HasAura(c.Me, "Slice and Dice"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Sinister Strike");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .Cast("Evasion", c => c.Me.HealthPercent < 35d, onSelf: true)
        .Cast("Vanish", c => c.Me.HealthPercent < 15d, onSelf: true)

        .Cast("Slice and Dice",
            c => c.ComboPoints >= 2 && !c.HasAura(c.Me, "Slice and Dice"),
            onSelf: true)
        .Cast("Blade Flurry", c => c.EnemiesInMelee >= 2, onSelf: true)
        .Cast("Adrenaline Rush", c => c.EnemiesInMelee >= 2, onSelf: true)
        .Cast("Eviscerate", c => c.ComboPoints >= 4, description: "the spender")
        .Cast("Fan of Knives", c => c.EnemiesInMelee >= 3)
        .Cast("Sinister Strike", c => c.ComboPoints < 5, description: "and again, and again");
}
