using System.Globalization;
using WoWBuddy.BotBases.Support;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Execution;
using WoWBuddy.GameApi.Capabilities;

namespace WoWBuddy.Live;

/// <summary>
/// Spending talent points through the client.
/// </summary>
/// <remarks>
/// <para>
/// Two calls and no cleverness. What a talent is called, what it does, and whether it is a
/// sensible thing to take are all outside what this project knows — a build comes from the user
/// and the client decides whether each pick is legal.
/// </para>
/// <para>
/// <b>Nothing here can undo a point.</b> That is deliberate: unlearning talents costs gold and
/// is not a decision a bot should be making at three in the morning.
/// </para>
/// </remarks>
public sealed class LuaTalents : ITalents
{
    private readonly ILuaEvaluator _lua;
    private readonly CapabilityReport _capabilities;

    private bool _warned;

    /// <summary>Builds a talent trainer over an attached client.</summary>
    public LuaTalents(ILuaEvaluator lua, CapabilityReport capabilities)
    {
        _lua = lua ?? throw new ArgumentNullException(nameof(lua));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    /// <inheritdoc />
    public int UnspentPoints =>
        Supports() ? _lua.EvaluateInt("GetUnspentTalentPoints()") ?? 0 : 0;

    /// <inheritdoc />
    /// <remarks>
    /// The client reports nothing useful about whether a point went in, so the rank is read
    /// before and after. A rank that did not move means the pick was refused — almost always a
    /// prerequisite the build did not account for.
    /// </remarks>
    public bool Learn(TalentPick pick)
    {
        if (!Supports() || !pick.IsPlausible)
        {
            return false;
        }

        string tab = pick.Tab.ToString(CultureInfo.InvariantCulture);
        string index = pick.Index.ToString(CultureInfo.InvariantCulture);

        int? before = Rank(tab, index);

        if (before is null)
        {
            Log.For<LuaTalents>().Warning(
                "This character has no talent {Pick}. The build is for a different class or "
                + "specialisation.", pick);

            return false;
        }

        _lua.Execute($"LearnTalent({tab}, {index})");

        return Rank(tab, index) > before;
    }

    private int? Rank(string tab, string index) =>
        _lua.EvaluateInt($"select(5, GetTalentInfo({tab}, {index}))");

    private bool Supports()
    {
        if (_capabilities.Supports(GameCapability.Talents))
        {
            return true;
        }

        if (!_warned)
        {
            _warned = true;
            Log.For<LuaTalents>().Warning(
                "{Explanation}", _capabilities.Explain(GameCapability.Talents));
        }

        return false;
    }
}
