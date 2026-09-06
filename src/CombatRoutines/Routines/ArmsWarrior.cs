using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Arms Warrior.
/// </summary>
/// <remarks>
/// Slower and more deliberate than Fury: one big weapon, and a rotation built around keeping
/// Rend up and spending on Mortal Strike. Overpower only exists when the target has dodged, so
/// it sits behind a proc rather than in the filler.
/// </remarks>
public sealed class ArmsWarrior : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Arms Warrior";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Warrior;

    /// <summary>Charge reaches this far, and is how the fight starts.</summary>
    public override float PullRange => 20f;

    /// <inheritdoc />
    public override bool UsesDrinkablePower => false;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Battle Shout", c => !c.HasAura(c.Me, "Battle Shout"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Charge", description: "closes the gap and generates rage")
        .Cast("Heroic Throw", description: "when charge is on cooldown");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .Cast("Victory Rush", c => c.Me.HealthPercent < 90d, description: "free heal after a kill")
        .Cast("Rend",
            c => c.Target is not null && !c.HasAura(c.Target, "Rend"),
            description: "kept up rather than recast over itself")
        .Cast("Mortal Strike", description: "the rotation is built around this")
        .Cast("Overpower",
            c => c.HasAura(c.Me, "Taste for Blood"),
            description: "only usable off a dodge or the proc that fakes one")
        .Cast("Execute", c => c.Target is { HealthPercent: < 20d }, description: "finisher")
        .Cast("Slam", c => c.Me.Power > 50, description: "rage dump")
        .Cast("Heroic Strike", c => c.Me.Power > 60, description: "so nothing is wasted");
}
