using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Balance Druid.
/// </summary>
/// <remarks>
/// Ranged casting with both dots kept rolling, and Starfall held for packs. Eclipse is the
/// tree's real mechanic — alternating between arcane and nature damage as the proc swings — and
/// the rotation follows the proc rather than trying to predict its cycle, which a bot cannot do
/// reliably from an aura alone.
/// </remarks>
public sealed class BalanceDruid : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Balance Druid";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Druid;

    /// <inheritdoc />
    public override float PullRange => 30f;

    /// <inheritdoc />
    public override double RestPowerPercent => 45d;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Mark of the Wild", c => !c.HasAura(c.Me, "Mark of the Wild"), onSelf: true)
        .Cast("Moonkin Form", c => !c.HasAura(c.Me, "Moonkin Form"), onSelf: true)
        .Cast("Thorns", c => !c.HasAura(c.Me, "Thorns"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Moonfire")
        .Cast("Wrath");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .Cast("Barkskin", c => c.Me.HealthPercent < 40d, onSelf: true)

        .Cast("Moonfire", c => c.Target is not null && !c.HasAura(c.Target, "Moonfire"))
        .Cast("Insect Swarm", c => c.Target is not null && !c.HasAura(c.Target, "Insect Swarm"))
        .Cast("Starfall", c => c.EnemiesInMelee >= 3)
        .Cast("Starfire",
            c => c.HasAura(c.Me, "Eclipse (Lunar)"),
            description: "follows the proc rather than predicting the cycle")
        .Cast("Wrath", c => c.HasAura(c.Me, "Eclipse (Solar)"))
        .Cast("Wrath", description: "filler when neither side of the proc is up");
}
