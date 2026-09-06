using System.Globalization;
using WoWBuddy.Common.Logging;
using WoWBuddy.Common.Scheduling;
using WoWBuddy.Core.Execution;

namespace WoWBuddy.Live;

/// <summary>
/// Getting back into the world from the character-select screen.
/// </summary>
/// <remarks>
/// <para>
/// <b>No credentials are involved, and none are stored.</b> This project holds nothing that
/// could log an account in and does not intend to. What it can do is the half that needs no
/// secret: a dropped connection leaves the client at character select with the last-played
/// character already chosen, and entering the world from there is one call.
/// </para>
/// <para>
/// A client sitting at the login screen instead needs a person. <see cref="CanEnterWorld"/>
/// reports that honestly rather than the bot clicking hopefully at it.
/// </para>
/// <para>
/// <b>Checked at the moment of use, not probed at attach.</b> The calls this needs exist only
/// while the glue screens are loaded, so a probe taken from inside the world would report them
/// missing every single time. That is also why this is not a <c>GameCapability</c>: the honest
/// answer changes depending on which screen the client is showing.
/// </para>
/// <para>
/// <c>// TODO: verify</c> — whether the bot's Lua bridge reaches the glue screens' script
/// environment at all. 3.3.5a runs the login and character-select screens in a separate Lua
/// state from the world, and this project has no record of which one
/// <c>FrameScript_Execute</c> dispatches to. If it is the world's only, every call here finds
/// nothing and the feature reports itself unavailable, which is the safe way to be wrong. To
/// check: disconnect a character and run <c>type(EnterWorld)</c> through the inspector's
/// <c>lua</c> command while the character-select screen is showing.
/// </para>
/// </remarks>
public sealed class LuaWorldEntry : IWorldEntry
{
    private readonly ILuaEvaluator _lua;

    private bool _warned;

    /// <summary>Enters the world through an attached client.</summary>
    public LuaWorldEntry(ILuaEvaluator lua)
    {
        _lua = lua ?? throw new ArgumentNullException(nameof(lua));
    }

    /// <inheritdoc />
    public bool CanEnterWorld
    {
        get
        {
            if (!_lua.CanReadResults)
            {
                return false;
            }

            bool available = Exists("EnterWorld");

            if (!available && !_warned)
            {
                _warned = true;

                Log.For<LuaWorldEntry>().Warning(
                    "The client has no EnterWorld to call. Either it is at the login screen, "
                    + "which needs a person, or the bot's scripting does not reach the glue "
                    + "screens on this build.");
            }

            return available;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The slot is used only when the client offers a way to choose one and the caller asked
    /// for a specific character. Otherwise it enters as whoever is already selected, which is
    /// the character that just fell out of the world — the right answer, and one fewer
    /// unverified call in the path that matters.
    /// </remarks>
    public bool EnterWorld(int slot)
    {
        if (!CanEnterWorld)
        {
            return false;
        }

        if (slot > 0 && Exists("CharacterSelect_SelectCharacter"))
        {
            _lua.Execute(
                $"CharacterSelect_SelectCharacter({slot.ToString(CultureInfo.InvariantCulture)})");
        }
        else if (slot > 0)
        {
            Log.For<LuaWorldEntry>().Warning(
                "This client offers no way to choose character {Slot}, so the bot is entering "
                + "the world as whoever is already selected.", slot);
        }

        return _lua.Execute("EnterWorld()");
    }

    private bool Exists(string function) =>
        string.Equals(_lua.Evaluate($"type({function})"), "function", StringComparison.Ordinal);
}
