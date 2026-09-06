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

    /// <summary>
    /// Health below which somebody is worth an emergency cast rather than a steady one.
    /// </summary>
    private const double Emergency = 45d;

    /// <inheritdoc />
    /// <remarks>
    /// Healing above damage, and the group's health above the character's own position in the
    /// list. Solo, the group is empty and every rule below reads exactly as it did before
    /// groups existed: the character is the only member, so it heals itself.
    /// </remarks>
    protected override Rotation CombatRotation { get; } = new Rotation()
        // Emergencies first, on whoever is in one. A healer that finishes its cast bar while
        // the tank dies has its priorities the wrong way round.
        .CastOn("Flash Heal",
            c => GroupHealing.MostHurt(c, Emergency),
            description: "emergency heal on whoever is worst off")

        .CastOn("Power Word: Shield",
            c => GroupHealing.MostHurt(c, 70d) is { } hurt && !c.HasAura(hurt, "Weakened Soul")
                ? hurt
                : null,
            description: "absorbs before the damage lands, unlike a heal")

        // Worth a group heal only when it saves more than one person; otherwise it is a slower,
        // costlier single heal.
        .Cast("Prayer of Healing",
            c => GroupHealing.CountBelow(c, 65d) >= 3,
            description: "three or more hurt makes it cheaper per point than healing each")

        .CastOn("Renew",
            c => GroupHealing.MostHurt(c, 80d) is { } hurt && !c.HasAura(hurt, "Renew") ? hurt : null,
            description: "cheap and keeps working while casting something else")

        .CastOn("Heal",
            c => GroupHealing.MostHurt(c, 70d),
            description: "the efficient heal when there is time for it")

        // Every rule below this line is damage, reached only when nobody needs healing.
        .Cast("Shadow Word: Pain",
            c => c.Target is not null && !c.HasAura(c.Target, "Shadow Word: Pain"),
            description: "kept up rather than recast")
        .Cast("Holy Fire", c => c.Target is not null)
        .Cast("Smite", c => c.Target is not null, description: "filler");

    /// <inheritdoc />
    protected override Rotation RestRotation { get; } = new Rotation()
        .Cast("Renew", c => c.Me.HealthPercent < 90d && !c.HasAura(c.Me, "Renew"), onSelf: true)
        .Cast("Flash Heal", c => c.Me.HealthPercent < 60d, onSelf: true);
}
