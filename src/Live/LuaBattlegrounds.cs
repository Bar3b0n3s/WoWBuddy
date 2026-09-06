using System.Globalization;
using WoWBuddy.BotBases.Battlegrounds;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Execution;
using WoWBuddy.GameApi.Capabilities;

namespace WoWBuddy.Live;

/// <summary>
/// Queueing for and sitting inside a battleground, through the client's own scripting.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the least verified corner of the bot.</b> Every call here is written from the
/// shape of 3.3.5a's API rather than from this project having watched it work, which is exactly
/// what section 7 of <c>docs/phase-8-manual-test.md</c> exists to check. The interface it
/// implements was written as intentions for that reason, so getting the details right later
/// changes this file and nothing else.
/// </para>
/// <para>
/// Two of the assumptions are worth naming. Queueing is a two-step dance — ask the server about
/// a battleground's instances, then join a slot — and the two calls take different indices: the
/// first takes the battleground's place in the list, the second takes an instance number where
/// zero means "first available". And the gates are detected through
/// <c>GetBattlefieldInstanceRunTime</c>, which reports how long the match has been going;
/// anything above zero means it has started.
/// </para>
/// <para>
/// <b>When the started signal is unavailable, the bot assumes the match has begun.</b> That is
/// the safer of the two mistakes: a bot that wrongly thinks the gates are shut waits behind
/// them until it is thrown out for being idle, while one that wrongly thinks they are open
/// walks into them and achieves nothing worse than looking silly for thirty seconds.
/// </para>
/// </remarks>
public sealed class LuaBattlegrounds : IBattlegroundActions
{
    /// <summary>Separates fields in the status reading.</summary>
    private const char FieldSeparator = '\u001F';

    /// <summary>
    /// Which battlefield slot to look at.
    /// </summary>
    /// <remarks>
    /// A character can be queued for more than one at a time; the bot only ever queues for one,
    /// so the first slot is the one it queued into. One-based, as the client counts.
    /// </remarks>
    public const int Slot = 1;

    /// <summary>How long a reading stays good for.</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromMilliseconds(500);

    /// <summary>Reads the whole battlefield state in one go.</summary>
    internal const string ReadScript = """
        local status, mapName = GetBattlefieldStatus(1)
        local runTime = GetBattlefieldInstanceRunTime and GetBattlefieldInstanceRunTime() or 0
        local winner = GetBattlefieldWinner and GetBattlefieldWinner() or nil
        __wowbuddy_result = (status or "none") .. "\31" .. (mapName or "")
            .. "\31" .. tostring(runTime or 0)
            .. "\31" .. (winner ~= nil and 1 or 0)
        """;

    private readonly ILuaEvaluator _lua;
    private readonly CapabilityReport _capabilities;
    private readonly Func<DateTimeOffset> _clock;

    private BattlegroundStatus _status = BattlegroundStatus.None;
    private string _name = string.Empty;
    private long _runTime;
    private bool _finished;
    private DateTimeOffset _readAt = DateTimeOffset.MinValue;
    private bool _warned;

    /// <summary>Reads the battlefield state from a client.</summary>
    public LuaBattlegrounds(
        ILuaEvaluator lua,
        CapabilityReport capabilities,
        Func<DateTimeOffset>? clock = null)
    {
        _lua = lua ?? throw new ArgumentNullException(nameof(lua));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public BattlegroundStatus Status
    {
        get
        {
            Refresh();
            return _status;
        }
    }

    /// <inheritdoc />
    public bool IsInside => Status == BattlegroundStatus.Active;

    /// <inheritdoc />
    /// <remarks>
    /// Unknown counts as started. See the class remarks: waiting behind gates that are already
    /// open ends with the character thrown out for standing still.
    /// </remarks>
    public bool HasStarted
    {
        get
        {
            Refresh();
            return _status == BattlegroundStatus.Active && (_runTime > 0 || !HasRunTime);
        }
    }

    /// <inheritdoc />
    public bool IsFinished
    {
        get
        {
            Refresh();
            return _finished;
        }
    }

    /// <inheritdoc />
    public string Name
    {
        get
        {
            Refresh();
            return _name;
        }
    }

    /// <summary>How many times the state has actually been read from the client.</summary>
    public int Reads { get; private set; }

    /// <summary>True when the client can report how long the match has been running.</summary>
    private bool HasRunTime => _capabilities.Supports(GameCapability.Battlegrounds);

    /// <inheritdoc />
    public bool Queue(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!Supports())
        {
            return false;
        }

        int index = IndexOf(name);

        if (index <= 0)
        {
            Log.For<LuaBattlegrounds>().Warning(
                "This client offers no battleground called '{Name}'. The name has to match what "
                + "the client calls it, in the client's own language.", name);
            return false;
        }

        // Two calls, two different indices, and they are not interchangeable: the first takes
        // the battleground's place in the list, the second an instance number where zero means
        // whichever is available.
        string position = index.ToString(CultureInfo.InvariantCulture);

        _lua.Execute($"RequestBattlegroundInstanceInfo({position})");
        _lua.Execute("JoinBattlefield(0)");

        Invalidate();
        return true;
    }

    /// <inheritdoc />
    public bool AcceptInvitation()
    {
        if (!Supports() || Status != BattlegroundStatus.Confirmed)
        {
            return false;
        }

        // The second argument is whether to accept: one to go in, zero to decline.
        _lua.Execute($"AcceptBattlefieldPort({Slot.ToString(CultureInfo.InvariantCulture)}, 1)");

        Invalidate();
        return true;
    }

    /// <inheritdoc />
    public bool Leave()
    {
        if (!Supports())
        {
            return false;
        }

        _lua.Execute("LeaveBattlefield()");

        Invalidate();
        return true;
    }

    /// <summary>Forgets the last reading.</summary>
    public void Invalidate() => _readAt = DateTimeOffset.MinValue;

    /// <summary>
    /// Where a battleground sits in the client's own list.
    /// </summary>
    /// <remarks>
    /// Matched by the name the client shows, because that is the only handle the scripting
    /// gives. It means a client running in another language needs the name in that language,
    /// which is worth saying out loud rather than leaving as a puzzle.
    /// </remarks>
    private int IndexOf(string name)
    {
        string escaped = name.Replace("\"", "\\\"", StringComparison.Ordinal);

        string? answer = _lua.Evaluate(
            "(function() for i = 1, GetNumBattlegroundTypes() do "
            + "local localised = GetBattlegroundInfo(i) "
            + $"if localised and string.lower(localised) == string.lower(\"{escaped}\") then return i end "
            + "end return 0 end)()");

        return int.TryParse(answer, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
            ? index
            : 0;
    }

    private bool Supports()
    {
        if (_capabilities.Supports(GameCapability.Battlegrounds))
        {
            return true;
        }

        if (!_warned)
        {
            _warned = true;
            Log.For<LuaBattlegrounds>().Warning(
                "{Explanation}", _capabilities.Explain(GameCapability.Battlegrounds));
        }

        return false;
    }

    private void Refresh()
    {
        DateTimeOffset now = _clock();

        if (now - _readAt < CacheLifetime)
        {
            return;
        }

        _readAt = now;

        if (!Supports())
        {
            Reset();
            return;
        }

        _lua.Execute(ReadScript);
        Reads++;

        string? raw = _lua.Evaluate("__wowbuddy_result");

        if (raw is null)
        {
            Reset();
            return;
        }

        string[] fields = raw.Split(FieldSeparator);

        if (fields.Length < 4)
        {
            Log.For<LuaBattlegrounds>().Warning(
                "The battlefield status could not be read: {Raw}", raw);
            Reset();
            return;
        }

        _status = ParseStatus(fields[0]);
        _name = fields[1];
        _runTime = long.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out long runTime)
            ? runTime
            : 0;
        _finished = fields[3] == "1";
    }

    private void Reset()
    {
        _status = BattlegroundStatus.None;
        _name = string.Empty;
        _runTime = 0;
        _finished = false;
    }

    /// <summary>
    /// Turns what the client says into what the bot means.
    /// </summary>
    /// <remarks>
    /// The four strings 3.3.5a uses. Anything else is treated as not queued, which is the
    /// conservative answer: a bot that thinks it is not in a queue will try to join one, and
    /// the client will refuse if it already is.
    /// </remarks>
    internal static BattlegroundStatus ParseStatus(string status) => status.ToUpperInvariant() switch
    {
        "QUEUED" => BattlegroundStatus.Queued,
        "CONFIRM" => BattlegroundStatus.Confirmed,
        "ACTIVE" => BattlegroundStatus.Active,
        _ => BattlegroundStatus.None,
    };
}
