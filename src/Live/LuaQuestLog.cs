using System.Globalization;
using WoWBuddy.BotBases.Questing;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Execution;
using WoWBuddy.GameApi.Capabilities;

namespace WoWBuddy.Live;

/// <summary>
/// The character's quest log, read out of the client's own scripting.
/// </summary>
/// <remarks>
/// <para>
/// <b>One round trip, not fifty.</b> Every Lua evaluation is a hop onto the game thread and
/// back, and reading a full log a field at a time would be five calls per quest, twenty-five
/// quests, on every tick. So the whole log is assembled by a loop running inside the client and
/// comes back as a single delimited string. That is the difference between a bot that stutters
/// and one that does not, and it is why this class looks the way it does.
/// </para>
/// <para>
/// <b>Cached, briefly.</b> The tree asks about the quest log several times per tick and the log
/// changes a few times an hour. A short cache turns that back into one read per tick.
/// </para>
/// <para>
/// <b>Two things here are assumptions, not verified facts.</b> That <c>GetQuestLogTitle</c>
/// returns completion in its seventh slot, and that <c>GetQuestLink</c> yields a hyperlink
/// containing <c>quest:&lt;id&gt;</c>. Both come from the shape of 3.3.5a's API rather than from
/// this project having watched them work, which is why section 3 of
/// <c>docs/phase-7-manual-test.md</c> asks a human to check them. The parser is tolerant to
/// match: a row it cannot read is dropped with a line saying so, rather than becoming a quest
/// with a wrong id, which would have the bot working the wrong objective.
/// </para>
/// <para>
/// Completion is the known gap. See <see cref="IQuestLog.IsCompleted"/>: 3.3.5a has no call for
/// it, so the bot remembers what it hands in and treats a giver with nothing on offer as
/// evidence.
/// </para>
/// </remarks>
public sealed class LuaQuestLog : IQuestLog
{
    /// <summary>
    /// Separates fields within a row.
    /// </summary>
    /// <remarks>
    /// The ASCII unit separator, chosen because quest titles and objective text will not
    /// contain it. A comma or a pipe would eventually meet a quest named after one.
    /// </remarks>
    private const char FieldSeparator = '\u001F';

    /// <summary>Separates rows. The ASCII record separator, for the same reason.</summary>
    private const char RowSeparator = '\u001E';

    /// <summary>How long a reading stays good for.</summary>
    /// <remarks>
    /// Long enough that a tick asking several times pays for one read, short enough that
    /// accepting a quest is visible almost immediately. The bot also clears the cache itself
    /// whenever it changes the log, so this only covers changes made by the player or the
    /// server.
    /// </remarks>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromMilliseconds(750);

    /// <summary>
    /// Assembles the whole log inside the client and returns it as one string.
    /// </summary>
    /// <remarks>
    /// Headers are skipped: the log interleaves category headers with quests, and only a quest
    /// has a link. Objectives are appended to their quest's row, so one read covers everything
    /// the questing base asks about.
    /// </remarks>
    internal const string ReadScript = """
        local rows = {}
        local total = GetNumQuestLogEntries()
        for i = 1, total do
            local title, _, _, _, isHeader, _, isComplete = GetQuestLogTitle(i)
            if not isHeader then
                local link = GetQuestLink(i)
                local id = link and string.match(link, "quest:(%d+)")
                if id then
                    local done = (isComplete and isComplete ~= 0) and 1 or 0
                    local row = id .. "\31" .. (title or "") .. "\31" .. done
                    SelectQuestLogEntry(i)
                    local objectives = GetNumQuestLeaderBoards(i) or 0
                    for j = 1, objectives do
                        local text, _, finished = GetQuestLogLeaderBoard(j, i)
                        row = row .. "\31" .. (text or "") .. "\31" .. (finished and 1 or 0)
                    end
                    rows[#rows + 1] = row
                end
            end
        end
        __wowbuddy_result = table.concat(rows, "\30")
        """;

    private readonly ILuaEvaluator _lua;
    private readonly CapabilityReport _capabilities;
    private readonly HashSet<uint> _completed = [];
    private readonly Func<DateTimeOffset> _clock;

    private IReadOnlyList<QuestLogEntry> _entries = [];
    private DateTimeOffset _readAt = DateTimeOffset.MinValue;
    private bool _warnedAboutCapability;

    /// <summary>Reads a quest log from a client.</summary>
    /// <param name="lua">The attached client's scripting.</param>
    /// <param name="capabilities">What that client turned out to support.</param>
    /// <param name="clock">The current time. Injected so the cache can be tested.</param>
    public LuaQuestLog(
        ILuaEvaluator lua,
        CapabilityReport capabilities,
        Func<DateTimeOffset>? clock = null)
    {
        _lua = lua ?? throw new ArgumentNullException(nameof(lua));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>How many quests a 3.3.5a log holds.</summary>
    public int Capacity => 25;

    /// <inheritdoc />
    public IReadOnlyList<QuestLogEntry> Entries
    {
        get
        {
            Refresh();
            return _entries;
        }
    }

    /// <summary>How many times the log has actually been read from the client.</summary>
    /// <remarks>Exposed because "did the cache work" is worth being able to assert.</remarks>
    public int Reads { get; private set; }

    /// <summary>Rows that came back malformed and were dropped.</summary>
    public int Dropped { get; private set; }

    /// <inheritdoc />
    public bool IsCompleted(uint questId) => _completed.Contains(questId);

    /// <inheritdoc />
    public void MarkCompleted(uint questId)
    {
        if (_completed.Add(questId))
        {
            // The log has necessarily changed, and the next question about it should not be
            // answered from a reading taken before the quest left.
            Invalidate();
        }
    }

    /// <inheritdoc />
    public QuestGiverResult Accept(uint questId)
    {
        if (!Supports(GameCapability.QuestGiver))
        {
            return QuestGiverResult.NotReady;
        }

        // Whether the quest window is open at all. Accepting into a closed window does nothing,
        // and would be indistinguishable from a giver that does not have the quest.
        if (!IsQuestWindowOpen())
        {
            return QuestGiverResult.NotReady;
        }

        _lua.Execute("AcceptQuest()");
        Invalidate();

        // The client does not report whether accepting worked, so the log is the answer: the
        // quest is either in it now, or the giver did not have it.
        return IsInLogNow(questId) ? QuestGiverResult.Done : QuestGiverResult.NotOffered;
    }

    /// <inheritdoc />
    public QuestGiverResult TurnIn(uint questId, int rewardIndex)
    {
        if (!Supports(GameCapability.QuestGiver))
        {
            return QuestGiverResult.NotReady;
        }

        if (!IsInLogNow(questId))
        {
            return QuestGiverResult.NotOffered;
        }

        if (!IsQuestWindowOpen())
        {
            return QuestGiverResult.NotReady;
        }

        // CompleteQuest moves the window on to the rewards; GetQuestReward takes one. A quest
        // with no choice still needs an index, and the client ignores it.
        _lua.Execute("CompleteQuest()");
        _lua.Execute($"GetQuestReward({rewardIndex.ToString(CultureInfo.InvariantCulture)})");

        Invalidate();

        return IsInLogNow(questId) ? QuestGiverResult.NotReady : QuestGiverResult.Done;
    }

    /// <inheritdoc />
    public bool Abandon(uint questId)
    {
        if (!Supports(GameCapability.QuestLog))
        {
            return false;
        }

        int index = IndexOf(questId);

        if (index <= 0)
        {
            return false;
        }

        _lua.Execute(
            $"SelectQuestLogEntry({index.ToString(CultureInfo.InvariantCulture)}) "
            + "SetAbandonQuest() AbandonQuest()");

        Invalidate();
        return !IsInLogNow(questId);
    }

    /// <summary>Forgets the last reading, so the next question re-reads the client.</summary>
    public void Invalidate() => _readAt = DateTimeOffset.MinValue;

    private bool IsQuestWindowOpen() =>
        _lua.EvaluateBool("(QuestFrame ~= nil and QuestFrame:IsVisible()) and true or false");

    private bool IsInLogNow(uint questId)
    {
        Invalidate();
        return this.Find(questId) is not null;
    }

    /// <summary>
    /// The log position of a quest, which is what the abandon calls take.
    /// </summary>
    /// <remarks>
    /// One-based over the whole log including headers, which is what the client's own index
    /// means, so this asks the client rather than counting the parsed quests, where the headers
    /// are gone and the numbering no longer matches.
    /// </remarks>
    private int IndexOf(uint questId)
    {
        string id = questId.ToString(CultureInfo.InvariantCulture);

        string? answer = _lua.Evaluate(
            "(function() for i = 1, GetNumQuestLogEntries() do "
            + "local link = GetQuestLink(i) "
            + $"if link and string.match(link, \"quest:{id}:\") then return i end "
            + "end return 0 end)()");

        return int.TryParse(answer, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
            ? index
            : 0;
    }

    private bool Supports(GameCapability capability)
    {
        if (_capabilities.Supports(capability))
        {
            return true;
        }

        if (!_warnedAboutCapability)
        {
            _warnedAboutCapability = true;
            Log.For<LuaQuestLog>().Warning("{Explanation}", _capabilities.Explain(capability));
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

        if (!Supports(GameCapability.QuestLog) || !Supports(GameCapability.QuestIds))
        {
            _entries = [];
            return;
        }

        _lua.Execute(ReadScript);
        Reads++;

        string? raw = _lua.Evaluate("__wowbuddy_result");

        if (raw is null)
        {
            // The read failed rather than the log being empty. Keeping the previous reading
            // would look like nothing had changed, so the log reads as unknown instead, and the
            // questing base treats an unknown log as one it cannot work from.
            _entries = [];
            return;
        }

        _entries = Parse(raw);
    }

    private IReadOnlyList<QuestLogEntry> Parse(string raw)
    {
        if (raw.Length == 0)
        {
            return [];
        }

        List<QuestLogEntry> entries = [];

        foreach (string row in raw.Split(RowSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = row.Split(FieldSeparator);

            // id, title, complete, then pairs of objective text and whether it is finished.
            if (fields.Length < 3
                || !uint.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint questId)
                || questId == 0)
            {
                Drop(row);
                continue;
            }

            List<QuestObjective> objectives = [];

            for (int index = 3; index + 1 < fields.Length; index += 2)
            {
                objectives.Add(new QuestObjective(
                    fields[index],
                    Have: 0,
                    Need: 0,
                    IsDone: fields[index + 1] == "1"));
            }

            entries.Add(new QuestLogEntry(questId, fields[1], fields[2] == "1", objectives));
        }

        return entries;
    }

    private void Drop(string row)
    {
        Dropped++;

        // Dropping rather than guessing: a quest with a wrong id has the bot working the wrong
        // objective, which is worse than a quest the bot cannot see at all.
        Log.For<LuaQuestLog>().Warning(
            "A quest log row could not be read and was skipped: {Row}", row);
    }
}
