using WoWBuddy.BotBases.Support;

namespace WoWBuddy.BotBases.Tests;

/// <summary>A talent tree driven by hand.</summary>
public sealed class FakeTalents : ITalents
{
    public int UnspentPoints { get; set; }

    /// <summary>What the bot put points into, in order.</summary>
    public List<TalentPick> Learned { get; } = [];

    /// <summary>Picks the client will refuse, as it does when a prerequisite is not met.</summary>
    public HashSet<TalentPick> Refuse { get; } = [];

    public bool Learn(TalentPick pick)
    {
        if (Refuse.Contains(pick))
        {
            return false;
        }

        Learned.Add(pick);
        UnspentPoints = Math.Max(0, UnspentPoints - 1);
        return true;
    }
}
