using WoWBuddy.CombatRoutines;
using WoWBuddy.Core.Objects;

namespace WoWBuddy.CombatRoutines.Tests;

/// <summary>
/// A combat situation written down rather than read from a client.
/// </summary>
/// <remarks>
/// This is what makes rotations checkable. A rule is a pure function of a situation, so a
/// situation can be stated — half health, target nearly dead, this proc up, that spell on
/// cooldown — and the rotation then has exactly one right answer. Rotations are the part of a
/// bot users edit most, and without this the only way to check an edit is to die of it.
/// </remarks>
public sealed class FakeCombat : ICombatContext
{
    /// <summary>Spells that are ready. Anything not listed is treated as unavailable.</summary>
    public HashSet<string> Ready { get; } = [];

    /// <summary>Auras on the character.</summary>
    public HashSet<string> MyAuras { get; } = [];

    /// <summary>Auras on the target.</summary>
    public HashSet<string> TargetAuras { get; } = [];

    /// <summary>Auras on the pet.</summary>
    public HashSet<string> PetAuras { get; } = [];

    /// <summary>Spells the rotation asked to cast, in order.</summary>
    public List<string> CastLog { get; } = [];

    /// <summary>Spells the client will refuse.</summary>
    public HashSet<string> Refused { get; } = [];

    /// <inheritdoc />
    public UnitSnapshot Me { get; set; } = new(
        new WoWGuid(0x1), 100d, 100d, 100, 80, IsAlive: true, 0f, WoWGuid.Zero);

    /// <inheritdoc />
    public UnitSnapshot? Target { get; set; }

    /// <inheritdoc />
    public UnitSnapshot? Pet { get; set; }

    /// <inheritdoc />
    public int EnemiesInMelee { get; set; }

    /// <inheritdoc />
    public bool IsCasting { get; set; }

    /// <summary>Marks the listed spells ready and everything else not.</summary>
    public FakeCombat WithReady(params string[] spells)
    {
        Ready.Clear();
        foreach (string spell in spells)
        {
            Ready.Add(spell);
        }

        return this;
    }

    /// <summary>Places a target at the given health.</summary>
    public FakeCombat WithTarget(double healthPercent = 100d, bool alive = true, float distance = 5f)
    {
        Target = new UnitSnapshot(
            new WoWGuid(0xF130000000000002), healthPercent, 100d, 0, 80, alive, distance, WoWGuid.Zero);
        return this;
    }

    /// <summary>Places a pet at the given health, optionally already on the target.</summary>
    public FakeCombat WithPet(double healthPercent = 100d, bool alive = true, bool onTarget = false)
    {
        Pet = new UnitSnapshot(
            new WoWGuid(0xF140000000000003), healthPercent, 100d, 0, 80, alive, 5f,
            onTarget && Target is { } t ? t.Guid : WoWGuid.Zero);
        return this;
    }

    /// <summary>Sets the character's health.</summary>
    public FakeCombat WithHealth(double percent)
    {
        Me = Me with { HealthPercent = percent };
        return this;
    }

    /// <summary>Sets the character's power bar.</summary>
    public FakeCombat WithPower(double percent, uint raw = 0)
    {
        Me = Me with { PowerPercent = percent, Power = raw };
        return this;
    }

    /// <inheritdoc />
    public bool IsSpellReady(string spellName) => Ready.Contains(spellName);

    /// <inheritdoc />
    public bool HasAura(UnitSnapshot unit, string auraName)
    {
        if (unit.Guid == Me.Guid)
        {
            return MyAuras.Contains(auraName);
        }

        if (Target is { } target && unit.Guid == target.Guid)
        {
            return TargetAuras.Contains(auraName);
        }

        if (Pet is { } pet && unit.Guid == pet.Guid)
        {
            return PetAuras.Contains(auraName);
        }

        return false;
    }

    /// <inheritdoc />
    public bool Cast(string spellName, bool onSelf = false)
    {
        if (Refused.Contains(spellName))
        {
            return false;
        }

        CastLog.Add(spellName);
        return true;
    }
}
