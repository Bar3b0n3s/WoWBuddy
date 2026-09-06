using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Affliction Warlock.
/// </summary>
/// <remarks>
/// Dots and drains, which suits an unattended bot unusually well: Drain Life turns the fight
/// into health, and the pet holds whatever the dots are killing. The rotation is a checklist of
/// which dot is missing, then a filler.
/// </remarks>
public sealed class AfflictionWarlock : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Affliction Warlock";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Warlock;

    /// <inheritdoc />
    public override float PullRange => 30f;

    /// <inheritdoc />
    public override double RestHealthPercent => 45d;

    /// <inheritdoc />
    public override double RestPowerPercent => 35d;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Fel Armor", c => !c.HasAura(c.Me, "Fel Armor"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Haunt")
        .Cast("Corruption")
        .Cast("Shadow Bolt");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .Cast("Drain Life",
            c => c.Me.HealthPercent < 40d,
            description: "the reason this specialisation rarely has to stop")
        .Cast("Death Coil", c => c.Me.HealthPercent < 25d)

        .Cast("Corruption", c => c.Target is not null && !c.HasAura(c.Target, "Corruption"))
        .Cast("Unstable Affliction",
            c => c.Target is not null && !c.HasAura(c.Target, "Unstable Affliction"))
        .Cast("Curse of Agony", c => c.Target is not null && !c.HasAura(c.Target, "Curse of Agony"))
        .Cast("Haunt")
        .Cast("Drain Soul", c => c.Target is { HealthPercent: < 25d }, description: "finisher")
        .Cast("Shadow Bolt", description: "filler");

    /// <inheritdoc />
    protected override Rotation RestRotation { get; } = new Rotation()
        .Cast("Life Tap",
            c => c.Me.PowerPercent < 40d && c.Me.HealthPercent > 70d,
            onSelf: true,
            description: "trades health for mana, which is faster than drinking for both");

    /// <inheritdoc />
    public override bool PetControl(ICombatContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Pet is not { IsAlive: true } pet)
        {
            return context.Cast("Summon Felhunter", onSelf: true)
                || context.Cast("Summon Imp", onSelf: true);
        }

        if (context.Target is { IsAlive: true } target && pet.TargetGuid != target.Guid)
        {
            return context.Cast("Attack", onSelf: true);
        }

        return false;
    }
}
