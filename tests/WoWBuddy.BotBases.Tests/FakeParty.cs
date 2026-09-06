using WoWBuddy.BotBases.Group;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Objects;

namespace WoWBuddy.BotBases.Tests;

/// <summary>A group set by hand.</summary>
/// <remarks>
/// "What does the bot do when the healer is dead and the tank is at 20 per cent" is a question
/// worth having a definite answer to, and arranging it in a live dungeon is not a thing anyone
/// can do on demand.
/// </remarks>
public sealed class FakeParty : IPartyState
{
    private readonly List<PartyMember> _members = [];

    public IReadOnlyList<PartyMember> Members => _members;

    public PartyRole MyRole { get; set; } = PartyRole.None;

    public bool IsInInstance { get; set; }

    /// <summary>What the party was asked to do, in order.</summary>
    public List<string> Actions { get; } = [];

    /// <summary>Whether the client's own follow works. False makes the bot path instead.</summary>
    public bool CanFollow { get; set; } = true;

    public bool Follow(WoWGuid guid)
    {
        Actions.Add($"Follow({guid})");
        return CanFollow;
    }

    /// <summary>Adds someone to the group.</summary>
    public PartyMember Add(
        ulong guid,
        string name = "Someone",
        float distance = 10f,
        PartyRole role = PartyRole.Damage,
        bool leader = false,
        bool alive = true,
        bool online = true,
        bool inCombat = false,
        double healthPercent = 100d,
        WoWGuid targetGuid = default,
        Vector3 position = default)
    {
        var member = new PartyMember(
            new WoWGuid(guid | ((ulong)WoWGuidType.Player << 48)),
            name,
            position == default ? new Vector3(distance, 0f, 0f) : position,
            distance,
            80,
            healthPercent,
            100d,
            alive,
            online,
            leader,
            inCombat,
            targetGuid,
            role);

        _members.Add(member);
        return member;
    }

    /// <summary>Empties the group.</summary>
    public FakeParty Clear()
    {
        _members.Clear();
        return this;
    }
}
