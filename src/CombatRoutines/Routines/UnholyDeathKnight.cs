using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Unholy Death Knight.
/// </summary>
/// <remarks>
/// Diseases and a permanent ghoul. The pet matters here in the way a hunter's does — it holds
/// things and takes damage the character otherwise would — so it is kept summoned and kept on
/// the right target.
/// </remarks>
public sealed class UnholyDeathKnight : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Unholy Death Knight";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.DeathKnight;

    /// <inheritdoc />
    public override float PullRange => 30f;

    /// <inheritdoc />
    public override bool UsesDrinkablePower => false;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Unholy Presence", c => !c.HasAura(c.Me, "Unholy Presence"), onSelf: true)
        .Cast("Horn of Winter", c => !c.HasAura(c.Me, "Horn of Winter"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Death Grip")
        .Cast("Icy Touch");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .Cast("Icebound Fortitude", c => c.Me.HealthPercent < 30d, onSelf: true)

        .Cast("Icy Touch", c => c.Target is not null && !c.HasAura(c.Target, "Frost Fever"))
        .Cast("Plague Strike", c => c.Target is not null && !c.HasAura(c.Target, "Blood Plague"))

        .Cast("Scourge Strike", description: "hits harder the more diseases are on the target")
        .Cast("Blood Strike")
        .Cast("Death and Decay", c => c.EnemiesInMelee >= 3)
        .Cast("Death Coil", c => c.Me.Power >= 40, description: "runic power dump");

    /// <inheritdoc />
    public override bool PetControl(ICombatContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Pet is not { IsAlive: true } pet)
        {
            return context.Cast("Raise Dead", onSelf: true);
        }

        if (context.Target is { IsAlive: true } target && pet.TargetGuid != target.Guid)
        {
            return context.Cast("Attack", onSelf: true);
        }

        return false;
    }
}
