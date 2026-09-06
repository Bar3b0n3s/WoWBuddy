using WoWBuddy.BotBases.Battlegrounds;

namespace WoWBuddy.BotBases.Tests;

/// <summary>A battleground queue set by hand.</summary>
public sealed class FakeBattlegrounds : IBattlegroundActions
{
    public BattlegroundStatus Status { get; set; } = BattlegroundStatus.None;

    public bool IsInside { get; set; }

    public bool HasStarted { get; set; }

    public bool IsFinished { get; set; }

    public string Name { get; set; } = "Warsong Gulch";

    /// <summary>What the bot asked the queue to do, in order.</summary>
    public List<string> Actions { get; } = [];

    /// <summary>Whether queueing succeeds.</summary>
    public bool CanQueue { get; set; } = true;

    public bool Queue(string name)
    {
        Actions.Add($"Queue({name})");

        if (!CanQueue)
        {
            return false;
        }

        Status = BattlegroundStatus.Queued;
        return true;
    }

    public bool AcceptInvitation()
    {
        Actions.Add("AcceptInvitation");
        Status = BattlegroundStatus.Active;
        IsInside = true;
        return true;
    }

    public bool Leave()
    {
        Actions.Add("Leave");
        Status = BattlegroundStatus.None;
        IsInside = false;
        IsFinished = false;
        HasStarted = false;
        return true;
    }

    /// <summary>Puts the character inside a battleground that has begun.</summary>
    public FakeBattlegrounds Playing()
    {
        Status = BattlegroundStatus.Active;
        IsInside = true;
        HasStarted = true;
        IsFinished = false;
        return this;
    }
}
