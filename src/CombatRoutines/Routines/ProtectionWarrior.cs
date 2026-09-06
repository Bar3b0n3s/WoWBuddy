using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Protection Warrior.
/// </summary>
/// <remarks>
/// <para>
/// A tanking routine, which is a different job from a damage one and fails differently. Damage
/// output barely matters; holding everything does. So the rotation leads with the threat
/// abilities and the area taunt, and keeps a cooldown in reserve for when health drops rather
/// than using it on cooldown.
/// </para>
/// <para>
/// Soloing as Protection is slow but very hard to kill, which for an unattended bot is often
/// the better trade.
/// </para>
/// </remarks>
public sealed class ProtectionWarrior : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Protection Warrior";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Warrior;

    /// <inheritdoc />
    public override float PullRange => 20f;

    /// <inheritdoc />
    public override bool UsesDrinkablePower => false;

    /// <summary>It can afford a lower floor than a damage specialisation, and does.</summary>
    public override double RestHealthPercent => 45d;

    /// <inheritdoc />
    public override double ReadyHealthPercent => 75d;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Commanding Shout", c => !c.HasAura(c.Me, "Commanding Shout"), onSelf: true)
        .Cast("Defensive Stance", c => !c.HasAura(c.Me, "Defensive Stance"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Charge")
        .Cast("Heroic Throw");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        // Survival before threat: a tank that dies holds nothing.
        .Cast("Shield Block", c => c.Me.HealthPercent < 70d, onSelf: true)
        .Cast("Shield Wall", c => c.Me.HealthPercent < 30d, onSelf: true,
            description: "kept for emergencies rather than used on cooldown")
        .Cast("Last Stand", c => c.Me.HealthPercent < 20d, onSelf: true)

        .Cast("Thunder Clap", c => c.EnemiesInMelee >= 2, description: "area threat first")
        .Cast("Shockwave", c => c.EnemiesInMelee >= 2)
        .Cast("Shield Slam", description: "the biggest single-target threat")
        .Cast("Revenge")
        .Cast("Devastate", description: "filler that also stacks the armour debuff")
        .Cast("Heroic Strike", c => c.Me.Power > 60);
}
