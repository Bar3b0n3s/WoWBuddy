using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Demonology Warlock.
/// </summary>
/// <remarks>
/// The pet tree, and the most forgiving warlock to leave running: the Felguard holds things
/// while the character casts, and Soul Link shares the damage that does get through. Keeping the
/// pet alive is most of the job.
/// </remarks>
public sealed class DemonologyWarlock : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Demonology Warlock";

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
        .Cast("Fel Armor", c => !c.HasAura(c.Me, "Fel Armor"), onSelf: true)
        .Cast("Soul Link", c => !c.HasAura(c.Me, "Soul Link"), onSelf: true,
            description: "shares the damage with the pet, which is the tree's whole defence");

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Immolate")
        .Cast("Shadow Bolt");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .Cast("Death Coil", c => c.Me.HealthPercent < 25d)
        .Cast("Drain Life", c => c.Me.HealthPercent < 35d)

        .Cast("Immolate", c => c.Target is not null && !c.HasAura(c.Target, "Immolate"))
        .Cast("Corruption", c => c.Target is not null && !c.HasAura(c.Target, "Corruption"))
        .Cast("Demonic Empowerment", c => c.Pet is { IsAlive: true })
        .Cast("Incinerate", c => c.HasAura(c.Me, "Molten Core"))
        .Cast("Shadow Bolt", description: "filler");

    /// <inheritdoc />
    protected override Rotation RestRotation { get; } = new Rotation()
        .Cast("Life Tap", c => c.Me.PowerPercent < 40d && c.Me.HealthPercent > 70d, onSelf: true);

    /// <inheritdoc />
    public override bool PetControl(ICombatContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Pet is not { IsAlive: true } pet)
        {
            return context.Cast("Summon Felguard", onSelf: true)
                || context.Cast("Summon Voidwalker", onSelf: true);
        }

        if (pet.HealthPercent < 40d && context.IsSpellReady("Health Funnel"))
        {
            // Soul Link means a dead pet is a much bigger problem here than elsewhere.
            return context.Cast("Health Funnel");
        }

        if (context.Target is { IsAlive: true } target && pet.TargetGuid != target.Guid)
        {
            return context.Cast("Attack", onSelf: true);
        }

        return false;
    }
}
