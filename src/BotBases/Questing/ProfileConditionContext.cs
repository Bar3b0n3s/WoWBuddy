using WoWBuddy.Profiles;

namespace WoWBuddy.BotBases.Questing;

/// <summary>
/// Answers a profile's conditions from what the bot can see right now.
/// </summary>
/// <remarks>
/// A thin adapter on purpose. Conditions are evaluated on every tick, so anything expensive
/// here would be paid for hundreds of times a minute; everything it reads is already a plain
/// value on <see cref="IBotState"/>.
/// </remarks>
public sealed class ProfileConditionContext(IBotState state) : IProfileConditionContext
{
    private readonly IBotState _state = state ?? throw new ArgumentNullException(nameof(state));

    public int Level => _state.Level;

    public long Money => _state.Inventory.Copper;

    public int BagsFullPercent => (int)Math.Round(_state.Inventory.UsedFraction * 100d);

    public bool IsQuestCompleted(uint questId) => _state.Quests.IsCompleted(questId);

    public bool IsQuestInLog(uint questId) => _state.Quests.IsInLog(questId);

    public bool IsQuestReadyToTurnIn(uint questId) => _state.Quests.IsReadyToTurnIn(questId);

    public int ItemCount(uint itemId) => _state.ItemCount(itemId);
}
