using WoWBuddy.BotBases.Support;

namespace WoWBuddy.BotBases.Tests;

/// <summary>A mount, driven by hand.</summary>
public sealed class FakeTravel : ITravel
{
    public bool IsMounted { get; set; }

    public bool CanMount { get; set; } = true;

    /// <summary>What the bot asked for, in order.</summary>
    public List<string> Actions { get; } = [];

    public bool Mount()
    {
        Actions.Add("Mount");
        IsMounted = CanMount;
        return CanMount;
    }

    public bool Dismount()
    {
        Actions.Add("Dismount");
        IsMounted = false;
        return true;
    }
}
