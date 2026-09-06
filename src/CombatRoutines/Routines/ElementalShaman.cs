using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Elemental Shaman.
/// </summary>
/// <remarks>
/// Ranged casting with a heal in reserve, which grinds comfortably. Totems are dropped out of
/// combat rather than mid-fight: they cost a global cooldown each and a bot that re-drops them
/// on every pull spends most of the fight placing furniture.
/// </remarks>
public sealed class ElementalShaman : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Elemental Shaman";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Shaman;

    /// <inheritdoc />
    public override float PullRange => 30f;

    /// <inheritdoc />
    public override double RestPowerPercent => 45d;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Lightning Shield", c => !c.HasAura(c.Me, "Lightning Shield"), onSelf: true)
        .Cast("Totem of Wrath", c => !c.HasAura(c.Me, "Totem of Wrath"), onSelf: true)
        .Cast("Flametongue Weapon", c => !c.HasAura(c.Me, "Flametongue Weapon"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Flame Shock")
        .Cast("Lightning Bolt");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .Cast("Healing Wave", c => c.Me.HealthPercent < 35d, onSelf: true)

        .Cast("Flame Shock",
            c => c.Target is not null && !c.HasAura(c.Target, "Flame Shock"),
            description: "kept up; Lava Burst is worth much more while it is")
        .Cast("Lava Burst", description: "the hardest hit, and it always crits through Flame Shock")
        .Cast("Chain Lightning", c => c.EnemiesInMelee >= 2)
        .Cast("Earth Shock", c => c.HasAura(c.Me, "Maelstrom Weapon"))
        .Cast("Lightning Bolt", description: "filler");

    /// <inheritdoc />
    protected override Rotation RestRotation { get; } = new Rotation()
        .Cast("Healing Wave", c => c.Me.HealthPercent < 80d, onSelf: true);
}
