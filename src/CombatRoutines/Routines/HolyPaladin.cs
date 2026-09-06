using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Holy Paladin.
/// </summary>
/// <remarks>
/// A single-target healer with almost no group healing in Wrath, so the rotation is a ladder of
/// heals sized to how badly someone is hurt: the instant for emergencies, the big one when
/// there is time, and the cheap one to top up. Solo, the group is empty and every rule resolves
/// to the character itself.
/// </remarks>
public sealed class HolyPaladin : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Holy Paladin";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Paladin;

    /// <inheritdoc />
    public override float PullRange => 20f;

    /// <summary>It heals through damage rather than stopping, so it can afford a lower floor.</summary>
    public override double RestHealthPercent => 50d;

    /// <summary>Mana is the real constraint.</summary>
    public override double RestPowerPercent => 55d;

    /// <inheritdoc />
    public override double ReadyPowerPercent => 80d;

    /// <inheritdoc />
    /// <remarks>A healer has plenty to do with nothing targeted, starting with healing.</remarks>
    protected override bool NeedsTargetToFight => false;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Blessing of Kings", c => !c.HasAura(c.Me, "Blessing of Kings"), onSelf: true)
        .Cast("Devotion Aura", c => !c.HasAura(c.Me, "Devotion Aura"), onSelf: true)
        .Cast("Seal of Wisdom", c => !c.HasAura(c.Me, "Seal of Wisdom"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Judgement of Wisdom")
        .Cast("Exorcism");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .CastOn("Lay on Hands",
            c => GroupHealing.MostHurt(c, 15d),
            description: "the one that saves a run, kept for when it would")
        .CastOn("Holy Shock",
            c => GroupHealing.MostHurt(c, 40d),
            description: "instant, so it is the emergency heal")
        .CastOn("Flash of Light",
            c => GroupHealing.MostHurt(c, 60d),
            description: "fast and cheap")
        .CastOn("Holy Light",
            c => GroupHealing.MostHurt(c, 80d),
            description: "the big heal, for when there is time for it")
        .Cast("Judgement of Wisdom", c => c.Target is not null, description: "keeps mana coming back")
        .Cast("Consecration", c => c.EnemiesInMelee >= 2)
        .Cast("Holy Shock", c => c.Target is not null, description: "damage when nobody needs healing");

    /// <inheritdoc />
    protected override Rotation RestRotation { get; } = new Rotation()
        .Cast("Flash of Light", c => c.Me.HealthPercent < 70d, onSelf: true);
}
