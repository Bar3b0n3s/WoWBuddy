using WoWBuddy.Behavior;
using WoWBuddy.Common.Geometry;
using WoWBuddy.CombatRoutines;
using WoWBuddy.Core.Objects;

namespace WoWBuddy.BotBases;

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
}
