using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Assassination Rogue.
/// </summary>
/// <remarks>
/// <para>
/// Poisons and bleeds, spent through Envenom. Every rogue rotation is really two decisions —
/// build a combo point or spend the ones you have — so the rules are ordered around
/// <see cref="ICombatContext.ComboPoints"/> rather than around which ability hits hardest.
/// </para>
/// <para>
/// <b>No positional abilities.</b> Backstab and Ambush require standing behind the target, and
/// this bot does not control its facing well enough to promise that. A rotation that assumed it
/// would spend the night failing to cast.
/// </para>
/// </remarks>
public sealed class AssassinationRogue : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Assassination Rogue";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Rogue;

    /// <inheritdoc />
    public override bool UsesDrinkablePower => false;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Slice and Dice", c => c.ComboPoints >= 1 && !c.HasAura(c.Me, "Slice and Dice"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Mutilate")
        .Cast("Sinister Strike", description: "for characters that do not have Mutilate yet");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .Cast("Evasion", c => c.Me.HealthPercent < 35d, onSelf: true)
        .Cast("Vanish", c => c.Me.HealthPercent < 15d, onSelf: true, description: "the way out")

        // Slice and Dice first among the spenders: it makes every attack after it faster, so
        // spending on anything else while it is down is worth less than it looks.
        .Cast("Slice and Dice",
            c => c.ComboPoints >= 2 && !c.HasAura(c.Me, "Slice and Dice"),
            onSelf: true)
        .Cast("Rupture",
            c => c.ComboPoints >= 4 && c.Target is not null && !c.HasAura(c.Target, "Rupture"))
        .Cast("Envenom", c => c.ComboPoints >= 4, description: "the spender this specialisation is for")
        .Cast("Fan of Knives", c => c.EnemiesInMelee >= 3)
        .Cast("Mutilate", c => c.ComboPoints < 5, description: "builds two at a time")
        .Cast("Sinister Strike", c => c.ComboPoints < 5);
}
