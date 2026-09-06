namespace WoWBuddy.BotBases.Support;

/// <summary>Something the bot needs to go and do in town.</summary>
public enum Errand
{
    /// <summary>Nothing needed.</summary>
    None,

    /// <summary>Gear is worn and needs repairing.</summary>
    Repair,

    /// <summary>Bags are full and need emptying at a vendor.</summary>
    Sell,

    /// <summary>Keepable items should be posted to an alt.</summary>
    Mail,

    /// <summary>New abilities are available at a trainer.</summary>
    Train,
}

/// <summary>When the bot should interrupt what it is doing to run an errand.</summary>
public sealed record ErrandSettings
{
    /// <summary>Durability below which the bot goes to repair.</summary>
    /// <remarks>
    /// Well above zero. Gear at zero durability gives no stats at all, which turns a working
    /// character into one that dies to everything, and the bot would discover that by dying.
    /// </remarks>
    public double RepairAtDurabilityPercent { get; init; } = 35d;

    /// <summary>Free bag slots at or below which the bot goes to sell.</summary>
    public int SellAtFreeSlots { get; init; } = 2;

    /// <summary>Whether to post keepable items to an alt while at a mailbox.</summary>
    public bool MailEnabled { get; init; }

    /// <summary>Who to post them to.</summary>
    public string MailRecipient { get; init; } = string.Empty;

    /// <summary>Free bag slots at or below which mailing is worth a trip on its own.</summary>
    public int MailAtFreeSlots { get; init; } = 4;

    /// <summary>Whether to visit a trainer when new abilities are available.</summary>
    public bool TrainingEnabled { get; init; } = true;

    /// <summary>How many levels may pass before training is worth a trip.</summary>
    /// <remarks>
    /// Not every level. A trip to town costs several minutes, and the abilities gained at a
    /// single level rarely change how a character fights.
    /// </remarks>
    public int TrainEveryLevels { get; init; } = 2;

    /// <summary>Money below which repairing is not attempted, so the bot keeps a reserve.</summary>
    public long MinimumCopperAfterRepair { get; init; } = 10_000;
}

/// <summary>
/// Decides which errand, if any, is due.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the behaviour tree that carries an errand out, because deciding and doing
/// fail differently. Deciding is arithmetic over inventory state and can be checked exactly;
/// doing involves walking to a town, finding an NPC and clicking things, and mostly fails for
/// reasons that have nothing to do with whether the errand was needed.
/// </para>
/// <para>
/// The order matters. Repair comes first because gear at zero durability makes a character
/// die, which is worse than a full bag; selling comes before mailing because a vendor solves
/// the bag problem and a mailbox only partly does.
/// </para>
/// </remarks>
public sealed class ErrandPlanner
{
    private readonly ErrandSettings _settings;

    public ErrandPlanner(ErrandSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>The level at which the character last visited a trainer.</summary>
    public int LastTrainedLevel { get; set; }

    /// <summary>The settings in force.</summary>
    public ErrandSettings Settings => _settings;

    /// <summary>
    /// The most urgent errand due, or <see cref="Errand.None"/>.
    /// </summary>
    /// <param name="inventory">Bag space, durability and money.</param>
    /// <param name="characterLevel">The character's level.</param>
    /// <param name="hasSellableItems">Whether there is anything a vendor would buy.</param>
    public Errand Next(InventoryState inventory, int characterLevel, bool hasSellableItems)
    {
        // Repair first: gear at zero durability gives no stats, and the bot finds that out
        // by dying rather than by noticing.
        if (inventory.LowestDurabilityPercent <= _settings.RepairAtDurabilityPercent
            && inventory.Copper > _settings.MinimumCopperAfterRepair)
        {
            return Errand.Repair;
        }

        // Selling only helps if there is something to sell. Walking to a vendor with a bag
        // full of quest items achieves nothing and the bot would do it again immediately.
        if (inventory.FreeSlots <= _settings.SellAtFreeSlots && hasSellableItems)
        {
            return Errand.Sell;
        }

        if (_settings.MailEnabled
            && !string.IsNullOrWhiteSpace(_settings.MailRecipient)
            && inventory.FreeSlots <= _settings.MailAtFreeSlots)
        {
            return Errand.Mail;
        }

        if (_settings.TrainingEnabled
            && characterLevel - LastTrainedLevel >= _settings.TrainEveryLevels)
        {
            return Errand.Train;
        }

        return Errand.None;
    }

    /// <summary>
    /// True when the bags are full and there is nothing a vendor would take.
    /// </summary>
    /// <remarks>
    /// The stuck case: a bot in this state cannot loot, and going to a vendor will not fix
    /// it. Something has to give — destroying junk, or stopping and telling the user — and
    /// the caller needs to know it is here rather than looping.
    /// </remarks>
    public bool IsWedged(InventoryState inventory, bool hasSellableItems) =>
        inventory.FreeSlots <= 0 && !hasSellableItems;

    /// <summary>Records that the character has just been trained.</summary>
    public void NoteTrained(int characterLevel) => LastTrainedLevel = characterLevel;
}
