using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Holy Priest.
/// </summary>
/// <remarks>
/// <para>
/// The healer case. A healer grinding solo is mostly a slower caster with a much longer
/// safety margin: the damage rotation is thin, but the character almost never dies, which for
/// an unattended bot is worth more than kill speed.
/// </para>
/// <para>
/// Healing is placed above damage in the combat rotation on purpose. A rotation that damages
/// first and heals with what is left over is how a healer dies with a full mana bar.
/// </para>
/// </remarks>
public sealed class HolyPriest : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Holy Priest";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Priest;

    /// <inheritdoc />
    public override float PullRange => 30f;

    /// <summary>Heals through damage rather than stopping, so it can afford a lower floor.</summary>
    public override double RestHealthPercent => 50d;

    /// <summary>Mana is the real constraint; it stops to drink well before it is empty.</summary>
    public override double RestPowerPercent => 55d;

    /// <inheritdoc />
    public override double ReadyPowerPercent => 80d;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Power Word: Fortitude", c => !c.HasAura(c.Me, "Power Word: Fortitude"), onSelf: true)
        .Cast("Inner Fire", c => !c.HasAura(c.Me, "Inner Fire"), onSelf: true)
        .Cast("Divine Spirit", c => !c.HasAura(c.Me, "Divine Spirit"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Shadow Word: Pain", description: "opener; it keeps ticking while other things happen")
        .Cast("Smite");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        // Survival first. Every rule below this line is a damage rule, and a healer that
        // reaches them while dying has its priorities the wrong way round.
        .Cast("Power Word: Shield",
            c => c.Me.HealthPercent < 70d && !c.HasAura(c.Me, "Weakened Soul"),
            onSelf: true,
            description: "absorbs before the damage lands, unlike a heal")
        .Cast("Flash Heal",
            c => c.Me.HealthPercent < 45d,
            onSelf: true,
            description: "emergency heal")
        .Cast("Renew",
            c => c.Me.HealthPercent < 80d && !c.HasAura(c.Me, "Renew"),
            onSelf: true,
            description: "cheap and keeps working while casting something else")
        .Cast("Shadow Word: Pain",
            c => c.Target is not null && !c.HasAura(c.Target, "Shadow Word: Pain"),
            description: "kept up rather than recast")
        .Cast("Holy Fire")
        .Cast("Smite", description: "filler");

    /// <inheritdoc />
    protected override Rotation RestRotation { get; } = new Rotation()
        .Cast("Renew", c => c.Me.HealthPercent < 90d && !c.HasAura(c.Me, "Renew"), onSelf: true)
        .Cast("Flash Heal", c => c.Me.HealthPercent < 60d, onSelf: true);
}
