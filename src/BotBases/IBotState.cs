using WoWBuddy.Behavior;
using WoWBuddy.Common.Geometry;
using WoWBuddy.CombatRoutines;
using WoWBuddy.BotBases.Support;
using WoWBuddy.Common.Scheduling;
using WoWBuddy.BotBases.Battlegrounds;
using WoWBuddy.BotBases.Group;
using WoWBuddy.BotBases.Questing;
using WoWBuddy.Core.Objects;

namespace WoWBuddy.BotBases;

/// <summary>A world object the client can currently see.</summary>
/// <param name="Guid">Its GUID, which is how it is interacted with.</param>
/// <param name="Entry">Its template id, which is what says whether it is worth gathering.</param>
/// <param name="Position">Where it is.</param>
/// <param name="HasPosition">
/// Whether the position is real. False when the game object position offset could not be
/// worked out at attach, in which case the object can be seen but not walked to.
/// </param>
/// <param name="Distance">Yards from the character.</param>
public readonly record struct VisibleObject(
    WoWGuid Guid,
    uint Entry,
    Vector3 Position,
    bool HasPosition,
    float Distance);

/// <summary>A creature the bot could decide to fight.</summary>
/// <param name="Guid">Its GUID.</param>
/// <param name="Position">Where it is.</param>
/// <param name="Distance">Yards from the character.</param>
/// <param name="Level">Its level.</param>
/// <param name="HealthPercent">Its health.</param>
/// <param name="IsAlive">Whether it is alive.</param>
/// <param name="IsInCombat">Whether it is already fighting something.</param>
/// <param name="IsTargetingMe">Whether it is fighting the character.</param>
/// <param name="Entry">Its creature template id, for avoid lists.</param>
public readonly record struct CandidateTarget(
    WoWGuid Guid,
    Vector3 Position,
    float Distance,
    int Level,
    double HealthPercent,
    bool IsAlive,
    bool IsInCombat,
    bool IsTargetingMe,
    uint Entry);

/// <summary>
/// Everything the behaviour tree can see and do.
/// </summary>
/// <remarks>
/// <para>
/// An interface of plain values and simple verbs rather than the live game objects, for the
/// same reason the combat rotations take a snapshot: it makes the tree a function of a
/// situation, so the whole decision structure of the bot can be tested by describing a
/// situation rather than by logging in and hoping it arises.
/// </para>
/// <para>
/// That matters most for the parts that are hardest to reproduce deliberately. Dying at the
/// wrong moment, getting stuck while something is attacking, running out of mana mid-pull —
/// these are exactly the cases where an unattended bot goes wrong, and exactly the cases
/// nobody can reliably stage in a live client.
/// </para>
/// </remarks>
public interface IBotState
{
    /// <summary>The current time. Injected so behaviour can be tested without waiting.</summary>
    DateTimeOffset Now { get; }

    /// <summary>Shared loosely typed state.</summary>
    Blackboard Blackboard { get; }

    /// <summary>The combat routine playing this character.</summary>
    ICombatRoutine Routine { get; }

    /// <summary>What the routine sees.</summary>
    ICombatContext Combat { get; }

    /// <summary>True when a character is logged in and the world is loaded.</summary>
    bool IsInWorld { get; }

    /// <summary>True when the character is dead or a ghost.</summary>
    bool IsDead { get; }

    /// <summary>True when the character is a ghost and needs to reach its corpse.</summary>
    bool IsGhost { get; }

    /// <summary>True when the character is in combat.</summary>
    bool IsInCombat { get; }

    /// <summary>Where the character is.</summary>
    Vector3 Position { get; }

    /// <summary>The map the character is on.</summary>
    int MapId { get; }

    /// <summary>The character's health, 0 to 100.</summary>
    double HealthPercent { get; }

    /// <summary>The current target, or null.</summary>
    CandidateTarget? Target { get; }

    /// <summary>Creatures nearby that could be fought, nearest first.</summary>
    IReadOnlyList<CandidateTarget> NearbyEnemies { get; }

    /// <summary>Where the character's corpse is, once it is known.</summary>
    Vector3? CorpsePosition { get; }

    /// <summary>True while the character is moving under the bot's direction.</summary>
    bool IsMoving { get; }

    /// <summary>True when movement has given up on the current path.</summary>
    bool MovementFailed { get; }

    /// <summary>Selects a unit as the current target.</summary>
    bool SetTarget(WoWGuid guid);

    /// <summary>Starts walking to a position. False when no route could be found.</summary>
    bool MoveTo(Vector3 destination);

    /// <summary>Stops moving.</summary>
    void StopMoving();

    /// <summary>Releases the character's spirit after dying.</summary>
    bool ReleaseCorpse();

    /// <summary>Reclaims the character's body once standing on the corpse.</summary>
    bool RetrieveCorpse();

    /// <summary>Sits down to eat or drink. False when there is nothing to consume.</summary>
    bool StartResting();

    // ---- Added in phase 5 -------------------------------------------------------------

    /// <summary>The character's level.</summary>
    int Level { get; }

    /// <summary>Bag space, durability and money.</summary>
    InventoryState Inventory { get; }

    /// <summary>What the scheduler thinks the bot should be doing.</summary>
    SessionState Session { get; }

    /// <summary>Corpses in range that still have loot on them, nearest first.</summary>
    IReadOnlyList<CandidateTarget> LootableCorpses { get; }

    /// <summary>Loots the given corpse. False when it could not be reached or opened.</summary>
    bool Loot(WoWGuid guid);

    /// <summary>True while a loot window is open and being worked through.</summary>
    bool IsLooting { get; }

    /// <summary>The errand the bot is currently running, if any.</summary>
    Errand CurrentErrand { get; }

    /// <summary>World objects the client can currently see.</summary>
    IReadOnlyList<VisibleObject> VisibleObjects { get; }

    /// <summary>Interacts with something by GUID: a node, a vendor, a mailbox.</summary>
    bool Interact(WoWGuid guid);

    /// <summary>
    /// Starts an errand. False when the bot does not know where to go for it.
    /// </summary>
    /// <remarks>
    /// Returning false is the normal case before profiles exist: the bot has no idea where
    /// the nearest vendor is until a profile tells it. The tree treats that as "cannot do
    /// this errand" rather than as an error.
    /// </remarks>
    bool BeginErrand(Errand errand);

    // ---- Added in phase 7 -------------------------------------------------------------

    /// <summary>The character's quest log, and the verbs for changing it.</summary>
    IQuestLog Quests { get; }

    /// <summary>How many of an item the character is carrying, across all bags.</summary>
    int ItemCount(uint itemId);

    /// <summary>
    /// Uses an item from the bags, on the current target when it has one.
    /// </summary>
    /// <returns>False when the item is not carried or is on cooldown.</returns>
    bool UseItem(uint itemId);

    // ---- Added in phase 8 -------------------------------------------------------------

    /// <summary>The character's group, and what it is doing.</summary>
    IPartyState Party { get; }

    /// <summary>Queueing for and sitting inside a battleground.</summary>
    IBattlegroundActions Battlegrounds { get; }
}
