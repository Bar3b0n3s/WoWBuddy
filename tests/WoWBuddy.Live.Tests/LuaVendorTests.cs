using WoWBuddy.BotBases.Support;
using WoWBuddy.Core.Tests;
using WoWBuddy.GameApi.Capabilities;
using WoWBuddy.Live;
using Xunit;

namespace WoWBuddy.Live.Tests;

public sealed class LuaVendorTests
{
    private const string Field = "\u001F";
    private const string Row = "\u001E";

    private static string Slot(
        int bag,
        int slot,
        string name,
        int quality,
        string itemClass = "Miscellaneous",
        int count = 1,
        int price = 100) =>
        string.Join(Field, bag, slot, name, quality, 10, 1, itemClass, "Junk", "", count, price);

    private static LuaVendor Build(FakeLua lua, DateTimeOffset? now = null)
    {
        DateTimeOffset clock = now ?? DateTimeOffset.UnixEpoch;
        return new LuaVendor(lua, CapabilityProbes.Probe(lua), () => clock);
    }

    private static FakeLua WithMerchant(bool open = true, bool mailbox = false)
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["(MerchantFrame and MerchantFrame:IsVisible()) and true or false"] = open ? "true" : "false";
        lua.Answers["(MailFrame and MailFrame:IsVisible()) and true or false"] = mailbox ? "true" : "false";
        lua.Answers["CanMerchantRepair() and true or false"] = "true";
        lua.Answers["(select(2, GetRepairAllCost())) and true or false"] = "true";
        return lua;
    }

    [Fact]
    public void ReadsEveryOccupiedSlotInOneRoundTrip()
    {
        FakeLua lua = WithMerchant();
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            Slot(0, 1, "Broken Fang", quality: 0, price: 12),
            Slot(1, 4, "Sturdy Sword", quality: 2, price: 400, count: 1));

        LuaVendor vendor = Build(lua);
        IReadOnlyList<BagSlot> bags = vendor.BagContents;

        Assert.Equal(2, bags.Count);
        Assert.Equal(1, vendor.Reads);

        Assert.Equal("Broken Fang", bags[0].Item.Name);
        Assert.Equal(ItemQuality.Poor, bags[0].Item.Quality);
        Assert.Equal(12, bags[0].Item.SellPrice);
        Assert.Equal(1, bags[1].Bag);
        Assert.Equal(4, bags[1].Slot);
    }

    [Fact]
    public void AFailedReadIsNotMistakenForEmptyBags()
    {
        // An empty reading would have the handler decide there is nothing to sell and walk away
        // from full bags.
        FakeLua lua = WithMerchant();
        lua.Answers["__wowbuddy_result"] = null;

        Assert.Empty(Build(lua).BagContents);
    }

    [Fact]
    public void ARowItCannotReadIsSkipped()
    {
        FakeLua lua = WithMerchant();
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            Slot(0, 1, "Fine", quality: 0),
            "notabag" + Field + "1" + Field + "Broken");

        Assert.Equal("Fine", Assert.Single(Build(lua).BagContents).Item.Name);
    }

    [Fact]
    public void RepairingChecksTheCostFirst()
    {
        // RepairAllItems reports nothing: called without the money it silently does not repair,
        // and the bot would walk away believing it had.
        FakeLua lua = WithMerchant();
        lua.Answers["(select(2, GetRepairAllCost())) and true or false"] = "false";

        Assert.False(Build(lua).Repair());
        Assert.DoesNotContain(lua.Asked, a => a.StartsWith("execute: RepairAllItems", StringComparison.Ordinal));
    }

    [Fact]
    public void RepairingWorksWhenTheMoneyIsThere()
    {
        FakeLua lua = WithMerchant();

        Assert.True(Build(lua).Repair());
        Assert.Contains(lua.Asked, a => a == "execute: RepairAllItems()");
    }

    [Fact]
    public void NothingHappensWithoutTheRightWindowOpen()
    {
        // The same call sells, attaches to a letter, or uses the item depending on what is
        // open. Getting that wrong destroys things.
        FakeLua lua = WithMerchant(open: false);
        lua.Answers["__wowbuddy_result"] = Slot(0, 1, "Broken Fang", quality: 0);

        LuaVendor vendor = Build(lua);
        BagSlot slot = vendor.BagContents[0];

        Assert.False(vendor.Repair());
        Assert.False(vendor.Sell(slot));
        Assert.DoesNotContain(lua.Asked, a => a.StartsWith("execute: UseContainerItem", StringComparison.Ordinal));
    }

    [Fact]
    public void SellingUsesTheSlotTheItemIsIn()
    {
        FakeLua lua = WithMerchant();
        lua.Answers["__wowbuddy_result"] = Slot(2, 7, "Broken Fang", quality: 0);

        LuaVendor vendor = Build(lua);

        Assert.True(vendor.Sell(vendor.BagContents[0]));
        Assert.Contains(lua.Asked, a => a == "execute: UseContainerItem(2, 7)");
    }

    [Fact]
    public void ALetterIsClearedAttachedAndSentInOneScript()
    {
        // Split across round trips, a failure part way would leave items attached to an unsent
        // letter and out of the bags, where nothing would find them again.
        FakeLua lua = WithMerchant(open: false, mailbox: true);
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            Slot(0, 1, "Sturdy Sword", quality: 2),
            Slot(0, 2, "Good Shield", quality: 2));

        LuaVendor vendor = Build(lua);

        Assert.True(vendor.Mail("Bank", vendor.BagContents));

        string sent = Assert.Single(lua.Asked, a => a.StartsWith("execute: ClearSendMail", StringComparison.Ordinal));

        Assert.Contains("UseContainerItem(0, 1)", sent, StringComparison.Ordinal);
        Assert.Contains("UseContainerItem(0, 2)", sent, StringComparison.Ordinal);
        Assert.Contains("SendMail(\"Bank\"", sent, StringComparison.Ordinal);
    }

    [Fact]
    public void ALetterNeedsAMailboxAndSomethingToPutInIt()
    {
        FakeLua lua = WithMerchant(open: false, mailbox: false);

        Assert.False(Build(lua).Mail("Bank", []));
    }

    [Fact]
    public void ARecipientNameWithAQuoteInItCannotEndTheString()
    {
        FakeLua lua = WithMerchant(open: false, mailbox: true);
        lua.Answers["__wowbuddy_result"] = Slot(0, 1, "Sturdy Sword", quality: 2);

        LuaVendor vendor = Build(lua);
        vendor.Mail("Od\"d", vendor.BagContents);

        Assert.Contains(lua.Asked, a => a.Contains("Od\\\"d", StringComparison.Ordinal));
    }

    [Fact]
    public void ClosingClosesBothWindows()
    {
        // Whichever is not open ignores it, and leaving one open stops the character moving.
        FakeLua lua = WithMerchant();

        Assert.True(Build(lua).Close());
        Assert.Contains(lua.Asked, a => a == "execute: CloseMerchant() CloseMail()");
    }

    [Fact]
    public void SellingClearsTheCachedBagsBecauseTheyChanged()
    {
        FakeLua lua = WithMerchant();
        lua.Answers["__wowbuddy_result"] = Slot(0, 1, "Broken Fang", quality: 0);

        LuaVendor vendor = Build(lua);
        BagSlot slot = vendor.BagContents[0];
        Assert.Equal(1, vendor.Reads);

        vendor.Sell(slot);

        _ = vendor.BagContents;
        Assert.Equal(2, vendor.Reads);
    }

    [Fact]
    public void AClientWithoutTheVendorCallsRefusesEverything()
    {
        FakeLua lua = WithMerchant();
        lua.Functions.Remove("SendMail");
        lua.Answers["__wowbuddy_result"] = Slot(0, 1, "Broken Fang", quality: 0);

        LuaVendor vendor = Build(lua);

        Assert.Empty(vendor.BagContents);
        Assert.False(vendor.IsMerchantOpen);
        Assert.False(vendor.Repair());
        Assert.False(vendor.Close());
        Assert.Equal(0, vendor.Reads);
    }

    [Fact]
    public void AnItemTheClientHasNotCachedIsSkippedRatherThanGuessedAt()
    {
        // GetItemInfo returns nothing for an item the client has not seen. An item with an
        // unknown sell price must not be sold, and one with an unknown quality must not be
        // posted, so the script drops the row.
        Assert.Contains("if name and price then", LuaVendor.ReadBagsScript, StringComparison.Ordinal);
    }
}
