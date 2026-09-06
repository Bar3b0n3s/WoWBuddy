using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines;

/// <summary>
/// Everything the bot needs from a class specialisation.
/// </summary>
/// <remarks>
/// <para>
/// Split by <em>when</em> the bot needs a decision rather than by what the spells do, because
/// that is what the behaviour tree above knows: it knows the character is out of combat and
/// unbuffed, or in combat with a target, or resting. It does not know what a Warrior is.
/// </para>
/// <para>
/// Every method may be called on any tick and must be cheap and idempotent. None of them
/// should loop, sleep, or assume it will be called again.
/// </para>
/// </remarks>
public interface ICombatRoutine
{
    /// <summary>A name for the UI.</summary>
    string Name { get; }

    /// <summary>The class this routine plays.</summary>
    WoWClass Class { get; }

    /// <summary>How far away the routine can start a fight from.</summary>
    /// <remarks>
    /// A caster pulls from thirty yards, a melee character has to walk up. The bot base uses
    /// this to decide when to stop approaching and start fighting.
    /// </remarks>
    float PullRange { get; }

    /// <summary>Applies out-of-combat buffs. Returns true when something was cast.</summary>
    bool Buff(ICombatContext context);

    /// <summary>Starts a fight with the current target. Returns true when something was cast.</summary>
    bool Pull(ICombatContext context);

    /// <summary>Fights. Returns true when something was cast.</summary>
    bool Combat(ICombatContext context);

    /// <summary>
    /// Recovers out of combat: eats, drinks, bandages, heals.
    /// </summary>
    /// <returns>True when the character still needs to rest.</returns>
    bool Rest(ICombatContext context);

    /// <summary>Keeps a pet alive and attacking. Returns true when something was done.</summary>
    bool PetControl(ICombatContext context) => false;

    /// <summary>
    /// True when the character is healthy enough to start another fight.
    /// </summary>
    /// <remarks>
    /// The bot base asks this before pulling. A routine that says yes too eagerly produces a
    /// character that dies every third pull; one that says no too readily produces a character
    /// that spends the night eating.
    /// </remarks>
    bool IsReadyToFight(ICombatContext context);
}
