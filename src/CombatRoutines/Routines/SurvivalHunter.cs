using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Survival Hunter.
/// </summary>
/// <remarks>
/// Built around Explosive Shot and traps rather than raw shot damage. The traps are the
/// interesting part for a bot and also the part it plays worst: laying one usefully means
/// knowing where something will walk, which this bot does not. So they are used defensively —
/// something is on the character and needs to stop being — rather than as an opener.
/// </remarks>
public sealed class SurvivalHunter : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Survival Hunter";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Hunter;

    /// <inheritdoc />
    public override float PullRange => 35f;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Aspect of the Dragonhawk", c => !c.HasAura(c.Me, "Aspect of the Dragonhawk"), onSelf: true)
        .Cast("Aspect of the Hawk",
            c => !c.HasAura(c.Me, "Aspect of the Hawk") && !c.HasAura(c.Me, "Aspect of the Dragonhawk"),
            onSelf: true,
            description: "the earlier aspect, for characters that do not have the later one");

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Hunter's Mark", c => c.Target is not null && !c.HasAura(c.Target, "Hunter's Mark"))
        .Cast("Explosive Shot")
        .Cast("Steady Shot");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .Cast("Feign Death", c => c.Me.HealthPercent < 20d, onSelf: true)
        .Cast("Freezing Trap",
            c => c.Me.HealthPercent < 40d && c.EnemiesInMelee >= 1,
            description: "used to get something off the character, not as an opener")
        .Cast("Kill Shot", c => c.Target is { HealthPercent: < 20d })
        .Cast("Explosive Shot", description: "the specialisation, and it is kept rolling")
        .Cast("Black Arrow")
        .Cast("Multi-Shot", c => c.EnemiesInMelee >= 2)
        .Cast("Serpent Sting",
            c => c.Target is not null && !c.HasAura(c.Target, "Serpent Sting"))
        .Cast("Steady Shot");

    /// <inheritdoc />
    protected override Rotation RestRotation { get; } = new Rotation()
        .Cast("Mend Pet", c => c.Pet is { IsAlive: true, HealthPercent: < 80d });

    /// <inheritdoc />
    public override bool PetControl(ICombatContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Pet is not { } pet)
        {
            return context.Cast("Call Pet", onSelf: true);
        }

        if (!pet.IsAlive)
        {
            return context.Cast("Revive Pet", onSelf: true);
        }

        if (pet.HealthPercent < 50d && context.IsSpellReady("Mend Pet"))
        {
            return context.Cast("Mend Pet");
        }

        if (context.Target is { IsAlive: true } target && pet.TargetGuid != target.Guid)
        {
            return context.Cast("Attack", onSelf: true);
        }

        return false;
    }
}
