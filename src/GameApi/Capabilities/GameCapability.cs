namespace WoWBuddy.GameApi.Capabilities;

/// <summary>Something the bot needs the client to be able to tell it.</summary>
/// <remarks>
/// One member per feature that depends on Lua this project has not verified against a live
/// 12340 client. The manual test scripts ask a human to check these by hand; this asks the
/// client itself, at attach time, so that a feature which cannot work is switched off rather
/// than left to fail quietly four hours into a session.
/// </remarks>
public enum GameCapability
{
    /// <summary>Reading anything back out of Lua at all.</summary>
    LuaResults,

    /// <summary>How many quests are in the log, and their titles.</summary>
    QuestLog,

    /// <summary>Which quest a log entry is, which on 3.3.5a means parsing its hyperlink.</summary>
    QuestIds,

    /// <summary>How far along each of a quest's objectives is.</summary>
    QuestObjectives,

    /// <summary>Whether a quest has ever been handed in.</summary>
    QuestCompletion,

    /// <summary>Taking and handing in quests.</summary>
    QuestGiver,

    /// <summary>Who else is in the group.</summary>
    Party,

    /// <summary>What each group member is playing.</summary>
    PartyRoles,

    /// <summary>How many of an item the character is carrying.</summary>
    Inventory,

    /// <summary>Using an item from the bags.</summary>
    UseItem,

    /// <summary>Queueing for and sitting inside a battleground.</summary>
    Battlegrounds,

    /// <summary>Reading what the character can make, and making it.</summary>
    TradeSkills,

    /// <summary>What a recipe is made from, and how much of it the character has.</summary>
    Reagents,

    /// <summary>Doing business with a vendor or a mailbox.</summary>
    Vendor,

    /// <summary>Reading a merchant's shelves and buying from them.</summary>
    Buying,

    /// <summary>Spending talent points.</summary>
    Talents,

    /// <summary>Getting on and off a mount.</summary>
    Travel,

    /// <summary>Which server the character is on.</summary>
    Realm,
}

/// <summary>One thing to ask the client about itself.</summary>
/// <param name="Capability">What it establishes.</param>
/// <param name="Functions">The Lua functions the feature needs. All must exist.</param>
/// <param name="Purpose">What the bot uses them for.</param>
/// <param name="Consequence">What stops working without them.</param>
/// <param name="ExpectedAbsent">
/// True when this project already believes the client does not have it, so its absence is a
/// known limitation rather than a surprise. See <see cref="CapabilityReport"/>.
/// </param>
public sealed record CapabilityProbe(
    GameCapability Capability,
    IReadOnlyList<string> Functions,
    string Purpose,
    string Consequence,
    bool ExpectedAbsent = false);

/// <summary>What asking produced.</summary>
/// <param name="Capability">What was asked about.</param>
/// <param name="Available">Whether the client has it.</param>
/// <param name="Missing">The functions it does not have.</param>
/// <param name="Probe">What was asked, so the report can explain itself.</param>
public sealed record CapabilityResult(
    GameCapability Capability,
    bool Available,
    IReadOnlyList<string> Missing,
    CapabilityProbe Probe)
{
    /// <summary>
    /// True when this is a surprise rather than a known limitation.
    /// </summary>
    /// <remarks>
    /// The difference matters to a user. A missing function this project already expects to be
    /// missing is a documented gap they can read about; one it expected to find means their
    /// client is not what the bot thinks it is, and the rest of the offsets are suspect too.
    /// </remarks>
    public bool IsSurprise => !Available && !Probe.ExpectedAbsent;
}
