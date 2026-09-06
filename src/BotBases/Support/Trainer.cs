namespace WoWBuddy.BotBases.Support;

/// <summary>
/// One thing a trainer will teach.
/// </summary>
/// <remarks>
/// The index is the client's own position in the trainer window, which is what learning takes,
/// so it is only good while that window stays open — the same rule as a merchant's shelves.
/// </remarks>
/// <param name="Index">Its place in the trainer's list, one-based, which is what learns it.</param>
/// <param name="Name">What it is called, for logs.</param>
/// <param name="Rank">Which rank of it this is, as the client words it. Often empty.</param>
/// <param name="Cost">What it costs, in copper.</param>
public readonly record struct TrainerService(int Index, string Name, string Rank, long Cost)
{
    /// <summary>What to call it in a log line.</summary>
    public override string ToString() =>
        Rank.Length > 0 ? $"{Name} ({Rank})" : Name;
}

/// <summary>
/// Learning abilities from a trainer.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IVendorActions"/> because a trainer is a different window with
/// different calls, and because the mistake worth preventing is treating them as the same: the
/// errand used to walk to a trainer and then wait for a merchant window that was never going to
/// appear.
/// </para>
/// <para>
/// <b>Only what the trainer says is available appears here.</b> The client already knows what
/// the character's level and money allow, what it has learned, and what it cannot use; asking
/// it is both cheaper and more correct than any spell table this project could ship — and it
/// ships none.
/// </para>
/// </remarks>
public interface ITrainerActions
{
    /// <summary>True while a trainer's window is open.</summary>
    bool IsTrainerOpen { get; }

    /// <summary>
    /// What this trainer will teach the character right now.
    /// </summary>
    /// <remarks>
    /// Empty when no trainer is open, and empty when the client cannot say. Both mean "learn
    /// nothing here", which is the safe answer: the alternative is buying by index into a list
    /// the bot never read.
    /// </remarks>
    IReadOnlyList<TrainerService> Available { get; }

    /// <summary>Learns one. False when the client refused.</summary>
    bool Learn(TrainerService service);

    /// <summary>Closes the trainer's window.</summary>
    bool CloseTrainer();
}
