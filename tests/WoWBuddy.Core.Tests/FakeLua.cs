using WoWBuddy.Core.Execution;

namespace WoWBuddy.Core.Tests;

/// <summary>
/// A client's Lua interpreter, answered by hand.
/// </summary>
/// <remarks>
/// Most of what the bot needs about quests, groups, bags and battlegrounds comes through Lua,
/// and the interesting code is the code that decides what to do when a call is not there. That
/// is only testable if the answers can be arranged, which is what this is for.
/// </remarks>
public sealed class FakeLua : ILuaEvaluator
{
    /// <summary>Global functions the client has.</summary>
    public HashSet<string> Functions { get; } = new(StringComparer.Ordinal);

    /// <summary>Answers for expressions that are not a type check.</summary>
    public Dictionary<string, string?> Answers { get; } = [];

    /// <summary>Everything the bot asked, in order.</summary>
    public List<string> Asked { get; } = [];

    /// <inheritdoc />
    public bool CanReadResults { get; set; } = true;

    /// <inheritdoc />
    public string ResultsUnavailableReason { get; set; } = string.Empty;

    /// <summary>
    /// Runs whenever the bot executes a script, so a test can make the client change.
    /// </summary>
    /// <remarks>
    /// The whole point of several of these calls is that the client is different afterwards —
    /// a quest accepted, a quest handed in — and the bot finds out by looking again. Modelling
    /// that needs the fake to change too.
    /// </remarks>
    public Action<string>? OnExecute { get; set; }

    /// <inheritdoc />
    public bool Execute(string script)
    {
        Asked.Add($"execute: {script}");
        OnExecute?.Invoke(script);
        return true;
    }

    /// <inheritdoc />
    public string? Evaluate(string expression)
    {
        Asked.Add(expression);

        if (!CanReadResults)
        {
            return null;
        }

        if (expression.StartsWith("type(", StringComparison.Ordinal)
            && expression.EndsWith(')'))
        {
            string name = expression[5..^1];
            return Functions.Contains(name) ? "function" : "nil";
        }

        return Answers.TryGetValue(expression, out string? answer) ? answer : null;
    }

    /// <inheritdoc />
    public int? EvaluateInt(string expression) =>
        int.TryParse(Evaluate(expression), out int value) ? value : null;

    /// <inheritdoc />
    public double? EvaluateDouble(string expression) =>
        double.TryParse(Evaluate(expression), out double value) ? value : null;

    /// <inheritdoc />
    public bool EvaluateBool(string expression) =>
        string.Equals(Evaluate(expression), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Says the client has these functions.</summary>
    public FakeLua With(params string[] functions)
    {
        foreach (string function in functions)
        {
            Functions.Add(function);
        }

        return this;
    }

    /// <summary>Says the client has everything a 3.3.5a client is expected to have.</summary>
    /// <remarks>
    /// Everything except the two this project believes are absent on 12340, so that a test
    /// wanting "an ordinary client" does not have to list thirty function names.
    /// </remarks>
    public static FakeLua Typical335a()
    {
        FakeLua lua = new();

        lua.With(
            "GetNumQuestLogEntries", "GetQuestLogTitle", "SelectQuestLogEntry", "GetQuestLink",
            "GetQuestLogLeaderBoard", "GetNumQuestLeaderBoards", "AcceptQuest", "CompleteQuest", "GetQuestReward",
            "GetNumQuestChoices", "GetNumPartyMembers", "GetNumRaidMembers", "UnitGUID", "UnitIsUnit", "UnitHealth",
            "UnitHealthMax", "UnitName", "UnitIsDeadOrGhost", "UnitIsConnected",
            "UnitIsPartyLeader", "UnitAffectingCombat", "IsInInstance", "FollowUnit", "GetItemCount", "GetContainerNumSlots",
            "GetContainerItemLink", "GetMoney", "GetInventoryItemDurability", "GetItemInfo", "UseContainerItem", "GetBattlefieldStatus", "GetBattlefieldWinner",
            "GetBattlefieldInstanceRunTime", "GetNumBattlegroundTypes", "GetBattlegroundInfo",
            "RequestBattlegroundInstanceInfo", "JoinBattlefield", "AcceptBattlefieldPort",
            "LeaveBattlefield", "GetNumTradeSkills", "GetTradeSkillInfo",
            "GetTradeSkillLine", "DoTradeSkill", "CloseTradeSkill",
            "GetTradeSkillNumReagents", "GetTradeSkillReagentInfo", "GetTradeSkillReagentItemLink",
            "GetMerchantNumItems", "GetMerchantItemInfo", "GetMerchantItemLink", "BuyMerchantItem",
            "CanMerchantRepair", "RepairAllItems", "GetRepairAllCost", "GetContainerItemInfo",
            "SendMail", "ClearSendMail", "CloseMerchant", "CloseMail",
            "GetNumTrainerServices", "GetTrainerServiceInfo", "GetTrainerServiceCost",
            "BuyTrainerService", "SetTrainerServiceTypeFilter", "CloseTrainer",
            "GetUnspentTalentPoints", "GetTalentInfo", "LearnTalent",
            "IsUsableSpell", "CastSpellByName", "Dismount", "GetRealmName", "CreateFrame");

        return lua;
    }
}
