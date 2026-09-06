using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Marksmanship Hunter.
/// </summary>
/// <remarks>
/// Ranged damage with a pet holding whatever it shoots. The pet is the whole reason a hunter
/// grinds well unattended: it takes the damage the character would otherwise take, so keeping
/// it alive and on the right target matters more than the shot priority does.
/// </remarks>
public sealed class MarksmanshipHunter : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Marksmanship Hunter";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Hunter;

    /// <summary>Opens at range, which is the point of the specialisation.</summary>
    public override float PullRange => 35f;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Aspect of the Hawk", c => !c.HasAura(c.Me, "Aspect of the Hawk"), onSelf: true)
        .Cast("Trueshot Aura", c => !c.HasAura(c.Me, "Trueshot Aura"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Hunter's Mark", c => c.Target is not null && !c.HasAura(c.Target, "Hunter's Mark"))
        .Cast("Steady Shot");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .Cast("Feign Death", c => c.Me.HealthPercent < 20d, onSelf: true,
            description: "drops threat, which for a hunter is usually the way out")
        .Cast("Kill Shot", c => c.Target is { HealthPercent: < 20d }, description: "finisher")
        .Cast("Chimera Shot", description: "the specialisation's own shot, and its hardest hit")
        .Cast("Aimed Shot")
        .Cast("Arcane Shot")
        .Cast("Multi-Shot", c => c.EnemiesInMelee >= 2)
        .Cast("Steady Shot", description: "filler that keeps the rotation moving");

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

        // Heal it before sending it back in: a pet that dies mid-fight hands the whole fight
        // to the character, which is the situation this specialisation is worst at.
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
