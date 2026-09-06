using WoWBuddy.BotBases.Group;
using WoWBuddy.Common.Scheduling;

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
/// <para>
/// The whisper settings are the exception to "everything here is a choice": the ignore list
/// holds other people's character names. They are kept because the feature is unusable without
/// them — one "afk?" from a guildmate would end every session — and they go no further than
/// this file.
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

    /// <summary>
    /// Whether to get back into the world after a dropped connection.
    /// </summary>
    /// <remarks>
    /// Not a login, and needs no password: a disconnection leaves the client at character
    /// select with the last character still chosen. A client at the login screen needs a
    /// person, and the bot says so rather than trying.
    /// </remarks>
    public bool Reconnect { get; init; } = true;

    /// <summary>What to do when a person whispers the character.</summary>
    /// <remarks>
    /// Pausing by default. A character that keeps killing boars while a game master asks it a
    /// question has answered the question, and that is the failure that ends accounts rather
    /// than sessions.
    /// </remarks>
    public WhisperResponse WhisperResponse { get; init; } = WhisperResponse.Pause;

    /// <summary>How long to stand still for after being whispered, in minutes.</summary>
    public int WhisperPauseMinutes { get; init; } = 10;

    /// <summary>Names whose whispers are ignored, comma separated.</summary>
    /// <remarks>
    /// Your own alt, or a friend who knows what the character is doing. Without it a guild's
    /// ordinary chatter stops the bot every few minutes.
    /// </remarks>
    public string WhisperIgnore { get; init; } = string.Empty;

    /// <summary>The whisper settings these choices describe.</summary>
    public WhisperSettings ResolveWhispers() => new()
    {
        Response = WhisperResponse,
        PauseFor = TimeSpan.FromMinutes(Math.Clamp(WhisperPauseMinutes, 1, 24 * 60)),
        Ignore = new HashSet<string>(
            WhisperIgnore.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase),
    };

    /// <summary>The mount to cast, as the client names it. Empty means walk everywhere.</summary>
    public string MountName { get; init; } = string.Empty;

    /// <summary>The shortest journey worth mounting for, in yards.</summary>
    public float MountForJourneysOver { get; init; } = 100f;

    /// <summary>
    /// The order to spend talent points in, as <c>tab:index</c> pairs.
    /// </summary>
    /// <remarks>
    /// Empty leaves points unspent, which costs nothing. Spending them in the wrong order costs
    /// gold to undo, and this project has no talent data with which to work out a right order.
    /// </remarks>
    public string TalentBuild { get; init; } = string.Empty;

    /// <summary>Where the meshes actually are, resolved against the default.</summary>
    public string ResolveMmaps(string baseDirectory) =>
        MmapsDirectory.Length > 0
            ? MmapsDirectory
            : Path.Combine(baseDirectory, "mmaps");
}
