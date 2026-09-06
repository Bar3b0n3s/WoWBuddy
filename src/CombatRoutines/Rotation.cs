using WoWBuddy.Core.Objects;

namespace WoWBuddy.CombatRoutines;

/// <summary>
/// What a rotation knows about one unit at the moment a decision is made.
/// </summary>
/// <remarks>
/// <para>
/// Plain values rather than a live game object, for two reasons. It keeps a rotation a pure
/// function of a situation, so a situation can be written down in a test and the rotation has
/// one right answer — which matters because rotations are the part of a bot users edit most,
/// and otherwise the only way to check an edit is to die of it.
/// </para>
/// <para>
/// It also means a rotation sees a consistent picture. Reading through to the client on every
/// property lets health change between two rules of the same decision, which produces
/// rotations that occasionally do something that made sense against neither reading.
/// </para>
/// </remarks>
/// <param name="Guid">The unit's GUID.</param>
/// <param name="HealthPercent">Health, 0 to 100.</param>
/// <param name="PowerPercent">Active power bar, 0 to 100.</param>
/// <param name="Power">Raw value of the active power bar.</param>
/// <param name="Level">Unit level.</param>
/// <param name="IsAlive">Whether it is alive.</param>
/// <param name="Distance">Yards from the character. Zero for the character itself.</param>
/// <param name="TargetGuid">What this unit is targeting.</param>
public readonly record struct UnitSnapshot(
    WoWGuid Guid,
    double HealthPercent,
    double PowerPercent,
    uint Power,
    int Level,
    bool IsAlive,
    float Distance,
    WoWGuid TargetGuid)
{
    /// <summary>True when the unit is dead.</summary>
    public bool IsDead => !IsAlive;
}

/// <summary>What a routine can see and do on one decision.</summary>
public interface ICombatContext
{
    /// <summary>The character.</summary>
    UnitSnapshot Me { get; }

    /// <summary>The current target, or null when nothing is targeted.</summary>
    UnitSnapshot? Target { get; }

    /// <summary>The character's pet, or null when there is none.</summary>
    UnitSnapshot? Pet { get; }

    /// <summary>Units currently within melee range of the character.</summary>
    int EnemiesInMelee { get; }

    /// <summary>True when the character is part way through a cast or channel.</summary>
    bool IsCasting { get; }

    /// <summary>True when the spell is known, usable and off cooldown.</summary>
    bool IsSpellReady(string spellName);

    /// <summary>True when the named aura is on the unit.</summary>
    bool HasAura(UnitSnapshot unit, string auraName);

    /// <summary>
    /// True when the named aura is on the unit, treating a missing unit as not having it.
    /// </summary>
    /// <remarks>
    /// Saves every rule that mentions a target or a pet from restating what to do when there
    /// is not one. A rule like "refresh the damage-over-time when the target lacks it" should
    /// simply not fire when there is no target, and this is what makes that read naturally.
    /// </remarks>
    bool HasAura(UnitSnapshot? unit, string auraName) => unit is { } present && HasAura(present, auraName);

    /// <summary>Casts a spell.</summary>
    bool Cast(string spellName, bool onSelf = false);

    // ---- group play, added in phase 8 --------------------------------------------------

    /// <summary>
    /// The rest of the character's group, nearest first. Empty when playing alone.
    /// </summary>
    /// <remarks>
    /// Defaulted to empty so that every routine written before groups existed still compiles
    /// and behaves exactly as it did: a rotation that never mentions the group is a solo
    /// rotation, which is what most of them are.
    /// </remarks>
    IReadOnlyList<UnitSnapshot> Group => [];

    /// <summary>
    /// Casts a spell on someone else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="Cast"/> because healing someone must not disturb the
    /// character's own target. Retargeting to heal and back again loses casts, breaks
    /// channelled spells, and on a damage character means the next attack goes at whatever was
    /// healed. The client can cast directly at a unit without changing target, and this is
    /// that.
    /// </para>
    /// <para>
    /// Defaulted to failing rather than falling back to <see cref="Cast"/>: a routine asking to
    /// heal someone specific and silently healing the wrong unit is worse than not healing.
    /// </para>
    /// </remarks>
    bool CastOn(string spellName, UnitSnapshot unit) => false;

    /// <summary>
    /// Combo points on the current target, 0 to 5.
    /// </summary>
    /// <remarks>
    /// Only rogues and cat-form druids have these, so it is defaulted rather than forced on
    /// every context. The live implementation reads <c>GetComboPoints("player", "target")</c>.
    /// </remarks>
    int ComboPoints => 0;
}

/// <summary>
/// One rule in a rotation: a spell, and when to use it.
/// </summary>
/// <param name="SpellName">The spell to cast.</param>
/// <param name="When">When the rule applies. Null means whenever the spell is ready.</param>
/// <param name="OnSelf">Whether the spell targets the character.</param>
/// <param name="Description">What the rule is for, shown in logs and the rotation editor.</param>
/// <param name="Selector">
/// Picks who to cast at, for rules that heal or buff someone other than the character or its
/// target. Returning null means the rule does not apply this time.
/// </param>
public sealed record RotationRule(
    string SpellName,
    Func<ICombatContext, bool>? When = null,
    bool OnSelf = false,
    string Description = "",
    Func<ICombatContext, UnitSnapshot?>? Selector = null)
{
    /// <summary>True when this rule should fire in the given situation.</summary>
    public bool Applies(ICombatContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Readiness is checked first because it is cheap and rules out most rules; the
        // condition may cost a round trip to the client.
        if (!context.IsSpellReady(SpellName) || (When is not null && !When(context)))
        {
            return false;
        }

        // A rule that names who to cast at does not apply when there is nobody to cast at.
        // That is what lets "heal whoever is hurt" sit in a rotation without a condition
        // repeating the same search.
        return Selector is null || Selector(context) is not null;
    }

    /// <summary>Casts this rule.</summary>
    public bool Fire(ICombatContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Selector is null)
        {
            return context.Cast(SpellName, OnSelf);
        }

        return Selector(context) is { } unit && context.CastOn(SpellName, unit);
    }

    public override string ToString() =>
        string.IsNullOrEmpty(Description) ? SpellName : $"{SpellName} ({Description})";
}

/// <summary>
/// An ordered list of rules, cast highest-priority-first.
/// </summary>
/// <remarks>
/// <para>
/// A rotation is a priority list rather than a sequence, which is how players actually think
/// about them: "keep this up, use that on cooldown, otherwise filler". Each decision walks the
/// list from the top and casts the first rule that applies, so a proc or a low-health
/// emergency displaces the filler without any explicit state.
/// </para>
/// <para>
/// One cast per decision, deliberately. The client has a global cooldown and queuing a second
/// spell in the same instant cancels the first, so a rotation that tried to fire several rules
/// at once would cast fewer spells, not more.
/// </para>
/// </remarks>
public sealed class Rotation
{
    private readonly List<RotationRule> _rules = [];

    /// <summary>The rules, in priority order.</summary>
    public IReadOnlyList<RotationRule> Rules => _rules;

    /// <summary>Adds a rule below the ones already added.</summary>
    public Rotation Add(RotationRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _rules.Add(rule);
        return this;
    }

    /// <summary>Adds a rule with a condition.</summary>
    public Rotation Cast(
        string spellName,
        Func<ICombatContext, bool>? when = null,
        bool onSelf = false,
        string description = "") =>
        Add(new RotationRule(spellName, when, onSelf, description));

    /// <summary>Adds a rule that casts at whoever <paramref name="on"/> picks.</summary>
    /// <remarks>
    /// The healing form. The selector runs during the decision, so "whoever is most hurt" is
    /// evaluated against the same snapshot as everything else in the rotation rather than
    /// against a group that may have changed since.
    /// </remarks>
    public Rotation CastOn(
        string spellName,
        Func<ICombatContext, UnitSnapshot?> on,
        Func<ICombatContext, bool>? when = null,
        string description = "") =>
        Add(new RotationRule(spellName, when, OnSelf: false, description, on));

    /// <summary>
    /// Casts the highest-priority rule that applies.
    /// </summary>
    /// <returns>The rule that fired, or null when nothing applied.</returns>
    public RotationRule? Execute(ICombatContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Interrupting the bot's own cast to start another is almost always wrong: the first
        // is cancelled and nothing lands.
        if (context.IsCasting)
        {
            return null;
        }

        foreach (RotationRule rule in _rules)
        {
            if (!rule.Applies(context))
            {
                continue;
            }

            return rule.Fire(context) ? rule : null;
        }

        return null;
    }

    /// <summary>The first rule that would fire, without casting it.</summary>
    /// <remarks>For the rotation editor and for tests, which want the decision not the effect.</remarks>
    public RotationRule? Preview(ICombatContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.IsCasting ? null : _rules.FirstOrDefault(rule => rule.Applies(context));
    }
}
