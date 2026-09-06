namespace WoWBuddy.BotBases.Support;

/// <summary>
/// Doing business with a vendor or a mailbox.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the rest of <see cref="IBotState"/> because it is only reachable while a
/// window is open, and because everything in it is a verb rather than a fact. The errand
/// handler is the only thing that uses it.
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

    /// <summary>Repairs everything, and reports whether the character could afford it.</summary>
    bool Repair();

    /// <summary>Sells one stack.</summary>
    bool Sell(BagSlot slot);

    /// <summary>Posts items to another character.</summary>
    /// <remarks>
    /// One letter, up to the twelve attachments a letter holds. The caller batches; this does
    /// not, because a partial send is far easier to reason about than a partial batch.
    /// </remarks>
    bool Mail(string recipient, IReadOnlyList<BagSlot> items);

    /// <summary>Closes whatever window is open.</summary>
    bool Close();
}
