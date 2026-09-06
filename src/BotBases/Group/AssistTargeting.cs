using WoWBuddy.Core.Objects;

namespace WoWBuddy.BotBases.Group;

/// <summary>
/// Picking what to attack when someone else is deciding.
/// </summary>
/// <remarks>
/// <para>
/// The single most important rule in group play, and the one that separates a useful bot from
/// one nobody will group with twice: attack what the tank is attacking, and pull nothing of
/// your own. A damage character that picks its own targets breaks crowd control, pulls the
/// next room, and gets the group killed — and does it in a way that is obvious to everyone
/// present.
/// </para>
/// <para>
/// So the order here is deliberate and short. Whatever the anchor is on. Failing that, whatever
/// is already hitting the character, because standing there being eaten helps nobody. Failing
/// that, nothing at all — which is the answer that keeps groups alive.
/// </para>
/// </remarks>
public static class AssistTargeting
{
    /// <summary>Picks what to attack, or null to attack nothing.</summary>
    /// <param name="state">What the bot can see.</param>
    /// <param name="anchor">Whoever is deciding: the tank, or the leader.</param>
    public static CandidateTarget? Choose(IBotState state, PartyMember? anchor)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (anchor is { IsAlive: true, TargetGuid.IsZero: false } present
            && Find(state, present.TargetGuid) is { } theirs)
        {
            return theirs;
        }

        return Attacker(state);
    }

    /// <summary>Whatever is currently hitting the character, nearest first.</summary>
    public static CandidateTarget? Attacker(IBotState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.NearbyEnemies
            .Where(enemy => enemy is { IsAlive: true, IsTargetingMe: true })
            .OrderBy(enemy => enemy.Distance)
            .Select(enemy => (CandidateTarget?)enemy)
            .FirstOrDefault();
    }

    private static CandidateTarget? Find(IBotState state, WoWGuid guid)
    {
        foreach (CandidateTarget enemy in state.NearbyEnemies)
        {
            if (enemy.Guid == guid && enemy.IsAlive)
            {
                return enemy;
            }
        }

        // The anchor is targeting something the bot cannot see as an enemy — usually another
        // player, or a friendly NPC they are talking to. Not a reason to attack anything.
        return null;
    }
}
