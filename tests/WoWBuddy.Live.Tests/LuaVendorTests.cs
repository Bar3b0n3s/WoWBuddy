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

    // ---- buying ---------------------------------------------------------------------------

    private static string Shelf(
        int index,
        uint itemId,
        string name,
        long price = 100,
        int stackSize = 1,
        int available = MerchantItem.Unlimited) =>
        string.Join(Field, index, itemId, name, price, stackSize, available);

    [Fact]
    public void ReadsTheMerchantsShelvesInOneRoundTrip()
    {
        FakeLua lua = WithMerchant();
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            Shelf(1, 2880, "Weak Flux", price: 10, stackSize: 1),
            Shelf(2, 2320, "Coarse Thread", price: 100, stackSize: 5, available: 3));

        LuaVendor vendor = Build(lua);
        IReadOnlyList<MerchantItem> stock = vendor.MerchantStock;

        Assert.Equal(2, stock.Count);

        Assert.Equal(2880u, stock[0].ItemId);
        Assert.True(stock[0].InStock);

        // A limited shelf can only supply what it has, however much is wanted.
        Assert.Equal(15, stock[1].Obtainable(100));
        Assert.Equal(1, vendor.StockReads);
    }

    [Fact]
    public void WithNoMerchantOpenTheShelvesAreEmptyWithoutAsking()
    {
        // The client can answer a stale item count for a moment after the window closes, and
        // buying by index into a list belonging to a shop the character walked away from buys
        // the wrong thing.
        FakeLua lua = WithMerchant(open: false);
        lua.Answers["__wowbuddy_result"] = Shelf(1, 2880, "Weak Flux");

        LuaVendor vendor = Build(lua);

        Assert.Empty(vendor.MerchantStock);
        Assert.Equal(0, vendor.StockReads);
    }

    [Fact]
    public void BuyingAsksTheClientForTheShelfsOwnIndex()
    {
        FakeLua lua = WithMerchant();
        lua.Answers["__wowbuddy_result"] = Shelf(4, 2880, "Weak Flux", price: 10);

        LuaVendor vendor = Build(lua);
        MerchantItem flux = Assert.Single(vendor.MerchantStock);

        Assert.True(vendor.Buy(flux, 20));
        Assert.Contains("execute: BuyMerchantItem(4, 20)", lua.Asked);
    }

    [Fact]
    public void NothingIsBoughtWithTheWindowShut()
    {
        // The same guard as selling, and for the same reason: what a client call does depends
        // on what happens to be open.
        FakeLua lua = WithMerchant(open: false);
        LuaVendor vendor = Build(lua);

        Assert.False(vendor.Buy(new MerchantItem(1, 2880, "Weak Flux", 10, 1, -1), 5));
        Assert.DoesNotContain(lua.Asked, asked => asked.Contains("execute: BuyMerchantItem", StringComparison.Ordinal));
    }

    [Fact]
    public void BuyingNothingIsNotAPurchase()
    {
        FakeLua lua = WithMerchant();
        LuaVendor vendor = Build(lua);

        Assert.False(vendor.Buy(new MerchantItem(1, 2880, "Weak Flux", 10, 1, -1), 0));
        Assert.DoesNotContain(lua.Asked, asked => asked.Contains("execute: BuyMerchantItem", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutTheBuyingCallsNothingIsReadOrBought()
    {
        FakeLua lua = WithMerchant();
        lua.Functions.Remove("BuyMerchantItem");
        lua.Answers["__wowbuddy_result"] = Shelf(1, 2880, "Weak Flux");

        LuaVendor vendor = Build(lua);

        Assert.Empty(vendor.MerchantStock);
        Assert.False(vendor.Buy(new MerchantItem(1, 2880, "Weak Flux", 10, 1, -1), 5));
    }

    [Fact]
    public void BuyingForgetsBothTheBagsAndTheShelves()
    {
        // A purchase changes the bags, the shelves, and a limited-stock vendor's count with
        // them, so every reading taken a moment ago is now wrong.
        FakeLua lua = WithMerchant();
        lua.Answers["__wowbuddy_result"] = Shelf(1, 2880, "Weak Flux", price: 10);

        LuaVendor vendor = Build(lua);
        MerchantItem flux = Assert.Single(vendor.MerchantStock);

        vendor.Buy(flux, 5);

        Assert.Single(vendor.MerchantStock);
        Assert.Equal(2, vendor.StockReads);
    }

    [Fact]
    public void AShelfRowThatCannotBeReadIsSkippedRatherThanGuessedAt()
    {
        FakeLua lua = WithMerchant();
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            "nonsense",
            Shelf(2, 2880, "Weak Flux"));

        MerchantItem flux = Assert.Single(Build(lua).MerchantStock);

        Assert.Equal(2880u, flux.ItemId);
    }
}
