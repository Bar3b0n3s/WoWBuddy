using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines.Routines;

/// <summary>
/// Destruction Warlock.
/// </summary>
/// <remarks>
/// Direct damage rather than dots, with Immolate kept up because Conflagrate consumes it. The
/// fastest warlock at killing one thing, and the one most likely to pull a second by killing
/// the first too slowly to matter — so the imp stays out and the health-for-mana trade is used
/// between pulls rather than during them.
/// </remarks>
public sealed class DestructionWarlock : RoutineBase
{
    /// <inheritdoc />
    public override string Name => "Destruction Warlock";

    /// <inheritdoc />
    public override WoWClass Class => WoWClass.Warlock;

    /// <inheritdoc />
    public override float PullRange => 30f;

    /// <inheritdoc />
    public override double RestHealthPercent => 50d;

    /// <inheritdoc />
    public override double RestPowerPercent => 40d;

    /// <inheritdoc />
    protected override Rotation BuffRotation { get; } = new Rotation()
        .Cast("Fel Armor", c => !c.HasAura(c.Me, "Fel Armor"), onSelf: true);

    /// <inheritdoc />
    protected override Rotation PullRotation { get; } = new Rotation()
        .Cast("Immolate")
        .Cast("Shadow Bolt");

    /// <inheritdoc />
    protected override Rotation CombatRotation { get; } = new Rotation()
        .Cast("Death Coil", c => c.Me.HealthPercent < 25d)
        .Cast("Drain Life", c => c.Me.HealthPercent < 35d)

        .Cast("Immolate",
            c => c.Target is not null && !c.HasAura(c.Target, "Immolate"),
            description: "kept up, because Conflagrate eats it")
        .Cast("Conflagrate", c => c.HasAura(c.Target, "Immolate"))
        .Cast("Chaos Bolt")
        .Cast("Incinerate", description: "hits harder while Immolate is on")
        .Cast("Shadow Bolt");

    /// <inheritdoc />
    protected override Rotation RestRotation { get; } = new Rotation()
        .Cast("Life Tap", c => c.Me.PowerPercent < 40d && c.Me.HealthPercent > 70d, onSelf: true);

    /// <inheritdoc />
    public override bool PetControl(ICombatContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Pet is not { IsAlive: true })
        {
            return context.Cast("Summon Imp", onSelf: true);
        }

        return false;
    }
}
