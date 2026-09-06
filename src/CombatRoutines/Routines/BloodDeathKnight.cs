using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Blood Death Knight.
/// </summary>
/// <remarks>
/// <para>
/// In Wrath this is both the tanking tree and the best solo grinding tree, because Death Strike
/// heals for a share of the damage taken. A character that heals itself by fighting almost
/// never sits down, which over a night is worth far more than a faster kill.
/// </para>
/// <para>
/// Runes are not modelled here. The client already refuses a strike whose runes are spent, so
/// <see cref="ICombatContext.IsSpellReady"/> answers the question — a rune tracker would be a
/// second, worse copy of something the client is already authoritative about.
/// </para>
/// </remarks>
public sealed class BloodDeathKnight : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Blood Death Knight";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.DeathKnight;

    /// <summary>Death Grip opens the fight from range by dragging the target in.</summary>
    public override float PullRange => 30f;

    /// <inheritdoc />
    public override bool UsesDrinkablePower => false;

    /// <summary>It heals itself by fighting, so it stops far less often than most melee.</summary>
    public override double RestHealthPercent => 40d;

    /// <inheritdoc />
    public override double ReadyHealthPercent => 70d;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Blood Presence", c => !c.HasAura(c.Me, "Blood Presence"), onSelf: true)
        .Cast("Horn of Winter", c => !c.HasAura(c.Me, "Horn of Winter"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Death Grip", description: "brings it to the character rather than the other way round")
        .Cast("Icy Touch");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .Cast("Rune Tap", c => c.Me.HealthPercent < 50d, onSelf: true)
        .Cast("Icebound Fortitude", c => c.Me.HealthPercent < 30d, onSelf: true)

        // The diseases first: everything else in the tree hits harder while they are up.
        .Cast("Icy Touch", c => c.Target is not null && !c.HasAura(c.Target, "Frost Fever"))
        .Cast("Plague Strike", c => c.Target is not null && !c.HasAura(c.Target, "Blood Plague"))

        .Cast("Death Strike", description: "the heal, and the reason this grinds so well")
        .Cast("Heart Strike")
        .Cast("Blood Boil", c => c.EnemiesInMelee >= 2)
        .Cast("Death Coil", c => c.Me.Power >= 40, description: "runic power dump");
}
