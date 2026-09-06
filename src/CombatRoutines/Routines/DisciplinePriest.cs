using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Discipline Priest.
/// </summary>
/// <remarks>
/// Prevents damage rather than repairing it, which suits a bot better than it suits a person:
/// shielding on a schedule is exactly the sort of thing a rotation does well and a distracted
/// player does badly. Weakened Soul is the whole constraint — shielding through it wastes the
/// global cooldown and the mana for nothing.
/// </remarks>
public sealed class DisciplinePriest : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Discipline Priest";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Priest;

    /// <inheritdoc />
    public override float PullRange => 30f;

    /// <inheritdoc />
    public override double RestHealthPercent => 50d;

    /// <inheritdoc />
    public override double RestPowerPercent => 55d;

    /// <inheritdoc />
    public override double ReadyPowerPercent => 80d;

    /// <inheritdoc />
    /// <remarks>A healer has plenty to do with nothing targeted, starting with healing.</remarks>
    protected override bool NeedsTargetToFight => false;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Power Word: Fortitude", c => !c.HasAura(c.Me, "Power Word: Fortitude"), onSelf: true)
        .Cast("Inner Fire", c => !c.HasAura(c.Me, "Inner Fire"), onSelf: true)
        .Cast("Divine Spirit", c => !c.HasAura(c.Me, "Divine Spirit"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Shadow Word: Pain")
        .Cast("Smite");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .CastOn("Pain Suppression",
            c => GroupHealing.MostHurt(c, 20d),
            description: "kept for the moment it saves someone")
        .CastOn("Power Word: Shield",
            c => GroupHealing.MostHurt(c, 85d) is { } hurt && !c.HasAura(hurt, "Weakened Soul")
                ? hurt
                : null,
            description: "the specialisation: absorb before the damage lands")
        .CastOn("Penance", c => GroupHealing.MostHurt(c, 70d))
        .CastOn("Flash Heal", c => GroupHealing.MostHurt(c, 45d))
        .Cast("Prayer of Healing", c => GroupHealing.CountBelow(c, 65d) >= 3)
        .CastOn("Renew",
            c => GroupHealing.MostHurt(c, 80d) is { } hurt && !c.HasAura(hurt, "Renew") ? hurt : null)

        .Cast("Shadow Word: Pain",
            c => c.Target is not null && !c.HasAura(c.Target, "Shadow Word: Pain"))
        .Cast("Penance", c => c.Target is not null)
        .Cast("Smite", c => c.Target is not null, description: "filler");

    /// <inheritdoc />
    protected override Rotation RestRotation { get; } = new Rotation()
        .Cast("Renew", c => c.Me.HealthPercent < 90d && !c.HasAura(c.Me, "Renew"), onSelf: true)
        .Cast("Flash Heal", c => c.Me.HealthPercent < 60d, onSelf: true);
}
