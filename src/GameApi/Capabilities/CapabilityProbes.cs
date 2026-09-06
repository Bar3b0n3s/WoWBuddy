using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Execution;

namespace WoWBuddy.GameApi.Capabilities;

/// <summary>
/// Asks the attached client which of the calls the bot needs it actually has.
/// </summary>
/// <remarks>
/// <para>
/// <b>Existence, not behaviour.</b> Each probe asks whether a function exists, not what it
/// returns, because what it returns depends on the state the character happens to be in: a
/// party call returns nothing when solo, a quest call returns nothing with an empty log. Asking
/// whether the function is there separates "this client cannot do that" from "you happen to
/// have no quests", and only the first is worth switching a feature off for.
/// </para>
/// <para>
/// What existence does <em>not</em> establish is that a call returns what this bot expects — that
/// <c>GetQuestLogTitle</c> puts completion in the seventh slot, say. That needs a character with
/// a quest in the log and a person to look at the answer, which is what the manual test scripts
/// are for. The probe narrows the unverified surface; it does not close it.
/// </para>
/// </remarks>
public static class CapabilityProbes
{
    /// <summary>Everything the bot asks a client about itself.</summary>
    public static IReadOnlyList<CapabilityProbe> All { get; } =
    [
        new(GameCapability.QuestLog,
            ["GetNumQuestLogEntries", "GetQuestLogTitle", "SelectQuestLogEntry"],
            "reading the quest log",
            "The questing base cannot run."),

        new(GameCapability.QuestIds,
            ["GetQuestLink"],
            "working out which quest a log entry is",
            "The questing base cannot match the log against a profile. On 3.3.5a the id is not "
            + "in GetQuestLogTitle, so the hyperlink is the only way to get it."),

        new(GameCapability.QuestObjectives,
            ["GetQuestLogLeaderBoard", "GetNumQuestLeaderBoards"],
            "reading how far along each objective is",
            "Objective steps fall back to watching whether the whole quest completes."),

        // Expected to be absent. IsQuestFlaggedCompleted arrived in 4.0, and no offset for the
        // client's completed-quest bitmask is recorded in this project, so the bot remembers
        // what it hands in and learns the rest by finding givers with nothing on offer.
        new(GameCapability.QuestCompletion,
            ["IsQuestFlaggedCompleted"],
            "asking whether a quest was ever handed in",
            "The bot remembers only what it hands in itself, so on a character with history it "
            + "will walk to some quest givers once and find nothing on offer.",
            ExpectedAbsent: true),

        new(GameCapability.QuestGiver,
            ["AcceptQuest", "CompleteQuest", "GetQuestReward", "GetNumQuestChoices"],
            "taking and handing in quests",
            "The questing base cannot run."),

        new(GameCapability.Party,
            ["GetNumPartyMembers", "UnitGUID", "UnitHealth", "UnitHealthMax", "UnitIsUnit"],
            "seeing who else is in the group",
            "The dungeon base cannot follow, assist or heal anyone but itself."),

        // Unverified rather than known-absent: the Dungeon Finder assigns roles in 3.3.x, so
        // this may well exist. The bot does not use it either way — the role is a setting,
        // because guessing wrong means tanking in cloth — but knowing is worth a line.
        new(GameCapability.PartyRoles,
            ["UnitGroupRolesAssigned"],
            "reading what each group member is playing",
            "Nothing: the role is a setting regardless. This is recorded to find out whether "
            + "the call exists on 12340 at all.",
            ExpectedAbsent: true),

        new(GameCapability.Inventory,
            ["GetItemCount", "GetContainerNumSlots", "GetContainerItemLink"],
            "counting what the character is carrying",
            "Collect objectives and item conditions cannot be answered, and bag management "
            + "falls back to what the bot can see in memory."),

        new(GameCapability.UseItem,
            ["UseItemByName"],
            "using an item from the bags",
            "UseItem objectives cannot run."),

        new(GameCapability.Battlegrounds,
            ["GetBattlefieldStatus", "JoinBattlefield", "AcceptBattlefieldPort", "LeaveBattlefield"],
            "queueing for and leaving a battleground",
            "The battleground base cannot queue. It can still play one you joined by hand."),
    ];

    /// <summary>Asks the client about every capability.</summary>
    /// <param name="lua">The attached client's Lua.</param>
    /// <returns>What it can and cannot do.</returns>
    public static CapabilityReport Probe(ILuaEvaluator lua)
    {
        ArgumentNullException.ThrowIfNull(lua);

        CapabilityReport report = new();

        // Without the round trip nothing below can be established, and reporting ten
        // capabilities as missing would be misleading: they are unknown, not absent.
        report.Add(new CapabilityResult(
            GameCapability.LuaResults,
            lua.CanReadResults,
            lua.CanReadResults ? [] : ["the Lua round trip"],
            new CapabilityProbe(
                GameCapability.LuaResults,
                ["__wowbuddy_result"],
                "reading anything back out of the client",
                lua.ResultsUnavailableReason)));

        if (!lua.CanReadResults)
        {
            Log.For<CapabilityReport>().Warning(
                "Cannot ask the client what it supports: {Reason}", lua.ResultsUnavailableReason);

            return report;
        }

        foreach (CapabilityProbe probe in All)
        {
            List<string> missing = [];

            foreach (string function in probe.Functions)
            {
                if (!Exists(lua, function))
                {
                    missing.Add(function);
                }
            }

            report.Add(new CapabilityResult(probe.Capability, missing.Count == 0, missing, probe));
        }

        foreach (CapabilityResult surprise in report.Surprises)
        {
            Log.For<CapabilityReport>().Warning(
                "This client has no {Missing}, which this bot expected to find. {Consequence}",
                string.Join(", ", surprise.Missing), surprise.Probe.Consequence);
        }

        return report;
    }

    /// <summary>True when the client has a global function by that name.</summary>
    /// <remarks>
    /// <c>type(x) == "function"</c> rather than <c>x ~= nil</c>: a global that exists but is a
    /// table or a number is not something the bot can call, and on a heavily modified server
    /// that is a real possibility.
    /// </remarks>
    private static bool Exists(ILuaEvaluator lua, string function) =>
        string.Equals(lua.Evaluate($"type({function})"), "function", StringComparison.Ordinal);
}
