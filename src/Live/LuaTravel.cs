using WoWBuddy.BotBases.Support;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Execution;
using WoWBuddy.GameApi.Capabilities;

namespace WoWBuddy.Live;

/// <summary>
/// Getting on and off a mount.
/// </summary>
/// <remarks>
/// <para>
/// <b>The mount is named by the user, because this project has no mount data.</b> Which mounts
/// a character owns is in its own spellbook, but working out which one is fastest, or usable
/// here, would need spell data WoWBuddy does not ship. Naming one is a sentence to type and
/// removes the guesswork entirely.
/// </para>
/// <para>
/// <see cref="CanMount"/> asks the client rather than reasoning about it. Indoors, underwater,
/// in combat, in a battleground before the gates open, on a taxi — there are more ways to be
/// unable to mount than are worth enumerating, and the client already knows all of them.
/// </para>
/// </remarks>
public sealed class LuaTravel : ITravel
{
    private readonly ILuaEvaluator _lua;
    private readonly CapabilityReport _capabilities;
    private readonly Func<bool> _isMounted;
    private readonly string _mountName;

    private bool _warned;

    /// <summary>Builds travel over an attached client.</summary>
    /// <param name="lua">The client's scripting.</param>
    /// <param name="capabilities">What that client turned out to support.</param>
    /// <param name="isMounted">
    /// Whether the character is mounted, read from memory. The unit flag is verified, so it is
    /// a better answer than anything the scripting would give.
    /// </param>
    /// <param name="mountName">The mount to cast, as the client names it.</param>
    public LuaTravel(
        ILuaEvaluator lua,
        CapabilityReport capabilities,
        Func<bool> isMounted,
        string mountName)
    {
        _lua = lua ?? throw new ArgumentNullException(nameof(lua));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _isMounted = isMounted ?? throw new ArgumentNullException(nameof(isMounted));
        _mountName = mountName ?? string.Empty;
    }

    /// <inheritdoc />
    public bool IsMounted => _isMounted();

    /// <inheritdoc />
    public bool CanMount
    {
        get
        {
            if (_mountName.Length == 0 || !Supports() || IsMounted)
            {
                return false;
            }

            // IsUsableSpell covers the lot: indoors, underwater, in combat, on a taxi, and in a
            // battleground before the gates open. Enumerating those here would be a worse copy
            // of something the client is authoritative about.
            return _lua.EvaluateBool(
                $"(function() local usable = IsUsableSpell(\"{Escape(_mountName)}\") "
                + "return usable and true or false end)()");
        }
    }

    /// <inheritdoc />
    public bool Mount()
    {
        if (!CanMount)
        {
            return false;
        }

        Log.For<LuaTravel>().Debug("Mounting on {Mount}", _mountName);

        return _lua.Execute($"CastSpellByName(\"{Escape(_mountName)}\")");
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>Dismount</c> rather than casting the mount again: casting it again while mounted does
    /// dismount, but it also starts a cast the character then interrupts, and on a flying mount
    /// over water that is a swim.
    /// </remarks>
    public bool Dismount()
    {
        if (!Supports() || !IsMounted)
        {
            return false;
        }

        return _lua.Execute("Dismount()");
    }

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private bool Supports()
    {
        if (_capabilities.Supports(GameCapability.Travel))
        {
            return true;
        }

        if (!_warned)
        {
            _warned = true;
            Log.For<LuaTravel>().Warning(
                "{Explanation}", _capabilities.Explain(GameCapability.Travel));
        }

        return false;
    }
}
