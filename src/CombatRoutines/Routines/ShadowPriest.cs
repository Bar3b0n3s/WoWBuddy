using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Shadow Priest.
/// </summary>
/// <remarks>
/// One of the best grinding specialisations in the game, for a reason that matters more to a
/// bot than to a player: Vampiric Embrace turns damage into health, so it rarely has to stop.
/// The rotation is dots kept rolling and Mind Flay in the gaps, which is also unusually
/// forgiving of a bot's timing.
/// </remarks>
public sealed class ShadowPriest : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Shadow Priest";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Priest;

    /// <inheritdoc />
    public override float PullRange => 30f;

    /// <summary>It heals itself by fighting, so it can afford a lower floor than most casters.</summary>
    public override double RestHealthPercent => 45d;

    /// <inheritdoc />
    public override double RestPowerPercent => 30d;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Power Word: Fortitude", c => !c.HasAura(c.Me, "Power Word: Fortitude"), onSelf: true)
        .Cast("Inner Fire", c => !c.HasAura(c.Me, "Inner Fire"), onSelf: true)
        .Cast("Shadowform", c => !c.HasAura(c.Me, "Shadowform"), onSelf: true)
        .Cast("Vampiric Embrace", c => !c.HasAura(c.Me, "Vampiric Embrace"), onSelf: true,
            description: "the reason this specialisation grinds without stopping");

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Mind Blast")
        .Cast("Shadow Word: Pain");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .Cast("Power Word: Shield",
            c => c.Me.HealthPercent < 60d && !c.HasAura(c.Me, "Weakened Soul"),
            onSelf: true)
        .Cast("Flash Heal", c => c.Me.HealthPercent < 30d, onSelf: true)

        .Cast("Vampiric Touch",
            c => c.Target is not null && !c.HasAura(c.Target, "Vampiric Touch"))
        .Cast("Shadow Word: Pain",
            c => c.Target is not null && !c.HasAura(c.Target, "Shadow Word: Pain"))
        .Cast("Devouring Plague",
            c => c.Target is not null && !c.HasAura(c.Target, "Devouring Plague"))
        .Cast("Mind Blast")
        .Cast("Shadow Word: Death", c => c.Target is { HealthPercent: < 25d }, description: "finisher")
        .Cast("Mind Flay", c => c.Target is not null, description: "filler between dot refreshes");

    /// <inheritdoc />
    protected override Rotation RestRotation { get; } = new Rotation()
        .Cast("Renew", c => c.Me.HealthPercent < 85d && !c.HasAura(c.Me, "Renew"), onSelf: true);
}
