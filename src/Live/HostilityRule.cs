using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.Live;

/// <summary>
/// Deciding whether a creature is worth attacking, without faction data.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is an approximation and the only guesswork in the live view, so it is kept apart
/// where it can be read and tested.</b> Whether a creature is an enemy is properly decided by
/// its faction template, and resolving one needs a data file this project does not ship.
/// </para>
/// <para>
/// What it does instead is exclude what can be ruled out with certainty and leave the rest to
/// the profile's avoid list. It will occasionally offer a neutral critter as a target. The
/// alternative was to guess at faction ids, which this project does not do — and a user who has
/// faction data can supply a better rule, which is why the view takes one.
/// </para>
/// </remarks>
public static class HostilityRule
{
    /// <summary>True when a unit could be an enemy.</summary>
    /// <param name="isPlayer">Whether it is another player's character.</param>
    /// <param name="npcFlags">What services it offers, if any.</param>
    /// <param name="flags">Its unit flags.</param>
    public static bool CouldBeHostile(bool isPlayer, NpcFlags npcFlags, UnitFlags flags)
    {
        // Attacking another player is a decision the grinding bases should never make on their
        // own. The battleground base picks its own targets and does not come through here.
        if (isPlayer)
        {
            return false;
        }

        // Vendors, trainers, quest givers, flight masters and bankers all carry NPC flags, and
        // none of them is a target. This is the rule doing most of the work.
        if (npcFlags != NpcFlags.None)
        {
            return false;
        }

        // The client would refuse these anyway, and trying produces an error per attempt.
        return !flags.HasFlag(UnitFlags.NotSelectable) && !flags.HasFlag(UnitFlags.Pacified);
    }
}
