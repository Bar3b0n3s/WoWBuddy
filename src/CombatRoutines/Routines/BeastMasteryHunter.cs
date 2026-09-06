using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Beast Mastery Hunter.
/// </summary>
/// <remarks>
/// <para>
/// The pet case, and the strongest solo grinder in this expansion for a reason that matters
/// to a bot: the pet holds the target, so the character takes almost no damage and rarely has
/// to stop. Keeping the pet alive and attacking is therefore more important than any part of
/// the shot rotation, which is why <see cref="PetControl"/> exists at all.
/// </para>
/// <para>
/// The dead zone is the awkward part. A hunter cannot shoot inside about eight yards, so a
/// mob that reaches melee has to be dealt with rather than shot at, and a rotation that
/// ignores that produces a character standing still doing nothing.
/// </para>
/// </remarks>
public sealed class BeastMasteryHunter : RoutineBase
{
    /// <summary>Inside this range a hunter cannot use ranged attacks at all.</summary>
    public const float DeadZoneRange = 8f;

    /// <inheritdoc />
    public override string Name => "Beast Mastery Hunter";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Hunter;

    /// <inheritdoc />
    public override float PullRange => 35f;

    /// <summary>Mana matters, but the pet does the work, so it need not be near full.</summary>
    public override double RestPowerPercent => 30d;

    /// <inheritdoc />
    public override double ReadyPowerPercent => 50d;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Aspect of the Hawk", c => !c.HasAura(c.Me, "Aspect of the Hawk"), onSelf: true)
        .Cast("Call Pet", c => c.Pet is null, onSelf: true, description: "no pet is a broken hunter")
        .Cast("Revive Pet", c => c.Pet is { IsDead: true }, onSelf: true)
        .Cast("Mend Pet",
            c => c.Pet is { IsAlive: true, HealthPercent: < 60d } && !c.HasAura(c.Pet, "Mend Pet"),
            description: "topped up between fights so the pet starts the next one healthy");

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Hunter's Mark", c => c.Target is not null && !c.HasAura(c.Target, "Hunter's Mark"))
        .Cast("Serpent Sting", description: "opener; the pet is sent in by PetControl");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .Cast("Mend Pet",
            c => c.Pet is { IsAlive: true, HealthPercent: < 45d } && !c.HasAura(c.Pet, "Mend Pet"),
            description: "the pet is the tank; losing it means taking the damage instead")
        // Inside the dead zone nothing ranged will fire, so melee or make room instead.
        .Cast("Raptor Strike",
            c => c.EnemiesInMelee > 0,
            description: "melee, because shots do not work this close")
        .Cast("Disengage",
            c => c.EnemiesInMelee > 0 && c.Me.HealthPercent < 50d,
            description: "makes room to shoot again")
        .Cast("Kill Command",
            c => c.Pet is { IsAlive: true },
            description: "free damage through the pet")
        .Cast("Serpent Sting",
            c => c.Target is not null && !c.HasAura(c.Target, "Serpent Sting"),
            description: "kept up rather than recast")
        .Cast("Arcane Shot")
        .Cast("Steady Shot", description: "filler");

    /// <inheritdoc />
    public override bool PetControl(ICombatContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Pet is not { IsAlive: true } pet)
        {
            return false;
        }

        // Send the pet at anything the character is fighting that the pet is not. Without
        // this the hunter takes the damage the pet is supposed to be taking.
        if (context.Target is { IsAlive: true } target && pet.TargetGuid != target.Guid)
        {
            return context.Cast("Attack", onSelf: true);
        }

        return false;
    }
}
