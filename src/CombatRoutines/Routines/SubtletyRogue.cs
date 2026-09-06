using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Subtlety Rogue.
/// </summary>
/// <remarks>
/// <para>
/// The specialisation that suffers most from being played by a bot. Its damage comes from
/// opening from stealth in the right position and from Backstab, and both need facing this bot
/// does not control. What is left is Hemorrhage — no positional requirement — and the survival
/// tools, which are excellent.
/// </para>
/// <para>
/// So this rotation is honest about being a worse Combat Rogue with better escapes. Anyone
/// grinding on a rogue should use Combat; this is here so a character specced Subtlety is
/// played sensibly rather than badly.
/// </para>
/// </remarks>
public sealed class SubtletyRogue : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Subtlety Rogue";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Rogue;

    /// <inheritdoc />
    public override bool UsesDrinkablePower => false;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Slice and Dice", c => c.ComboPoints >= 1 && !c.HasAura(c.Me, "Slice and Dice"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Hemorrhage")
        .Cast("Sinister Strike");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .Cast("Evasion", c => c.Me.HealthPercent < 40d, onSelf: true)
        .Cast("Cloak of Shadows", c => c.Me.HealthPercent < 30d, onSelf: true)
        .Cast("Vanish", c => c.Me.HealthPercent < 15d, onSelf: true)

        .Cast("Slice and Dice",
            c => c.ComboPoints >= 2 && !c.HasAura(c.Me, "Slice and Dice"),
            onSelf: true)
        .Cast("Rupture",
            c => c.ComboPoints >= 4 && c.Target is not null && !c.HasAura(c.Target, "Rupture"))
        .Cast("Eviscerate", c => c.ComboPoints >= 4)
        .Cast("Fan of Knives", c => c.EnemiesInMelee >= 3)
        .Cast("Hemorrhage", c => c.ComboPoints < 5, description: "builds without needing to be behind")
        .Cast("Sinister Strike", c => c.ComboPoints < 5);
}
