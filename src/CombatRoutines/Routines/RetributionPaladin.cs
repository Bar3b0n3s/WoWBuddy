using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Retribution Paladin.
/// </summary>
/// <remarks>
/// A melee damage rotation with a heal attached, which makes it one of the better solo grinders:
/// it can top itself up between pulls without sitting down, so it spends more of the night
/// fighting than eating.
/// </remarks>
public sealed class RetributionPaladin : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Retribution Paladin";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Paladin;

    /// <inheritdoc />
    public override float PullRange => 20f;

    /// <inheritdoc />
    public override double RestHealthPercent => 50d;

    /// <inheritdoc />
    public override double RestPowerPercent => 30d;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Blessing of Might", c => !c.HasAura(c.Me, "Blessing of Might"), onSelf: true)
        .Cast("Retribution Aura", c => !c.HasAura(c.Me, "Retribution Aura"), onSelf: true)
        .Cast("Seal of Command", c => !c.HasAura(c.Me, "Seal of Command"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Judgement of Wisdom")
        .Cast("Exorcism");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .Cast("Lay on Hands", c => c.Me.HealthPercent < 15d, onSelf: true)
        .Cast("Flash of Light", c => c.Me.HealthPercent < 40d, onSelf: true,
            description: "the reason this specialisation grinds well")

        .Cast("Hammer of Wrath", c => c.Target is { HealthPercent: < 20d }, description: "finisher")
        .Cast("Crusader Strike")
        .Cast("Divine Storm", c => c.EnemiesInMelee >= 2)
        .Cast("Judgement of Wisdom")
        .Cast("Consecration", c => c.EnemiesInMelee >= 2)
        .Cast("Exorcism");

    /// <inheritdoc />
    protected override Rotation RestRotation { get; } = new Rotation()
        .Cast("Flash of Light", c => c.Me.HealthPercent < 80d, onSelf: true);
}
