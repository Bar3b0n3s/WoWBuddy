using WoWBuddy.Common.Logging;
using WoWBuddy.Common.Scheduling;
using WoWBuddy.Core.Execution;
using WoWBuddy.GameApi.Capabilities;

namespace WoWBuddy.Live;

/// <summary>
/// Whispers, collected by the client and drained by the bot.
/// </summary>
/// <remarks>
/// <para>
/// The one reader here that cannot poll for its answer. A whisper is an event: by the time the
/// bot next asks, the message has been and gone from anywhere it could be read. So a small
/// frame is created inside the client once, it listens for the event, and it appends what it
/// hears to a global that the bot empties on its next round trip.
/// </para>
/// <para>
/// That frame is the only lasting change this project makes inside the client's Lua, and it is
/// worth being plain about: it is a hidden frame with one event registered and a four-line
/// handler, it writes to one global, and it does nothing else. Reloading the interface removes
/// it, which the setup script notices and repairs.
/// </para>
/// <para>
/// <b>Nothing is ever sent.</b> This reads; it has no way to say anything back, deliberately.
/// </para>
/// </remarks>
public sealed class LuaWhispers : IWhisperSource
{
    /// <summary>Separates fields within a row.</summary>
    private const char FieldSeparator = '\u001F';

    /// <summary>Separates rows.</summary>
    private const char RowSeparator = '\u001E';

    /// <summary>The global the client's handler appends to.</summary>
    internal const string Buffer = "__wowbuddy_whispers";

    /// <summary>The frame's name, so the setup script can tell whether it already exists.</summary>
    internal const string FrameName = "WoWBuddyWhisperListener";

    /// <summary>
    /// Creates the listening frame, if it is not already there.
    /// </summary>
    /// <remarks>
    /// Idempotent, and run on every drain rather than once at attach: a UI reload destroys the
    /// frame, and a bot that set it up once would then hear nothing for the rest of the night
    /// while believing it was listening.
    /// </remarks>
    internal const string SetupScript = """
        if not WoWBuddyWhisperListener then
            local f = CreateFrame("Frame", "WoWBuddyWhisperListener")
            __wowbuddy_whispers = ""
            f:RegisterEvent("CHAT_MSG_WHISPER")
            f:SetScript("OnEvent", function(self, event, text, sender)
                __wowbuddy_whispers = (__wowbuddy_whispers or "")
                    .. (sender or "?") .. "\31" .. (text or "") .. "\30"
            end)
        end
        """;

    /// <summary>Takes what has arrived and empties the buffer in the same round trip.</summary>
    /// <remarks>
    /// Read and clear together, because anything read and not cleared is acted on twice and
    /// anything cleared and not read is lost. Doing both in one script leaves no gap for a
    /// whisper to arrive in between.
    /// </remarks>
    internal const string DrainScript = """
        __wowbuddy_result = __wowbuddy_whispers or ""
        __wowbuddy_whispers = ""
        """;

    private readonly ILuaEvaluator _lua;
    private readonly CapabilityReport _capabilities;
    private readonly Func<DateTimeOffset> _clock;

    private bool _warned;

    /// <summary>Listens to an attached client.</summary>
    public LuaWhispers(
        ILuaEvaluator lua,
        CapabilityReport capabilities,
        Func<DateTimeOffset>? clock = null)
    {
        _lua = lua ?? throw new ArgumentNullException(nameof(lua));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>How many times the buffer has been drained.</summary>
    public int Drains { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<Whisper> Drain()
    {
        if (!Supports())
        {
            return [];
        }

        _lua.Execute(SetupScript);
        _lua.Execute(DrainScript);
        Drains++;

        if (_lua.Evaluate("__wowbuddy_result") is not { Length: > 0 } raw)
        {
            return [];
        }

        DateTimeOffset now = _clock();
        List<Whisper> whispers = [];

        foreach (string row in raw.Split(RowSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = row.Split(FieldSeparator);

            if (fields.Length < 2)
            {
                // Reported rather than dropped silently: a whisper the bot failed to read is
                // one it also failed to stop for, which is the whole point of this class.
                Log.For<LuaWhispers>().Warning(
                    "A whisper could not be read and was skipped");
                continue;
            }

            whispers.Add(new Whisper(fields[0], fields[1], now));
        }

        return whispers;
    }

    private bool Supports()
    {
        if (_capabilities.Supports(GameCapability.Whispers))
        {
            return true;
        }

        if (!_warned)
        {
            _warned = true;
            Log.For<LuaWhispers>().Warning(
                "{Explanation}", _capabilities.Explain(GameCapability.Whispers));
        }

        return false;
    }
}
