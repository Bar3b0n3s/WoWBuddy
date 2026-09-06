using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Frost Death Knight.
/// </summary>
/// <remarks>
/// Faster than Blood and rather less durable. Howling Blast makes it the death knight tree that
/// handles several things at once best, which for a grinder that occasionally pulls two extra
/// is worth having. Runes are left to the client, as in <see cref="BloodDeathKnight"/>.
/// </remarks>
public sealed class FrostDeathKnight : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Frost Death Knight";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.DeathKnight;

    /// <inheritdoc />
    public override float PullRange => 30f;

    /// <inheritdoc />
    public override bool UsesDrinkablePower => false;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Frost Presence", c => !c.HasAura(c.Me, "Frost Presence"), onSelf: true)
        .Cast("Horn of Winter", c => !c.HasAura(c.Me, "Horn of Winter"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Death Grip")
        .Cast("Howling Blast")
        .Cast("Icy Touch");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .Cast("Icebound Fortitude", c => c.Me.HealthPercent < 30d, onSelf: true)

        .Cast("Icy Touch", c => c.Target is not null && !c.HasAura(c.Target, "Frost Fever"))
        .Cast("Plague Strike", c => c.Target is not null && !c.HasAura(c.Target, "Blood Plague"))

        .Cast("Howling Blast", c => c.EnemiesInMelee >= 2, description: "hits the whole pack")
        .Cast("Obliterate", description: "the main strike, once the diseases are up")
        .Cast("Blood Strike")
        .Cast("Frost Strike", c => c.Me.Power >= 40, description: "runic power dump");
}
