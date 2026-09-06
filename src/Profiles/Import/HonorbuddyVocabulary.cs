namespace WoWBuddy.Profiles.Import;

/// <summary>
/// What the importer believes Honorbuddy's profile elements and attributes mean.
/// </summary>
/// <remarks>
/// <para>
/// <b>This mapping is observational, not a specification.</b> Honorbuddy's profile format was
/// never published as one. What is here was derived from the shape of profiles the community
/// wrote and shared, so it is incomplete by construction and some of it may be wrong for any
/// given profile. That is why the importer's real job is not conversion but reporting: it says
/// exactly which elements, attributes and conditions it did not understand, and it refuses to
/// call a conversion clean when it dropped something that changes behaviour.
/// </para>
/// <para>
/// Keeping the whole vocabulary in one file means a user who finds a mapping wrong has one
/// place to correct it, and can see at a glance what the importer does and does not claim.
/// </para>
/// <para>
/// No Honorbuddy code or profile is used or included here. These are element and attribute
/// names — the vocabulary of a file format — applied to files the user already has.
/// </para>
/// </remarks>
public static class HonorbuddyVocabulary
{
    /// <summary>Root elements that mark a file as an Honorbuddy profile.</summary>
    public static readonly string[] RootElements = ["HBProfile", "HBProfiles"];

    /// <summary>Elements that hold the ordered list of steps.</summary>
    public static readonly string[] QuestOrderElements = ["QuestOrder", "Order"];

    /// <summary>Elements that hold hotspots inside a step.</summary>
    public static readonly string[] HotspotContainers = ["HuntingGrounds", "Hotspots"];

    /// <summary>Attribute names seen carrying a quest id.</summary>
    public static readonly string[] QuestIdAttributes = ["QuestId", "QuestID"];

    /// <summary>Attribute names seen carrying a quest name.</summary>
    public static readonly string[] QuestNameAttributes = ["QuestName"];

    /// <summary>
    /// Attribute names seen carrying the id of whatever the step acts on: the quest giver,
    /// the creature to kill, or the object to use.
    /// </summary>
    /// <remarks>
    /// Deliberately one list rather than one per step kind. Profiles are inconsistent about
    /// which of these they use, and all of them mean the same thing to this bot: the entry of
    /// the thing to walk to.
    /// </remarks>
    public static readonly string[] EntryAttributes =
        ["GiverId", "TurnInId", "MobId", "NpcId", "ObjectId", "GameObjectId", "Entry", "EntryId"];

    /// <summary>Attribute names seen carrying an item id.</summary>
    public static readonly string[] ItemIdAttributes = ["ItemId", "ItemID"];

    /// <summary>Attribute names seen carrying how many are needed.</summary>
    public static readonly string[] CountAttributes = ["CollectCount", "KillCount", "Count", "NumToCollect"];

    /// <summary>Attribute names seen carrying a working radius.</summary>
    public static readonly string[] RadiusAttributes = ["CollectionDistance", "Radius", "Range"];

    /// <summary>Attribute names seen carrying which objective of a quest is meant.</summary>
    public static readonly string[] ObjectiveIndexAttributes =
        ["QuestObjectiveIndex", "ObjectiveIndex", "Index"];

    /// <summary>Attribute names seen carrying a condition.</summary>
    public static readonly string[] ConditionAttributes = ["Condition"];

    /// <summary>
    /// Attributes that carry no meaning for this bot and are not worth reporting.
    /// </summary>
    /// <remarks>
    /// Presentation and bookkeeping only. Reporting these would bury the attributes that do
    /// matter under hundreds of lines of noise.
    /// </remarks>
    public static readonly string[] IgnorableAttributes =
    [
        "GiverName", "TurnInName", "MobName", "NpcName", "ObjectName",
        "GoalText", "StatusText", "Nav", "NavigationType",
        "WaitForNpcs", "AllowUseItemOnMob",
    ];
}
