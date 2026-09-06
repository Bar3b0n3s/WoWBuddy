namespace WoWBuddy.Presentation;

/// <summary>A way of playing the character, as the window offers it.</summary>
/// <param name="Name">What it is called.</param>
/// <param name="Description">What it does, for a tooltip.</param>
/// <param name="NeedsProfile">Whether it cannot run without a profile.</param>
/// <param name="NeedsGroup">Whether it expects the character to be in a group.</param>
public readonly record struct BotBaseOption(
    string Name,
    string Description,
    bool NeedsProfile = false,
    bool NeedsGroup = false)
{
    public override string ToString() => Name;

    /// <summary>The bot bases that ship with WoWBuddy.</summary>
    /// <remarks>
    /// Written out rather than discovered, unlike the combat routines. There are six of them,
    /// each needs a sentence and a couple of facts the type itself does not carry, and a list
    /// that has to be edited when one is added is not a burden at this size.
    /// </remarks>
    public static IReadOnlyList<BotBaseOption> All { get; } =
    [
        new("Grind", "Kills things in an area, then kills more things."),
        new("Gather", "Mines and herbs its way around a route.", NeedsProfile: true),
        new("Fish", "Fishes at one spot."),
        new("Questing", "Works through a profile's quests, then grinds.", NeedsProfile: true),
        new("Dungeon", "Follows, assists and plays its role in a group.", NeedsGroup: true),
        new("Battleground", "Queues, plays, and queues again.", NeedsProfile: true),
    ];
}
