namespace WoWBuddy.BotBases.Questing;

/// <summary>What happened when the bot tried to take or hand in a quest.</summary>
/// <remarks>
/// Three answers rather than a bool because the difference between "the window is not open
/// yet" and "this giver does not have that quest" decides whether the bot waits or gives up,
/// and getting it wrong either way is bad: treating a slow window as a refusal makes the bot
/// abandon quests it could have taken, and treating a refusal as slowness parks it in front of
/// an NPC for the rest of the session.
/// </remarks>
public enum QuestGiverResult
{
    /// <summary>The quest window is not open yet. Keep interacting.</summary>
    NotReady,

    /// <summary>The quest was taken or handed in.</summary>
    Done,

    /// <summary>The giver is talking, but does not have that quest.</summary>
    NotOffered,
}

/// <summary>How far along one of a quest's objectives is.</summary>
/// <param name="Text">What the quest log says, for logs and for matching by hand.</param>
/// <param name="Have">How many are done.</param>
/// <param name="Need">How many are needed.</param>
/// <param name="IsDone">Whether the client says this objective is finished.</param>
public readonly record struct QuestObjective(string Text, int Have, int Need, bool IsDone);

/// <summary>One quest in the character's log.</summary>
/// <param name="QuestId">Its id.</param>
/// <param name="Title">Its name.</param>
/// <param name="IsComplete">True when every objective is done and it can be handed in.</param>
/// <param name="Objectives">Its objectives, in the order the log lists them.</param>
public sealed record QuestLogEntry(
    uint QuestId,
    string Title,
    bool IsComplete,
    IReadOnlyList<QuestObjective> Objectives)
{
    /// <summary>The objective at a 1-based index, or null when there is none.</summary>
    public QuestObjective? Objective(int index) =>
        index >= 1 && index <= Objectives.Count ? Objectives[index - 1] : null;
}

/// <summary>
/// The character's quest log, and the verbs for changing it.
/// </summary>
/// <remarks>
/// <para>
/// Plain values and simple verbs, like the rest of <see cref="IBotState"/>, so that a questing
/// profile's whole decision structure can be tested by describing a quest log rather than by
/// playing to the situation in a live client.
/// </para>
/// <para>
/// <b>How a live implementation reads this, and where it cannot.</b> The log itself comes from
/// Lua: <c>GetNumQuestLogEntries</c>, <c>GetQuestLogTitle</c> and <c>GetQuestLogLeaderBoard</c>
/// give titles, completion and per-objective progress. On 3.3.5a <c>GetQuestLogTitle</c> does
/// not return a quest id, so the id has to come from <c>GetQuestLink</c>, whose hyperlink
/// contains <c>quest:&lt;id&gt;:&lt;level&gt;</c>.
/// </para>
/// <para>
/// <b><see cref="IsCompleted"/> is the hard one, and this is not a solved problem here.</b>
/// <c>IsQuestFlaggedCompleted</c> does not exist in 3.3.5a — it arrived in 4.0 — so there is no
/// Lua call that answers "has this character ever handed this quest in". Two ways round it:
/// </para>
/// <list type="number">
/// <item>
/// Remember what the bot itself hands in, per character, and treat everything else as not
/// completed. Cheap and correct for quests the bot did; wrong for a character with history,
/// where the bot will walk to a giver and find nothing on offer. <see cref="MarkCompleted"/>
/// exists so the questing base can record that and move on the moment it happens, which makes
/// the wrong answer self-correcting rather than a loop.
/// </item>
/// <item>
/// Read the client's completed-quest bitmask directly. The 3.3.5a client keeps one, filled in
/// response to the server's quest-query, and reading it would answer the question properly.
/// <b>TODO: verify.</b> No offset for it is recorded in <c>Offsets335a.cs</c>, and none is
/// guessed at here. Finding it means pattern-scanning around the handler for
/// <c>SMSG_QUEST_QUERY_RESPONSE</c>, or watching which memory changes when a quest is handed
/// in on a test server.
/// </item>
/// </list>
/// <para>
/// Until the second exists, the first is what the bot does, and the questing base is written so
/// that being wrong about it costs one walk to a quest giver rather than a stuck session.
/// </para>
/// </remarks>
public interface IQuestLog
{
    /// <summary>Quests currently in the log.</summary>
    IReadOnlyList<QuestLogEntry> Entries { get; }

    /// <summary>How many quests the log can hold. 25 on 3.3.5a.</summary>
    int Capacity { get; }

    /// <summary>True when the character has handed this quest in, as far as the bot knows.</summary>
    bool IsCompleted(uint questId);

    /// <summary>
    /// Records that a quest is completed.
    /// </summary>
    /// <remarks>
    /// Called when the bot hands one in, and also when a quest giver turns out to have nothing
    /// on offer — which on a character with history is how the bot finds out that a quest was
    /// done long before it arrived.
    /// </remarks>
    void MarkCompleted(uint questId);

    /// <summary>Takes a quest from the giver the character is talking to.</summary>
    QuestGiverResult Accept(uint questId);

    /// <summary>Hands a quest in to the giver the character is talking to.</summary>
    /// <param name="questId">The quest.</param>
    /// <param name="rewardIndex">
    /// Which reward to take, 1-based, or 0 when the quest offers no choice.
    /// </param>
    QuestGiverResult TurnIn(uint questId, int rewardIndex);

    /// <summary>Drops a quest from the log.</summary>
    bool Abandon(uint questId);
}

/// <summary>Questions worth asking a quest log.</summary>
public static class QuestLogExtensions
{
    /// <summary>The quest's entry in the log, or null when it is not in it.</summary>
    public static QuestLogEntry? Find(this IQuestLog log, uint questId)
    {
        ArgumentNullException.ThrowIfNull(log);

        foreach (QuestLogEntry entry in log.Entries)
        {
            if (entry.QuestId == questId)
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>True when the quest is in the log, finished or not.</summary>
    public static bool IsInLog(this IQuestLog log, uint questId) => log.Find(questId) is not null;

    /// <summary>True when the quest is in the log and every objective is done.</summary>
    public static bool IsReadyToTurnIn(this IQuestLog log, uint questId) =>
        log.Find(questId) is { IsComplete: true };

    /// <summary>True when there is no room to take another quest.</summary>
    public static bool IsFull(this IQuestLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        return log.Entries.Count >= log.Capacity;
    }
}
