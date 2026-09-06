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

    /// <summary>What this vendor is selling.</summary>
    public List<MerchantItem> Stock { get; } = [];

    public IReadOnlyList<MerchantItem> MerchantStock => IsMerchantOpen ? Stock : [];

    /// <summary>What the bot asked the vendor to do, in order.</summary>
    public List<string> Actions { get; } = [];

    /// <summary>Whether the character can afford to repair.</summary>
    public bool CanAffordRepair { get; set; } = true;

    /// <summary>Whether a letter can be sent.</summary>
    public bool CanSendMail { get; set; } = true;

    /// <summary>Whether the vendor accepts a purchase.</summary>
    public bool CanBuy { get; set; } = true;

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

    public bool Buy(MerchantItem item, int count)
    {
        Actions.Add($"Buy({item.Name}, {count})");
        return CanBuy;
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

    /// <summary>Puts something on the vendor's shelves.</summary>
    public FakeVendor Selling(
        uint itemId,
        string name,
        long price = 10,
        int stackSize = 1,
        int available = MerchantItem.Unlimited)
    {
        Stock.Add(new MerchantItem(Stock.Count + 1, itemId, name, price, stackSize, available));
        return this;
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
