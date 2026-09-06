using WoWBuddy.BotBases.Support;

namespace WoWBuddy.BotBases.Tests;

/// <summary>A vendor and a mailbox, driven by hand.</summary>
public sealed class FakeVendor : IVendorActions
{
    public bool IsMerchantOpen { get; set; }

    public bool IsMailboxOpen { get; set; }

    public bool CanRepairHere { get; set; } = true;

    public List<BagSlot> Bags { get; } = [];

    public IReadOnlyList<BagSlot> BagContents => Bags;

    /// <summary>What the bot asked the vendor to do, in order.</summary>
    public List<string> Actions { get; } = [];

    /// <summary>Whether the character can afford to repair.</summary>
    public bool CanAffordRepair { get; set; } = true;

    /// <summary>Whether a letter can be sent.</summary>
    public bool CanSendMail { get; set; } = true;

    public bool Repair()
    {
        Actions.Add("Repair");
        return CanAffordRepair;
    }

    public bool Sell(BagSlot slot)
    {
        Actions.Add($"Sell({slot.Item.Name})");
        return true;
    }

    public bool Mail(string recipient, IReadOnlyList<BagSlot> items)
    {
        Actions.Add($"Mail({recipient}, {items.Count})");
        return CanSendMail;
    }

    public bool Close()
    {
        Actions.Add("Close");
        IsMerchantOpen = false;
        IsMailboxOpen = false;
        return true;
    }

    /// <summary>Puts a stack in the bags.</summary>
    public FakeVendor With(string name, ItemQuality quality, int sellPrice, int count = 1)
    {
        Bags.Add(new BagSlot(
            0,
            Bags.Count + 1,
            new ItemInfo(
                Bags.Count + 100,
                name,
                quality,
                ItemLevel: 10,
                RequiredLevel: 1,
                ItemClass: "Miscellaneous",
                ItemSubClass: "Junk",
                EquipSlot: string.Empty,
                StackCount: count,
                SellPrice: sellPrice),
            count));

        return this;
    }
}
