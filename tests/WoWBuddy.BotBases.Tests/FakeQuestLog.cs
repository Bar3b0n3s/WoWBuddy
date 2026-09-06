using WoWBuddy.BotBases.Questing;

namespace WoWBuddy.BotBases.Tests;

/// <summary>A quest log set by hand.</summary>
/// <remarks>
/// The point of the whole plain-values design: a questing profile's decisions can be checked
/// by describing a quest log, rather than by playing a character to the situation and hoping
/// it arises.
/// </remarks>
public sealed class FakeQuestLog : IQuestLog
{
    private readonly List<QuestLogEntry> _entries = [];
    private readonly HashSet<uint> _completed = [];

    public IReadOnlyList<QuestLogEntry> Entries => _entries;

    public int Capacity { get; set; } = 25;

    /// <summary>What the log was asked to do, in order.</summary>
    public List<string> Actions { get; } = [];

    /// <summary>What <see cref="Accept"/> returns.</summary>
    public QuestGiverResult AcceptResult { get; set; } = QuestGiverResult.Done;

    /// <summary>What <see cref="TurnIn"/> returns.</summary>
    public QuestGiverResult TurnInResult { get; set; } = QuestGiverResult.Done;

    public bool IsCompleted(uint questId) => _completed.Contains(questId);

    public void MarkCompleted(uint questId)
    {
        Actions.Add($"MarkCompleted({questId})");
        _completed.Add(questId);
        _entries.RemoveAll(entry => entry.QuestId == questId);
    }

    public QuestGiverResult Accept(uint questId)
    {
        Actions.Add($"Accept({questId})");

        if (AcceptResult == QuestGiverResult.Done)
        {
            Add(questId);
        }

        return AcceptResult;
    }

    public QuestGiverResult TurnIn(uint questId, int rewardIndex)
    {
        Actions.Add($"TurnIn({questId}, reward {rewardIndex})");
        return TurnInResult;
    }

    public bool Abandon(uint questId)
    {
        Actions.Add($"Abandon({questId})");
        return _entries.RemoveAll(entry => entry.QuestId == questId) > 0;
    }

    /// <summary>Puts a quest in the log.</summary>
    public FakeQuestLog Add(uint questId, bool complete = false, params QuestObjective[] objectives)
    {
        _entries.RemoveAll(entry => entry.QuestId == questId);
        _entries.Add(new QuestLogEntry(questId, $"Quest {questId}", complete, objectives));
        return this;
    }

    /// <summary>Records a quest as already handed in.</summary>
    public FakeQuestLog Completed(uint questId)
    {
        _completed.Add(questId);
        return this;
    }

    /// <summary>Fills the log to its capacity.</summary>
    public FakeQuestLog Fill()
    {
        for (uint id = 1; _entries.Count < Capacity; id++)
        {
            Add(50_000 + id);
        }

        return this;
    }
}
