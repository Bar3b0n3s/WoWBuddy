namespace WoWBuddy.BotBases.Support;

/// <summary>
/// Something an open merchant is selling.
/// </summary>
/// <remarks>
/// The index is the client's own position in the merchant window, which is what buying takes,
/// so it is only good while that window stays open. Everything else is here so the decision of
/// what to buy can be made without asking the client again.
/// </remarks>
/// <param name="Index">Its place in the merchant window, one-based, which is what buys it.</param>
/// <param name="ItemId">What it is.</param>
/// <param name="Name">What it is called, for logs.</param>
/// <param name="Price">What one purchase costs, in copper.</param>
/// <param name="StackSize">How many items one purchase gives.</param>
/// <param name="Available">
/// How many are left, or -1 when the vendor has an endless supply. Zero means sold out, which
/// happens with limited-stock vendors and is worth walking away from rather than clicking at.
/// </param>
public readonly record struct MerchantItem(
    int Index,
    uint ItemId,
    string Name,
    long Price,
    int StackSize,
    int Available)
{
    /// <summary>The value the client uses for an endless supply.</summary>
    public const int Unlimited = -1;

    /// <summary>True when there is any left to buy.</summary>
    public bool InStock => Available != 0;

    /// <summary>How many items are actually obtainable here.</summary>
    public int Obtainable(int wanted) =>
        Available < 0 ? wanted : Math.Min(wanted, Available * Math.Max(1, StackSize));

    public override string ToString() => $"{Name} x{StackSize} for {Price}c";
}

/// <summary>
/// Doing business with a vendor or a mailbox.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the rest of <see cref="IBotState"/> because it is only reachable while a
/// window is open, and because everything in it is a verb rather than a fact. Only the errand
/// handler and the supply run use it.
/// </para>
/// <para>
/// Every method assumes the relevant window is open and returns false when it is not. Checking
/// first is the caller's job, and the caller does it, because "the window has not opened yet"
/// and "the vendor refused" want different answers: one is worth waiting for.
/// </para>
/// </remarks>
public interface IVendorActions
{
    /// <summary>True while a merchant window is open.</summary>
    bool IsMerchantOpen { get; }

    /// <summary>True while a mailbox is open.</summary>
    bool IsMailboxOpen { get; }

    /// <summary>True when the open merchant can repair.</summary>
    /// <remarks>
    /// Not every vendor does, and walking to one that cannot is a wasted trip the profile
    /// should have prevented. Checked anyway, because a profile can be wrong.
    /// </remarks>
    bool CanRepairHere { get; }

    /// <summary>What is in the character's bags, with enough detail to decide what to sell.</summary>
    IReadOnlyList<BagSlot> BagContents { get; }

    /// <summary>
    /// What the open merchant is selling.
    /// </summary>
    /// <remarks>
    /// Empty when no merchant is open, and empty when the client cannot say. Both mean "do not
    /// buy anything here", which is the safe answer: the alternative is buying by index into a
    /// list the bot never read.
    /// </remarks>
    IReadOnlyList<MerchantItem> MerchantStock { get; }

    /// <summary>Repairs everything, and reports whether the character could afford it.</summary>
    bool Repair();

    /// <summary>Sells one stack.</summary>
    bool Sell(BagSlot slot);

    /// <summary>
    /// Buys <paramref name="count"/> of something the open merchant sells.
    /// </summary>
    /// <remarks>
    /// Returns whether the purchase was asked for, not whether it arrived: the client answers
    /// nothing, and the bags catch up a moment later. The caller finds out by counting again.
    /// </remarks>
    bool Buy(MerchantItem item, int count);

    /// <summary>Posts items to another character.</summary>
    /// <remarks>
    /// One letter, up to the twelve attachments a letter holds. The caller batches; this does
    /// not, because a partial send is far easier to reason about than a partial batch.
    /// </remarks>
    bool Mail(string recipient, IReadOnlyList<BagSlot> items);

    /// <summary>Closes whatever window is open.</summary>
    bool Close();
}
