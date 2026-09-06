using System.Globalization;
using WoWBuddy.BotBases.Group;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Execution;
using WoWBuddy.Core.Objects;
using WoWBuddy.GameApi.Capabilities;

namespace WoWBuddy.Live;

/// <summary>
/// The character's group, read from the client and located in memory.
/// </summary>
/// <remarks>
/// <para>
/// A hybrid, and it has to be. Who is in the group, what they are called, how much health they
/// have and what they are targeting all come from Lua, because this project has verified no
/// offsets for any of it. Where they are standing comes from the object manager, because Lua
/// has no call that gives a position.
/// </para>
/// <para>
/// <b>An unknown position must never read as a near one.</b> A group member outside the
/// client's visible range is not in the object manager at all, so their distance is unknown —
/// and it is reported as infinite rather than as zero. That single choice is what stops a healer
/// deciding someone three zones away is in range, and a tank deciding the group is together
/// when it cannot see half of it. Unknown means far, always.
/// </para>
/// <para>
/// One round trip per refresh, for the same reason as the quest log: five calls per member per
/// tick is a stutter.
/// </para>
/// </remarks>
public sealed class LuaPartyState : IPartyState
{
    /// <summary>Separates fields within a row.</summary>
    private const char FieldSeparator = '\u001F';

    /// <summary>Separates rows.</summary>
    private const char RowSeparator = '\u001E';

    /// <summary>How long a reading stays good for.</summary>
    /// <remarks>
    /// Shorter than the quest log's, because a group moves. Health and position change second
    /// by second and a healer working from a half-second-old reading heals the wrong person.
    /// </remarks>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Collects the whole group inside the client and returns it as one string.
    /// </summary>
    /// <remarks>
    /// 3.3.5a counts the party as the members <em>other than</em> the character, which is the
    /// opposite of what later versions do; the loop runs from one to that count and the
    /// character itself is deliberately not in the list. Raid groups use a different set of
    /// unit ids and are not handled: the dungeon base is written for five-mans.
    /// </remarks>
    internal const string ReadScript = """
        local rows = {}
        for i = 1, GetNumPartyMembers() do
            local unit = "party" .. i
            local guid = UnitGUID(unit)
            if guid then
                local health, healthMax = UnitHealth(unit), UnitHealthMax(unit)
                local percent = 0
                if healthMax and healthMax > 0 then percent = (health / healthMax) * 100 end
                local target = UnitGUID(unit .. "target") or "0x0000000000000000"
                rows[#rows + 1] = unit .. "\31" .. guid .. "\31" .. (UnitName(unit) or "")
                    .. "\31" .. string.format("%.1f", percent)
                    .. "\31" .. (UnitIsDeadOrGhost(unit) and 0 or 1)
                    .. "\31" .. (UnitIsConnected(unit) and 1 or 0)
                    .. "\31" .. (UnitIsPartyLeader(unit) and 1 or 0)
                    .. "\31" .. (UnitAffectingCombat(unit) and 1 or 0)
                    .. "\31" .. target
            end
        end
        __wowbuddy_result = table.concat(rows, "\30")
        """;

    private readonly ILuaEvaluator _lua;
    private readonly CapabilityReport _capabilities;
    private readonly Func<Vector3> _myPosition;
    private readonly Func<WoWGuid, Vector3?> _locate;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<ulong, string> _unitIds = [];

    private IReadOnlyList<PartyMember> _members = [];
    private DateTimeOffset _readAt = DateTimeOffset.MinValue;
    private bool _warned;

    /// <summary>Reads a group from a client.</summary>
    /// <param name="lua">The attached client's scripting.</param>
    /// <param name="capabilities">What that client turned out to support.</param>
    /// <param name="myPosition">Where the character is, for working out distances.</param>
    /// <param name="locate">
    /// Where a GUID is, or null when the client cannot see it. Injected rather than taking the
    /// object manager directly so the whole class can be exercised without a game.
    /// </param>
    /// <param name="clock">The current time, so the cache can be tested.</param>
    public LuaPartyState(
        ILuaEvaluator lua,
        CapabilityReport capabilities,
        Func<Vector3> myPosition,
        Func<WoWGuid, Vector3?> locate,
        Func<DateTimeOffset>? clock = null)
    {
        _lua = lua ?? throw new ArgumentNullException(nameof(lua));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _myPosition = myPosition ?? throw new ArgumentNullException(nameof(myPosition));
        _locate = locate ?? throw new ArgumentNullException(nameof(locate));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public IReadOnlyList<PartyMember> Members
    {
        get
        {
            Refresh();
            return _members;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A setting, not a reading. See <see cref="PartyRole"/>: the client may know, but this
    /// project has not verified how to ask, and a bot wrong about this tanks in cloth.
    /// </remarks>
    public PartyRole MyRole { get; set; } = PartyRole.None;

    /// <inheritdoc />
    public bool IsInInstance =>
        Supports(GameCapability.Party)
        && _lua.EvaluateBool("IsInInstance() and true or false");

    /// <summary>How many times the group has actually been read from the client.</summary>
    public int Reads { get; private set; }

    /// <summary>Rows that came back malformed and were dropped.</summary>
    public int Dropped { get; private set; }

    /// <inheritdoc />
    public bool Follow(WoWGuid guid)
    {
        if (!Supports(GameCapability.Party))
        {
            return false;
        }

        // FollowUnit takes a unit id, not a GUID, so the mapping from the last reading is what
        // makes this possible at all. A GUID the bot has not seen in the group cannot be
        // followed, which is right: it is not in the group.
        Refresh();

        if (!_unitIds.TryGetValue(guid.Value, out string? unit))
        {
            return false;
        }

        return _lua.Execute($"FollowUnit(\"{unit}\")");
    }

    /// <summary>Forgets the last reading.</summary>
    public void Invalidate() => _readAt = DateTimeOffset.MinValue;

    private bool Supports(GameCapability capability)
    {
        if (_capabilities.Supports(capability))
        {
            return true;
        }

        if (!_warned)
        {
            _warned = true;
            Log.For<LuaPartyState>().Warning("{Explanation}", _capabilities.Explain(capability));
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

        if (!Supports(GameCapability.Party))
        {
            _members = [];
            _unitIds.Clear();
            return;
        }

        _lua.Execute(ReadScript);
        Reads++;

        string? raw = _lua.Evaluate("__wowbuddy_result");

        if (raw is null)
        {
            // Unknown rather than empty. Reporting an empty group would have the dungeon base
            // decide it is playing alone and wander off.
            _members = [];
            _unitIds.Clear();
            return;
        }

        _members = Parse(raw);
    }

    private IReadOnlyList<PartyMember> Parse(string raw)
    {
        _unitIds.Clear();

        if (raw.Length == 0)
        {
            return [];
        }

        Vector3 me = _myPosition();
        List<PartyMember> members = [];

        foreach (string row in raw.Split(RowSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = row.Split(FieldSeparator);

            if (fields.Length < 9 || !TryParseGuid(fields[1], out WoWGuid guid))
            {
                Dropped++;
                Log.For<LuaPartyState>().Warning(
                    "A party row could not be read and was skipped: {Row}", row);
                continue;
            }

            Vector3? position = _locate(guid);

            // Unknown means far. A member the client cannot see is not somewhere near zero.
            float distance = position is { } known ? me.Distance(known) : float.PositiveInfinity;

            TryParseGuid(fields[8], out WoWGuid targetGuid);

            members.Add(new PartyMember(
                guid,
                fields[2],
                position ?? Vector3.Zero,
                distance,
                Level: 0,
                HealthPercent: ParsePercent(fields[3]),
                PowerPercent: 0d,
                IsAlive: fields[4] == "1",
                IsOnline: fields[5] == "1",
                IsLeader: fields[6] == "1",
                IsInCombat: fields[7] == "1",
                targetGuid,
                PartyRole.None));

            _unitIds[guid.Value] = fields[0];
        }

        return [.. members.OrderBy(member => member.Distance)];
    }

    private static double ParsePercent(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? Math.Clamp(value, 0d, 100d)
            : 0d;

    /// <summary>
    /// Reads a GUID as the client writes it.
    /// </summary>
    /// <remarks>
    /// <c>UnitGUID</c> returns a string like <c>0x0000000000000001</c>. An unparseable one
    /// becomes zero rather than throwing: a member whose GUID could not be read is dropped by
    /// the caller, which is better than losing the whole group to one bad row.
    /// </remarks>
    internal static bool TryParseGuid(string text, out WoWGuid guid)
    {
        guid = WoWGuid.Zero;

        string trimmed = text.Trim();

        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..];
        }

        if (trimmed.Length == 0
            || !ulong.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong value))
        {
            return false;
        }

        guid = new WoWGuid(value);
        return value != 0UL;
    }
}
