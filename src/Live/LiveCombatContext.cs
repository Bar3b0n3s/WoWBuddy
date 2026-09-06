using System.Globalization;
using WoWBuddy.BotBases.Group;
using WoWBuddy.CombatRoutines;
using WoWBuddy.Core.Execution;
using WoWBuddy.Core.Objects;

namespace WoWBuddy.Live;

/// <summary>
/// What a combat routine sees, read from a live client.
/// </summary>
/// <remarks>
/// <para>
/// Snapshot-shaped on purpose. <see cref="ICombatContext"/> was written so that a rotation is a
/// pure function of a situation, and that only holds if the situation stops changing while the
/// rotation thinks about it. So the units are read once, when <see cref="Refresh"/> is called at
/// the top of a tick, and every rule in that decision sees the same numbers.
/// </para>
/// <para>
/// The questions that cannot be snapshotted — is this spell ready, is that aura up — go to the
/// client when asked, because a rotation asks about a handful of the character's hundred spells
/// and reading all of them every tick to snapshot them would cost far more than it saved.
/// </para>
/// <para>
/// <b>Casting goes through <see cref="SpellCaster"/>, which refuses until it has proved the
/// client runs the bot's scripts in a secure context.</b> That gate is the reason this project
/// casts through Lua at all rather than through a native address it could not verify.
/// </para>
/// </remarks>
public sealed class LiveCombatContext : ICombatContext
{
    private readonly ILuaEvaluator _lua;
    private readonly ICharacterView _view;
    private readonly SpellCaster? _caster;
    private readonly IPartyState? _party;

    private UnitSnapshot _me;
    private UnitSnapshot? _target;
    private UnitSnapshot? _pet;
    private IReadOnlyList<UnitSnapshot> _group = [];
    private int _enemiesInMelee;

    /// <summary>Builds a context over an attached client.</summary>
    /// <param name="lua">The client's scripting.</param>
    /// <param name="view">The verified half of the bot's picture of the world.</param>
    /// <param name="caster">
    /// Casting, gated on its own self-test. Omit and the context reads but never casts, which
    /// is what a routine run against a client that failed that test should do.
    /// </param>
    /// <param name="party">The group, for healing rotations.</param>
    public LiveCombatContext(
        ILuaEvaluator lua,
        ICharacterView view,
        SpellCaster? caster = null,
        IPartyState? party = null)
    {
        _lua = lua ?? throw new ArgumentNullException(nameof(lua));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _caster = caster;
        _party = party;

        Refresh();
    }

    /// <inheritdoc />
    public UnitSnapshot Me => _me;

    /// <inheritdoc />
    public UnitSnapshot? Target => _target;

    /// <inheritdoc />
    public UnitSnapshot? Pet => _pet;

    /// <inheritdoc />
    public int EnemiesInMelee => _enemiesInMelee;

    /// <inheritdoc />
    public IReadOnlyList<UnitSnapshot> Group => _group;

    /// <inheritdoc />
    public bool IsCasting =>
        _lua.EvaluateBool("(UnitCastingInfo(\"player\") or UnitChannelInfo(\"player\")) and true or false");

    /// <inheritdoc />
    public int ComboPoints => _lua.EvaluateInt("GetComboPoints(\"player\", \"target\")") ?? 0;

    /// <summary>How far counts as melee range.</summary>
    /// <remarks>
    /// Five yards is the usual melee reach in this expansion; the bot only uses this to decide
    /// whether an area attack is worth using, so being a yard out either way costs nothing.
    /// </remarks>
    public const float MeleeRange = 5f;

    /// <summary>
    /// Takes one consistent picture of the units, for the decision about to be made.
    /// </summary>
    /// <remarks>
    /// Called once at the top of a tick. Reading through to the client on every property would
    /// let health change between two rules of the same decision, and produce rotations that
    /// occasionally do something that made sense against neither reading.
    /// </remarks>
    public void Refresh()
    {
        _me = new UnitSnapshot(
            WoWGuid.Zero,
            _view.HealthPercent,
            _lua.EvaluateDouble("(UnitPower(\"player\") / math.max(UnitPowerMax(\"player\"), 1)) * 100") ?? 100d,
            (uint)(_lua.EvaluateInt("UnitPower(\"player\")") ?? 0),
            _view.Level,
            IsAlive: !_view.IsDead,
            0f,
            _view.Target?.Guid ?? WoWGuid.Zero);

        _target = _view.Target is { } target
            ? new UnitSnapshot(
                target.Guid,
                target.HealthPercent,
                0d,
                0,
                target.Level,
                target.IsAlive,
                target.Distance,
                WoWGuid.Zero)
            : null;

        _pet = ReadPet();

        _group = _party is null
            ? []
            : [.. _party.Members.Select(member => new UnitSnapshot(
                member.Guid,
                member.HealthPercent,
                member.PowerPercent,
                0,
                member.Level,
                member.IsAlive,
                member.Distance,
                member.TargetGuid))];

        _enemiesInMelee = _view.NearbyEnemies.Count(
            enemy => enemy.IsAlive && enemy.Distance <= MeleeRange);
    }

    /// <inheritdoc />
    public bool IsSpellReady(string spellName)
    {
        ArgumentException.ThrowIfNullOrEmpty(spellName);

        // A spell the character does not know is never ready, which is what makes a wrong or
        // misspelled name in a rotation fail safely: the rule is skipped and the next one runs.
        return _lua.EvaluateBool(
            $"(function() local name = \"{Escape(spellName)}\" "
            + "if not GetSpellInfo(name) then return false end "
            + "local start, duration = GetSpellCooldown(name) "
            + "if start and duration and duration > 1.5 and start > 0 then return false end "
            + "local usable, noMana = IsUsableSpell(name) "
            + "return (usable and not noMana) and true or false end)()");
    }

    /// <inheritdoc />
    public bool HasAura(UnitSnapshot unit, string auraName) =>
        HasAuraOn(UnitIdFor(unit), auraName);

    /// <inheritdoc />
    public bool Cast(string spellName, bool onSelf = false) =>
        _caster is not null && _caster.Cast(spellName, onSelf);

    /// <inheritdoc />
    /// <remarks>
    /// Casting at a unit id rather than retargeting. A healer that retargeted to heal and back
    /// again would lose casts, break channels, and on a damage character send the next attack
    /// at whatever it healed.
    /// </remarks>
    public bool CastOn(string spellName, UnitSnapshot unit)
    {
        if (_caster is null)
        {
            return false;
        }

        string id = UnitIdFor(unit);

        if (id == "player")
        {
            return _caster.Cast(spellName, onSelf: true);
        }

        if (id.Length == 0)
        {
            // Nothing addressable to cast at. Refusing is right: casting at the wrong unit is
            // worse than not casting.
            return false;
        }

        return _lua.Execute(
            $"CastSpellByName(\"{Escape(spellName)}\", \"{id}\")");
    }

    /// <summary>
    /// The unit id the client knows a snapshot by.
    /// </summary>
    /// <remarks>
    /// The scripting addresses units by name, not GUID, and there are only a handful of names:
    /// the character, its target, its pet, and party1 to party4. Anything else cannot be
    /// addressed at all, which is why <see cref="CastOn"/> refuses rather than guessing.
    /// </remarks>
    private string UnitIdFor(UnitSnapshot unit)
    {
        if (unit.Guid == _me.Guid)
        {
            return "player";
        }

        if (_target is { } target && unit.Guid == target.Guid)
        {
            return "target";
        }

        if (_pet is { } pet && unit.Guid == pet.Guid)
        {
            return "pet";
        }

        for (int index = 0; index < _group.Count; index++)
        {
            if (_group[index].Guid == unit.Guid)
            {
                return "party" + (index + 1).ToString(CultureInfo.InvariantCulture);
            }
        }

        return string.Empty;
    }

    private bool HasAuraOn(string unitId, string auraName)
    {
        if (unitId.Length == 0)
        {
            return false;
        }

        // Walks the unit's auras rather than using UnitAura's name lookup, because the lookup
        // is exact and a rotation names spells the way a player does.
        return _lua.EvaluateBool(
            $"(function() local wanted = \"{Escape(auraName)}\" "
            + $"for i = 1, 40 do local name = UnitAura(\"{unitId}\", i) "
            + "if not name then break end "
            + "if name == wanted then return true end end "
            + $"for i = 1, 40 do local name = UnitAura(\"{unitId}\", i, \"HARMFUL\") "
            + "if not name then break end "
            + "if name == wanted then return true end end "
            + "return false end)()");
    }

    private UnitSnapshot? ReadPet()
    {
        if (!_lua.EvaluateBool("UnitExists(\"pet\") and true or false"))
        {
            return null;
        }

        double health = _lua.EvaluateDouble(
            "(UnitHealth(\"pet\") / math.max(UnitHealthMax(\"pet\"), 1)) * 100") ?? 0d;

        LuaPartyState.TryParseGuid(_lua.Evaluate("UnitGUID(\"pet\") or \"\"") ?? string.Empty, out WoWGuid guid);
        LuaPartyState.TryParseGuid(
            _lua.Evaluate("UnitGUID(\"pettarget\") or \"\"") ?? string.Empty,
            out WoWGuid petTarget);

        return new UnitSnapshot(
            guid,
            health,
            0d,
            0,
            0,
            IsAlive: !_lua.EvaluateBool("UnitIsDeadOrGhost(\"pet\") and true or false"),
            0f,
            petTarget);
    }

    /// <summary>
    /// Makes a spell name safe to put inside a Lua string.
    /// </summary>
    /// <remarks>
    /// Spell names come from rotations, which users edit. A quote in one would otherwise end
    /// the string and turn the rest into code — the same shape of mistake as an unescaped SQL
    /// value, and worth closing even though the only person it could hurt is the user.
    /// </remarks>
    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
