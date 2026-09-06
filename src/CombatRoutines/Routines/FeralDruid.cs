using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Feral Druid.
/// </summary>
/// <remarks>
/// <para>
/// Cat form, because that is what a feral druid grinds in. Combo points work as they do for a
/// rogue, so the rules are ordered around building and spending rather than around which
/// ability hits hardest.
/// </para>
/// <para>
/// <b>Shred is left out on purpose.</b> It requires standing behind the target, and this bot
/// does not control its facing well enough to promise that — a rotation built on it would spend
/// the night failing to cast. Mangle is the filler instead, which is worse and works.
/// </para>
/// <para>
/// Bear form is not modelled. Tanking as a druid is a different rotation in a different form,
/// and switching between them mid-fight is a decision this routine is not in a position to
/// make well.
/// </para>
/// </remarks>
public sealed class FeralDruid : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Feral Druid";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Druid;

    /// <inheritdoc />
    public override bool UsesDrinkablePower => false;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Mark of the Wild", c => !c.HasAura(c.Me, "Mark of the Wild"), onSelf: true)
        .Cast("Cat Form", c => !c.HasAura(c.Me, "Cat Form"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Feral Charge - Cat")
        .Cast("Mangle (Cat)");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .Cast("Survival Instincts", c => c.Me.HealthPercent < 30d, onSelf: true)

        .Cast("Savage Roar",
            c => c.ComboPoints >= 1 && !c.HasAura(c.Me, "Savage Roar"),
            onSelf: true,
            description: "makes everything after it hit harder, so it goes first among spenders")
        .Cast("Rip",
            c => c.ComboPoints >= 4 && c.Target is not null && !c.HasAura(c.Target, "Rip"))
        .Cast("Ferocious Bite", c => c.ComboPoints >= 5)
        .Cast("Rake", c => c.Target is not null && !c.HasAura(c.Target, "Rake"))
        .Cast("Swipe (Cat)", c => c.EnemiesInMelee >= 3)
        .Cast("Mangle (Cat)", c => c.ComboPoints < 5, description: "the builder that needs no positioning");
}
