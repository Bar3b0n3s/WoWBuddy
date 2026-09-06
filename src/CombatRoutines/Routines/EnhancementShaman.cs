using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Enhancement Shaman.
/// </summary>
/// <remarks>
/// Melee with instant casts bolted on. Maelstrom Weapon is the interesting mechanic: it makes a
/// Lightning Bolt instant, so the rotation checks for the stack rather than casting the bolt on
/// its own — a hard cast in melee is a cast that gets pushed back and mostly wasted.
/// </remarks>
public sealed class EnhancementShaman : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Enhancement Shaman";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Shaman;

    /// <inheritdoc />
    public override float PullRange => 25f;

    /// <inheritdoc />
    public override double RestPowerPercent => 25d;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Lightning Shield", c => !c.HasAura(c.Me, "Lightning Shield"), onSelf: true)
        .Cast("Windfury Weapon", c => !c.HasAura(c.Me, "Windfury Weapon"), onSelf: true)
        .Cast("Strength of Earth Totem", c => !c.HasAura(c.Me, "Strength of Earth Totem"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Lightning Bolt", description: "opens at range and closes the gap on the way in")
        .Cast("Earth Shock");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .Cast("Healing Wave",
            c => c.Me.HealthPercent < 35d && c.HasAura(c.Me, "Maelstrom Weapon"),
            onSelf: true,
            description: "instant through the proc; a hard cast in melee mostly gets pushed back")
        .Cast("Healing Wave", c => c.Me.HealthPercent < 20d, onSelf: true, description: "worth eating the pushback")

        .Cast("Lightning Bolt",
            c => c.HasAura(c.Me, "Maelstrom Weapon"),
            description: "only while the proc makes it instant")
        .Cast("Stormstrike")
        .Cast("Lava Lash")
        .Cast("Flame Shock", c => c.Target is not null && !c.HasAura(c.Target, "Flame Shock"))
        .Cast("Earth Shock")
        .Cast("Magma Totem", c => c.EnemiesInMelee >= 3, onSelf: true);

    /// <inheritdoc />
    protected override Rotation RestRotation { get; } = new Rotation()
        .Cast("Healing Wave", c => c.Me.HealthPercent < 80d, onSelf: true);
}
