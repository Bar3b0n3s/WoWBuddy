using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Objects;

namespace WoWBuddy.BotBases.Group;

/// <summary>What a character is doing in a group.</summary>
/// <remarks>
/// <para>
/// <b>Set by the user, not detected.</b> A role decides whether the bot pulls, holds back, or
/// stands at range, and getting it wrong is the difference between a run and a wipe. The 3.3.5a
/// client does know about roles — the Dungeon Finder assigns them — but this project has not
/// verified how to read them, and a bot that guessed wrong would tank in cloth. So the setting
/// is authoritative.
/// </para>
/// <para>
/// TODO: verify whether <c>UnitGroupRolesAssigned</c> exists on 3.3.5a. If it does, it could
/// pre-fill the setting; it should still not override it, because a player can be assigned a
/// role by the finder and be playing another.
/// </para>
/// </remarks>
public enum PartyRole
{
    /// <summary>Not in a group, or playing alone.</summary>
    None = 0,

    /// <summary>Pulls, holds threat, decides where the group goes.</summary>
    Tank,

    /// <summary>Keeps the group alive and stays out of the fight.</summary>
    Healer,

    /// <summary>Attacks what the tank is attacking.</summary>
    Damage,
}

/// <summary>Someone else in the character's group.</summary>
/// <param name="Guid">Their GUID.</param>
/// <param name="Name">Their name, for logs.</param>
/// <param name="Position">Where they are.</param>
/// <param name="Distance">Yards from the character.</param>
/// <param name="Level">Their level.</param>
/// <param name="HealthPercent">Their health, 0 to 100.</param>
/// <param name="PowerPercent">Their mana or equivalent, 0 to 100.</param>
/// <param name="IsAlive">Whether they are alive.</param>
/// <param name="IsOnline">Whether they are connected.</param>
/// <param name="IsLeader">Whether they lead the group.</param>
/// <param name="IsInCombat">Whether they are fighting.</param>
/// <param name="TargetGuid">What they have targeted, or an empty GUID.</param>
/// <param name="Role">What they are playing, as far as the bot has been told.</param>
public readonly record struct PartyMember(
    WoWGuid Guid,
    string Name,
    Vector3 Position,
    float Distance,
    int Level,
    double HealthPercent,
    double PowerPercent,
    bool IsAlive,
    bool IsOnline,
    bool IsLeader,
    bool IsInCombat,
    WoWGuid TargetGuid,
    PartyRole Role)
{
    /// <summary>True when they are alive, connected, and close enough to be helped.</summary>
    /// <param name="range">How far counts as close enough. Forty yards is most heals.</param>
    public bool CanBeHelped(float range = 40f) => IsAlive && IsOnline && Distance <= range;
}

/// <summary>
/// The character's group, as plain values.
/// </summary>
/// <remarks>
/// Same shape as the rest of <see cref="IBotState"/>: values and simple questions, so that
/// "what does the bot do when the healer is dead and the tank is at 20%" can be answered in a
/// test rather than by arranging it in a live dungeon.
/// </remarks>
public interface IPartyState
{
    /// <summary>Everyone else in the group, nearest first. Empty when playing alone.</summary>
    IReadOnlyList<PartyMember> Members { get; }

    /// <summary>What the bot's own character is playing.</summary>
    PartyRole MyRole { get; }

    /// <summary>True when the character is inside a dungeon or raid.</summary>
    bool IsInInstance { get; }

    /// <summary>Follows a party member, using the client's own follow rather than pathing.</summary>
    /// <remarks>
    /// Worth having as its own verb: the client's follow keeps a sensible distance, handles
    /// doorways, and looks like a person following someone. Pathing to a moving target does
    /// none of that.
    /// </remarks>
    bool Follow(WoWGuid guid);
}

/// <summary>Questions worth asking a group.</summary>
public static class PartyStateExtensions
{
    /// <summary>True when there is anyone else in the group.</summary>
    public static bool IsInGroup(this IPartyState party)
    {
        ArgumentNullException.ThrowIfNull(party);
        return party.Members.Count > 0;
    }

    /// <summary>The group's leader, or null when nobody in it is marked as one.</summary>
    public static PartyMember? Leader(this IPartyState party)
    {
        ArgumentNullException.ThrowIfNull(party);

        foreach (PartyMember member in party.Members)
        {
            if (member.IsLeader)
            {
                return member;
            }
        }

        return null;
    }

    /// <summary>The group's tank, or null when nobody is playing one.</summary>
    public static PartyMember? Tank(this IPartyState party)
    {
        ArgumentNullException.ThrowIfNull(party);

        foreach (PartyMember member in party.Members)
        {
            if (member.Role == PartyRole.Tank)
            {
                return member;
            }
        }

        return null;
    }

    /// <summary>
    /// Whoever the bot should be watching: the tank, or failing that the leader.
    /// </summary>
    /// <remarks>
    /// The tank first, because that is who decides what gets attacked and where the group
    /// goes. The leader is the fallback for a group that has not been told its roles, which is
    /// most of them.
    /// </remarks>
    public static PartyMember? Anchor(this IPartyState party) => party.Tank() ?? party.Leader();

    /// <summary>The member with the least health worth healing, or null when nobody needs it.</summary>
    /// <param name="party">The group.</param>
    /// <param name="threshold">Health below which a member counts as needing help.</param>
    /// <param name="range">How far a heal reaches.</param>
    public static PartyMember? MostHurt(this IPartyState party, double threshold = 100d, float range = 40f)
    {
        ArgumentNullException.ThrowIfNull(party);

        PartyMember? worst = null;

        foreach (PartyMember member in party.Members)
        {
            if (!member.CanBeHelped(range) || member.HealthPercent >= threshold)
            {
                continue;
            }

            if (worst is null || member.HealthPercent < worst.Value.HealthPercent)
            {
                worst = member;
            }
        }

        return worst;
    }

    /// <summary>Members who are alive, connected and within range.</summary>
    public static IEnumerable<PartyMember> Reachable(this IPartyState party, float range = 40f)
    {
        ArgumentNullException.ThrowIfNull(party);
        return party.Members.Where(member => member.CanBeHelped(range));
    }

    /// <summary>True when anyone in the group is fighting.</summary>
    public static bool AnyoneInCombat(this IPartyState party)
    {
        ArgumentNullException.ThrowIfNull(party);
        return party.Members.Any(member => member.IsInCombat && member.IsAlive);
    }
}
