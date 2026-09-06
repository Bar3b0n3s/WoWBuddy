using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Restoration Druid.
/// </summary>
/// <remarks>
/// Heals over time rather than heals, which makes it the healing tree a bot plays closest to
/// correctly: keeping Rejuvenation on whoever lacks it is a checklist, and checklists are what
/// a rotation is good at. Swiftmend and Nature's Swiftness are the emergencies, kept for them.
/// </remarks>
public sealed class RestorationDruid : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Restoration Druid";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Druid;

    /// <inheritdoc />
    public override float PullRange => 30f;

    /// <inheritdoc />
    public override double RestHealthPercent => 50d;

    /// <inheritdoc />
    public override double RestPowerPercent => 55d;

    /// <inheritdoc />
    public override double ReadyPowerPercent => 80d;

    /// <inheritdoc />
    /// <remarks>A healer has plenty to do with nothing targeted, starting with healing.</remarks>
    protected override bool NeedsTargetToFight => false;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Mark of the Wild", c => !c.HasAura(c.Me, "Mark of the Wild"), onSelf: true)
        .Cast("Thorns", c => !c.HasAura(c.Me, "Thorns"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Moonfire")
        .Cast("Wrath");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .CastOn("Swiftmend",
            c => GroupHealing.MostHurt(c, 40d) is { } hurt && c.HasAura(hurt, "Rejuvenation")
                ? hurt
                : null,
            description: "instant, but it eats the heal over time it is standing on")
        .CastOn("Regrowth", c => GroupHealing.MostHurt(c, 50d))
        .CastOn("Rejuvenation",
            c => GroupHealing.MostHurt(c, 90d) is { } hurt && !c.HasAura(hurt, "Rejuvenation")
                ? hurt
                : null,
            description: "the checklist that is most of this tree")
        .Cast("Wild Growth", c => GroupHealing.CountBelow(c, 75d) >= 3)
        .CastOn("Nourish", c => GroupHealing.MostHurt(c, 70d))

        .Cast("Moonfire", c => c.Target is not null && !c.HasAura(c.Target, "Moonfire"))
        .Cast("Wrath", c => c.Target is not null, description: "damage when nobody needs healing");

    /// <inheritdoc />
    protected override Rotation RestRotation { get; } = new Rotation()
        .Cast("Rejuvenation", c => c.Me.HealthPercent < 85d && !c.HasAura(c.Me, "Rejuvenation"), onSelf: true);
}
