using WoWBuddy.BotBases.Support;

namespace WoWBuddy.BotBases.Tests;

/// <summary>A trainer driven by hand.</summary>
public sealed class FakeTrainer : ITrainerActions
{
    public bool IsTrainerOpen { get; set; }

    /// <summary>What this trainer will teach.</summary>
    public List<TrainerService> Services { get; } = [];

    public IReadOnlyList<TrainerService> Available => IsTrainerOpen ? Services : [];

    /// <summary>What the bot asked the trainer to do, in order.</summary>
    public List<string> Actions { get; } = [];

    /// <summary>Whether the trainer accepts a purchase.</summary>
    public bool CanLearn { get; set; } = true;

    public bool Learn(TrainerService service)
    {
        Actions.Add($"Learn({service.Name})");

        if (!CanLearn)
        {
            return false;
        }

        // As the client would: what has been learned is no longer on offer, and everything
        // after it moves up. A fake that did not do this would let a test pass while the real
        // thing bought the same rank thirty times.
        Services.Remove(service);
        return true;
    }

    public bool CloseTrainer()
    {
        Actions.Add("CloseTrainer");
        IsTrainerOpen = false;
        return true;
    }

    /// <summary>Puts something on the trainer's list.</summary>
    public FakeTrainer Teaching(string name, long cost, string rank = "")
    {
        Services.Add(new TrainerService(Services.Count + 1, name, rank, cost));
        return this;
    }
}
