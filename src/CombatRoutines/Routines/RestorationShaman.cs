using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Restoration Shaman.
/// </summary>
/// <remarks>
/// Riptide first and then heal into it — the whole tree is built around that ordering, and it is
/// the one thing a healing rotation can get wrong that costs real throughput. Chain Heal is held
/// for when several people are hurt, since on one target it is a slower, dearer single heal.
/// </remarks>
public sealed class RestorationShaman : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Restoration Shaman";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Shaman;

    /// <inheritdoc />
    public override float PullRange => 25f;

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
        .Cast("Water Shield", c => !c.HasAura(c.Me, "Water Shield"), onSelf: true)
        .Cast("Earthliving Weapon", c => !c.HasAura(c.Me, "Earthliving Weapon"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Flame Shock")
        .Cast("Lightning Bolt");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .CastOn("Riptide",
            c => GroupHealing.MostHurt(c, 85d) is { } hurt && !c.HasAura(hurt, "Riptide") ? hurt : null,
            description: "instant, and every heal after it lands harder")
        .Cast("Chain Heal",
            c => GroupHealing.CountBelow(c, 70d) >= 3,
            description: "worth it only when it bounces to somebody")
        .CastOn("Healing Wave", c => GroupHealing.MostHurt(c, 45d), description: "the emergency")
        .CastOn("Lesser Healing Wave", c => GroupHealing.MostHurt(c, 75d), description: "the top-up")

        .Cast("Flame Shock", c => c.Target is not null && !c.HasAura(c.Target, "Flame Shock"))
        .Cast("Lightning Bolt", c => c.Target is not null, description: "damage when nobody needs healing");

    /// <inheritdoc />
    protected override Rotation RestRotation { get; } = new Rotation()
        .Cast("Lesser Healing Wave", c => c.Me.HealthPercent < 80d, onSelf: true);
}
