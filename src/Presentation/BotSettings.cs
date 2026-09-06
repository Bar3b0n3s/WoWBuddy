using WoWBuddy.BotBases.Group;

namespace WoWBuddy.Presentation;

/// <summary>
/// What the window remembers between sessions.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is a choice the user made, not a fact about the client. Picking a bot base,
/// a routine and a profile again on every launch is the sort of friction that makes a tool feel
/// unfinished, and none of it is expensive to keep.
/// </para>
/// <para>
/// <b>No credentials, ever.</b> There is nothing here to log a character in with, and there
/// never will be: this project makes no network calls, and storing an account password to be
/// typed into a game client is a promise it is not in a position to keep safely.
/// </para>
/// </remarks>
public sealed record BotSettings
{
    /// <summary>The file this is kept in.</summary>
    public const string FileName = "settings";

    /// <summary>The bot base last chosen.</summary>
    public string BotBase { get; init; } = "Grind";

    /// <summary>The combat routine last chosen.</summary>
    public string Routine { get; init; } = string.Empty;

    /// <summary>The profile last loaded.</summary>
    public string ProfilePath { get; init; } = string.Empty;

    /// <summary>
    /// Where the navigation meshes are.
    /// </summary>
    /// <remarks>
    /// Empty means the <c>mmaps</c> folder beside the executable. Extracting them puts them
    /// wherever the extractor was run, and copying gigabytes to sit next to the bot is a poor
    /// default to force on anyone.
    /// </remarks>
    public string MmapsDirectory { get; init; } = string.Empty;

    /// <summary>What the character plays in a group.</summary>
    /// <remarks>Never detected. See <see cref="PartyRole"/>.</remarks>
    public PartyRole Role { get; init; } = PartyRole.None;

    /// <summary>Whether the character can skin.</summary>
    public bool CanSkin { get; init; }

    /// <summary>Who to post keepable items to, or empty for nobody.</summary>
    public string MailRecipient { get; init; } = string.Empty;

    /// <summary>The profession the crafting base works on.</summary>
    public string CraftProfession { get; init; } = string.Empty;

    /// <summary>The recipe it makes, or empty for whatever raises the skill fastest.</summary>
    public string CraftRecipe { get; init; } = string.Empty;

    /// <summary>Where the meshes actually are, resolved against the default.</summary>
    public string ResolveMmaps(string baseDirectory) =>
        MmapsDirectory.Length > 0
            ? MmapsDirectory
            : Path.Combine(baseDirectory, "mmaps");
}
