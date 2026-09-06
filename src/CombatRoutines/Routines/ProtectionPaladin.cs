using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Protection Paladin.
/// </summary>
/// <remarks>
/// The area-threat tank. Consecration and Holy Shield do most of the work of holding a pack
/// together, which makes this the easiest tanking specialisation for a bot to play acceptably:
/// much of its threat is passive area damage rather than a rotation that has to be executed
/// well.
/// </remarks>
public sealed class ProtectionPaladin : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Protection Paladin";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Paladin;

    /// <inheritdoc />
    public override float PullRange => 20f;

    /// <inheritdoc />
    public override double RestHealthPercent => 45d;

    /// <inheritdoc />
    public override double RestPowerPercent => 30d;

    /// <inheritdoc />
    public override double ReadyHealthPercent => 75d;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Blessing of Sanctuary", c => !c.HasAura(c.Me, "Blessing of Sanctuary"), onSelf: true)
        .Cast("Righteous Fury", c => !c.HasAura(c.Me, "Righteous Fury"), onSelf: true,
            description: "without this a paladin holds nothing")
        .Cast("Devotion Aura", c => !c.HasAura(c.Me, "Devotion Aura"), onSelf: true)
        .Cast("Seal of Vengeance", c => !c.HasAura(c.Me, "Seal of Vengeance"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Avenger's Shield", description: "ranged, and pulls a whole pack at once")
        .Cast("Judgement of Wisdom");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .Cast("Divine Protection", c => c.Me.HealthPercent < 30d, onSelf: true)
        .Cast("Lay on Hands", c => c.Me.HealthPercent < 15d, onSelf: true)
        .Cast("Flash of Light", c => c.Me.HealthPercent < 45d, onSelf: true)

        .Cast("Holy Shield", c => !c.HasAura(c.Me, "Holy Shield"), onSelf: true)
        .Cast("Hammer of the Righteous", c => c.EnemiesInMelee >= 2)
        .Cast("Consecration", description: "the area threat that holds a pack")
        .Cast("Shield of Righteousness")
        .Cast("Judgement of Wisdom")
        .Cast("Avenger's Shield");
}
