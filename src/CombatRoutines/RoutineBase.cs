using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines;

/// <summary>
/// Shared scaffolding for a routine built from rotations.
/// </summary>
/// <remarks>
/// A routine subclasses this and fills in four rotations. The thresholds are properties rather
/// than constants so a specialisation that genuinely differs — a healer resting at a higher
/// mana level, say — can say so without reimplementing the plumbing.
/// </remarks>
public abstract class RoutineBase : ICombatRoutine
{
    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract WoWClass Class { get; }

    /// <inheritdoc />
    public virtual float PullRange => 5f;

    /// <summary>Health percentage below which the character should rest before fighting again.</summary>
    public virtual double RestHealthPercent => 60d;

    /// <summary>Power percentage below which the character should rest, for classes that drink.</summary>
    public virtual double RestPowerPercent => 40d;

    /// <summary>Health percentage the character must reach before fighting again.</summary>
    public virtual double ReadyHealthPercent => 85d;

    /// <summary>Power percentage the character must reach before fighting again.</summary>
    public virtual double ReadyPowerPercent => 70d;

    /// <summary>True when the class drinks to restore its power bar.</summary>
    /// <remarks>
    /// Rage and energy refill on their own and are wasted out of combat, so a warrior or
    /// rogue that waited for a full bar would wait forever.
    /// </remarks>
    public virtual bool UsesDrinkablePower => true;

    /// <summary>Buffs to keep up out of combat.</summary>
    protected abstract Rotation BuffRotation { get; }

    /// <summary>How to open a fight.</summary>
    protected abstract Rotation PullRotation { get; }

    /// <summary>How to fight.</summary>
    protected abstract Rotation CombatRotation { get; }

    /// <summary>What to do while recovering. May be empty.</summary>
    protected virtual Rotation RestRotation { get; } = new();

    /// <inheritdoc />
    public virtual bool Buff(ICombatContext context) => BuffRotation.Execute(context) is not null;

    /// <inheritdoc />
    public virtual bool Pull(ICombatContext context) => PullRotation.Execute(context) is not null;

    /// <inheritdoc />
    public virtual bool Combat(ICombatContext context) => CombatRotation.Execute(context) is not null;

    /// <inheritdoc />
    public virtual bool Rest(ICombatContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!NeedsRest(context))
        {
            return false;
        }

        RestRotation.Execute(context);
        return true;
    }

    /// <inheritdoc />
    /// <remarks>Does nothing unless a routine with a pet overrides it.</remarks>
    public virtual bool PetControl(ICombatContext context) => false;

    /// <inheritdoc />
    public virtual bool IsReadyToFight(ICombatContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Me.HealthPercent < ReadyHealthPercent)
        {
            return false;
        }

        return !UsesDrinkablePower || context.Me.PowerPercent >= ReadyPowerPercent;
    }

    /// <summary>True when the character is worn down enough to stop and recover.</summary>
    protected bool NeedsRest(ICombatContext context)
    {
        if (context.Me.HealthPercent < RestHealthPercent)
        {
            return true;
        }

        return UsesDrinkablePower && context.Me.PowerPercent < RestPowerPercent;
    }
}
