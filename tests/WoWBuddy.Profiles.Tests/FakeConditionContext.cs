namespace WoWBuddy.Profiles.Tests;

/// <summary>
/// A character's situation, set by hand.
/// </summary>
/// <remarks>
/// Conditions are the part of a profile most likely to be subtly wrong, and they are pure
/// functions of a handful of numbers, so they can be tested exhaustively without a client.
/// </remarks>
internal sealed class FakeConditionContext : IProfileConditionContext
{
    public int Level { get; set; } = 1;

    public long Money { get; set; }

    public int BagsFullPercent { get; set; }

    public HashSet<uint> CompletedQuests { get; } = [];

    public HashSet<uint> QuestsInLog { get; } = [];

    public HashSet<uint> QuestsReadyToTurnIn { get; } = [];

    public Dictionary<uint, int> Items { get; } = [];

    public bool IsQuestCompleted(uint questId) => CompletedQuests.Contains(questId);

    public bool IsQuestInLog(uint questId) => QuestsInLog.Contains(questId);

    public bool IsQuestReadyToTurnIn(uint questId) => QuestsReadyToTurnIn.Contains(questId);

    public int ItemCount(uint itemId) => Items.TryGetValue(itemId, out int count) ? count : 0;
}
