using System.Globalization;
using WoWBuddy.Common.Logging;

namespace WoWBuddy.Core.Execution;

/// <summary>What a spell's current state allows.</summary>
/// <param name="IsKnown">Whether the character has the spell at all.</param>
/// <param name="IsUsable">Whether it could be cast right now, resources aside.</param>
/// <param name="CooldownRemaining">Seconds left on its cooldown, zero when ready.</param>
public readonly record struct SpellState(bool IsKnown, bool IsUsable, double CooldownRemaining)
{
    /// <summary>True when the spell is known, usable and off cooldown.</summary>
    public bool IsReady => IsKnown && IsUsable && CooldownRemaining <= 0d;
}

/// <summary>
/// Casts spells in the client.
/// </summary>
/// <remarks>
/// <para>
/// <b>On protected functions.</b> The client refuses to let <em>addon</em> code call
/// <c>CastSpellByName</c>: it tracks taint through the Lua VM, and code belonging to an addon
/// is tainted. A script handed straight to the client's own script entry point from the render
/// loop has no addon associated with it and so carries no taint, which is why bots for this
/// client cast this way and why the project brief calls it "the known workaround used by
/// existing 12340 bots".
/// </para>
/// <para>
/// That is an empirical property of one build, not a law, so it is <b>tested rather than
/// assumed</b>. <see cref="SelfTest"/> asks the client directly whether the bot's execution
/// context is secure, and casting stays disabled until it says yes.
/// </para>
/// <para>
/// A native cast function would avoid the question entirely and is the better long-term
/// answer, but the two published addresses for it disagree and neither has been confirmed
/// against a client. Guessing at one would mean calling an unknown function on the game
/// thread, which is a much worse failure than a refused cast.
/// </para>
/// </remarks>
public sealed class SpellCaster
{
    private readonly LuaBridge _lua;

    public SpellCaster(LuaBridge lua)
    {
        _lua = lua ?? throw new ArgumentNullException(nameof(lua));
    }

    /// <summary>True once the client has confirmed that casting will be accepted.</summary>
    public bool CanCast { get; private set; }

    /// <summary>Why casting is unavailable, or an empty string.</summary>
    public string UnavailableReason { get; private set; } = "The casting self-test has not run yet.";

    /// <summary>
    /// Asks the client whether the bot's scripts run in a secure context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client exposes <c>issecure()</c>, which reports whether the currently executing
    /// script is trusted to call protected functions. Asking it is a direct, side-effect-free
    /// answer to exactly the question that matters, and it is far better than the alternative
    /// of casting something and watching what happens.
    /// </para>
    /// <para>
    /// Where <c>issecure</c> is missing — a heavily modified client, say — the answer is
    /// unknown rather than negative, and that is reported as such: casting is left disabled,
    /// but the message says the check could not run rather than that casting is impossible.
    /// </para>
    /// </remarks>
    public bool SelfTest(out string detail)
    {
        if (!_lua.CanReadResults)
        {
            CanCast = false;
            UnavailableReason =
                "Values cannot be read back from Lua, so whether casting would be accepted " +
                "cannot be determined. " + _lua.ResultsUnavailableReason;
            detail = UnavailableReason;
            return false;
        }

        if (_lua.Evaluate("type(CastSpellByName)") != "function")
        {
            CanCast = false;
            UnavailableReason =
                "The client does not expose CastSpellByName, so this is not a client the bot " +
                "knows how to cast in.";
            detail = UnavailableReason;
            return false;
        }

        string? hasIsSecure = _lua.Evaluate("type(issecure)");
        if (hasIsSecure != "function")
        {
            CanCast = false;
            UnavailableReason =
                "The client does not expose issecure(), so whether the bot's scripts are " +
                "trusted to call protected functions could not be checked. Casting is left " +
                "disabled rather than attempted blindly.";
            detail = UnavailableReason;
            return false;
        }

        if (!_lua.EvaluateBool("issecure()"))
        {
            CanCast = false;
            UnavailableReason =
                "The client reports that the bot's scripts run in a tainted context, so " +
                "protected functions such as CastSpellByName will be refused. Casting through " +
                "Lua is not possible on this client; a verified native cast address would be needed.";
            detail = UnavailableReason;
            return false;
        }

        CanCast = true;
        UnavailableReason = string.Empty;
        detail = "The client reports the bot's scripts run in a secure context, so protected " +
                 "functions will be accepted.";
        return true;
    }

    /// <summary>
    /// Casts a spell by name.
    /// </summary>
    /// <param name="spellName">The spell's name exactly as the client knows it.</param>
    /// <param name="onSelf">Whether to cast it on the character rather than its target.</param>
    public bool Cast(string spellName, bool onSelf = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spellName);

        if (!CanCast)
        {
            Log.For<SpellCaster>().Warning(
                "Refusing to cast {Spell}: {Reason}", spellName, UnavailableReason);
            return false;
        }

        // A quote in a spell name would end the Lua string early and change what runs. No
        // real 3.3.5a spell contains one, which is exactly why an unexpected one means the
        // name did not come from where the caller thought.
        if (spellName.Contains('"', StringComparison.Ordinal)
            || spellName.Contains('\\', StringComparison.Ordinal)
            || spellName.Contains('\n', StringComparison.Ordinal))
        {
            Log.For<SpellCaster>().Error(
                "Refusing to cast a spell whose name contains a quote, backslash or newline: {Spell}",
                spellName);
            return false;
        }

        string script = onSelf
            ? $"CastSpellByName(\"{spellName}\", true);"
            : $"CastSpellByName(\"{spellName}\");";

        return _lua.Execute(script);
    }

    /// <summary>Stops whatever is currently being cast.</summary>
    public bool StopCasting() => CanCast && _lua.Execute("SpellStopCasting();");

    /// <summary>
    /// Reads what the client says about a spell right now.
    /// </summary>
    /// <remarks>
    /// One round trip rather than three, because each one costs a frame and a rotation asks
    /// about several spells per tick. The three values are packed into a delimited string and
    /// split here.
    /// </remarks>
    public SpellState GetSpellState(string spellName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spellName);

        if (!_lua.CanReadResults || spellName.Contains('"', StringComparison.Ordinal))
        {
            return default;
        }

        // GetSpellCooldown returns start, duration, enabled. A duration of zero means ready;
        // otherwise the remaining time is start + duration - now.
        string expression =
            $"(function() " +
            $"local known = GetSpellInfo(\"{spellName}\") ~= nil; " +
            $"local usable, noMana = IsUsableSpell(\"{spellName}\"); " +
            $"local start, duration = GetSpellCooldown(\"{spellName}\"); " +
            $"local remaining = 0; " +
            $"if start and duration and duration > 0 then remaining = start + duration - GetTime(); end; " +
            $"if remaining < 0 then remaining = 0; end; " +
            $"return tostring(known) .. \"|\" .. tostring(usable == 1 or usable == true) .. \"|\" .. tostring(remaining); " +
            $"end)()";

        string? result = _lua.Evaluate(expression);
        if (result is null)
        {
            return default;
        }

        string[] parts = result.Split('|');
        if (parts.Length != 3)
        {
            return default;
        }

        bool known = string.Equals(parts[0], "true", StringComparison.OrdinalIgnoreCase);
        bool usable = string.Equals(parts[1], "true", StringComparison.OrdinalIgnoreCase);
        double remaining = double.TryParse(
            parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : 0d;

        return new SpellState(known, usable, remaining);
    }

    /// <summary>True when the character is part way through a cast or channel.</summary>
    /// <remarks>
    /// Rotations need this constantly: starting a second cast during the first cancels it,
    /// which turns a working rotation into a character that stands still doing nothing.
    /// </remarks>
    public bool IsCasting() =>
        _lua.CanReadResults
        && _lua.EvaluateBool("tostring(UnitCastingInfo(\"player\") ~= nil or UnitChannelInfo(\"player\") ~= nil)");
}
