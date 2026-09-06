using WoWBuddy.BotBases;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Objects;

namespace WoWBuddy.Live;

/// <summary>
/// Everything about the character and its surroundings that comes from memory, plus the verbs
/// that need the game's own functions.
/// </summary>
/// <remarks>
/// <para>
/// The seam between what is verified and what is not. Everything behind this interface rests on
/// offsets the bot checks against your client at attach time, or on native calls it self-tests
/// before using — it is the trustworthy half of the bot's picture of the world.
/// </para>
/// <para>
/// It exists as an interface for the same reason the presentation layer does: so that
/// <see cref="LiveBotState"/>, which is where all the composition and most of the judgement
/// lives, can be exercised without a game running. The implementation over a real client is
/// deliberately thin enough to read in one sitting.
/// </para>
/// </remarks>
public interface ICharacterView
{
    /// <summary>True when a character is logged in and the object manager is populated.</summary>
    bool IsInWorld { get; }

    /// <summary>True when the character is dead, corpse not yet released.</summary>
    bool IsDead { get; }

    /// <summary>True when the character is a ghost.</summary>
    bool IsGhost { get; }

    /// <summary>True when the character is fighting.</summary>
    bool IsInCombat { get; }

    /// <summary>Where the character is.</summary>
    Vector3 Position { get; }

    /// <summary>Which map it is on.</summary>
    int MapId { get; }

    /// <summary>Its health, 0 to 100.</summary>
    double HealthPercent { get; }

    /// <summary>Its level.</summary>
    int Level { get; }

    /// <summary>What it has targeted, or null.</summary>
    CandidateTarget? Target { get; }

    /// <summary>Hostile units it can see.</summary>
    IReadOnlyList<CandidateTarget> NearbyEnemies { get; }

    /// <summary>Corpses it killed that still hold loot.</summary>
    IReadOnlyList<CandidateTarget> LootableCorpses { get; }

    /// <summary>Corpses it has looted that still hold a skin. Empty when it cannot skin.</summary>
    IReadOnlyList<CandidateTarget> SkinnableCorpses { get; }

    /// <summary>Objects it can see: nodes, vendors, mailboxes, flags.</summary>
    IReadOnlyList<VisibleObject> VisibleObjects { get; }

    /// <summary>Where its own corpse is, when it has one.</summary>
    Vector3? CorpsePosition { get; }

    /// <summary>True while a loot window is open.</summary>
    bool IsLooting { get; }

    /// <summary>Where something is, by GUID, or null when the client cannot see it.</summary>
    Vector3? Locate(WoWGuid guid);

    /// <summary>Targets a unit.</summary>
    bool SetTarget(WoWGuid guid);

    /// <summary>Interacts with something: a node, a vendor, a corpse, a flag.</summary>
    bool Interact(WoWGuid guid);

    /// <summary>Opens a corpse and takes everything on it.</summary>
    bool Loot(WoWGuid guid);

    /// <summary>Releases to the graveyard.</summary>
    bool ReleaseCorpse();

    /// <summary>Takes the corpse back, when standing on it.</summary>
    bool RetrieveCorpse();

    /// <summary>Sits down to eat or drink.</summary>
    bool StartResting();
}
