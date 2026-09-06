using WoWBuddy.BotBases.Support;
using WoWBuddy.Core.Tests;
using WoWBuddy.GameApi.Capabilities;
using WoWBuddy.Live;
using Xunit;

namespace WoWBuddy.Live.Tests;

public sealed class LuaInventoryTests
{
    private const string Field = "\u001F";

    private static string Reading(int free, int total, double durability, long copper) =>
        string.Join(Field, free, total, durability.ToString("F1"), copper);

    private static LuaInventory Build(FakeLua lua, DateTimeOffset? now = null)
    {
        DateTimeOffset clock = now ?? DateTimeOffset.UnixEpoch;
        return new LuaInventory(lua, CapabilityProbes.Probe(lua), () => clock);
    }

    [Fact]
    public void ReadsSpaceWearAndMoneyInOneRoundTrip()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = Reading(free: 12, total: 80, durability: 43.5, copper: 123456);

        LuaInventory inventory = Build(lua);
        InventoryState state = inventory.State;

        Assert.Equal(12, state.FreeSlots);
        Assert.Equal(80, state.TotalSlots);
        Assert.Equal(43.5d, state.LowestDurabilityPercent);
        Assert.Equal(123456L, state.Copper);
        Assert.Equal(0.85d, state.UsedFraction, 2);

        Assert.Equal(1, inventory.Reads);
    }

    [Fact]
    public void TheSameQuestionTwiceCostsOneRead()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = Reading(10, 80, 100d, 0);

        LuaInventory inventory = Build(lua);

        _ = inventory.State;
        _ = inventory.State;

        Assert.Equal(1, inventory.Reads);
    }

    [Fact]
    public void BagsAreTheLongestLivedCacheBecauseTheyChangeLeast()
    {
        Assert.True(LuaInventory.CacheLifetime > LuaQuestLog.CacheLifetime);
        Assert.True(LuaQuestLog.CacheLifetime > LuaPartyState.CacheLifetime);
    }

    [Fact]
    public void AFailedReadIsNotMistakenForFullBags()
    {
        // Sending the character to a vendor on the strength of a failed read would waste a
        // trip; the errand planner treats an unknown bag as nothing to act on.
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = null;

        InventoryState state = Build(lua).State;

        Assert.Equal(0, state.TotalSlots);
        Assert.Equal(0d, state.UsedFraction);
    }

    [Fact]
    public void AMalformedReadingIsNotGuessedAt()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = "12";

        Assert.Equal(0, Build(lua).State.TotalSlots);
    }

    [Fact]
    public void AClientWithoutTheBagCallsIsNotAsked()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Functions.Remove("GetMoney");
        lua.Answers["__wowbuddy_result"] = Reading(10, 80, 100d, 500);

        LuaInventory inventory = Build(lua);

        Assert.Equal(0, inventory.State.TotalSlots);
        Assert.Equal(0, inventory.Reads);
        Assert.Equal(0, inventory.Count(2589));
    }

    [Fact]
    public void CountingAnItemAsksAboutThatItem()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["GetItemCount(2589)"] = "7";

        Assert.Equal(7, Build(lua).Count(2589));
    }

    [Fact]
    public void AnItemTheCharacterDoesNotHaveCountsAsNone()
    {
        FakeLua lua = FakeLua.Typical335a();

        Assert.Equal(0, Build(lua).Count(2589));
    }

    [Fact]
    public void CountsAreNotCachedBecauseLootingChangesThem()
    {
        // Caching per item would go stale the moment something is looted, which is precisely
        // when a collect objective wants the answer.
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["GetItemCount(2589)"] = "1";

        LuaInventory inventory = Build(lua);
        Assert.Equal(1, inventory.Count(2589));

        lua.Answers["GetItemCount(2589)"] = "2";
        Assert.Equal(2, inventory.Count(2589));
    }

    [Fact]
    public void UsingAnItemFindsTheSlotHoldingThatId()
    {
        // A profile names an item by id, and turning an id into a name would need item data
        // this project does not ship.
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers[UseQuery(6948)] = "true";

        Assert.True(Build(lua).Use(6948));
        Assert.Contains(lua.Asked, asked => asked.Contains("item:6948:", StringComparison.Ordinal));
        Assert.Contains(lua.Asked, asked => asked.Contains("UseContainerItem(bag, slot)", StringComparison.Ordinal));
    }

    [Fact]
    public void AnItemTheCharacterIsNotCarryingCannotBeUsed()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers[UseQuery(6948)] = "false";

        Assert.False(Build(lua).Use(6948));
    }

    [Fact]
    public void UsingSomethingClearsTheCacheBecauseItMayBeGone()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = Reading(10, 80, 100d, 0);
        lua.Answers[UseQuery(6948)] = "true";

        LuaInventory inventory = Build(lua);

        _ = inventory.State;
        Assert.Equal(1, inventory.Reads);

        inventory.Use(6948);

        _ = inventory.State;
        Assert.Equal(2, inventory.Reads);
    }

    [Fact]
    public void AClientWithoutTheUseCallRefuses()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Functions.Remove("UseContainerItem");

        Assert.False(Build(lua).Use(6948));
    }

    [Fact]
    public void DurabilityOutsideTheSensibleRangeIsClamped()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = Reading(10, 80, -3d, 0);

        Assert.Equal(0d, Build(lua).State.LowestDurabilityPercent);
    }

    [Fact]
    public void UnreadableDurabilityReadsAsUnwornRatherThanBroken()
    {
        // The safe direction: a bot that wrongly believes its gear is broken walks to a
        // repairer for nothing, over and over.
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Join(Field, "10", "80", "notanumber", "0");

        Assert.Equal(100d, Build(lua).State.LowestDurabilityPercent);
    }

    [Fact]
    public void TheReadScriptSkipsSlotsWithNoDurability()
    {
        // Otherwise every character reads as nothing per cent worn because of its shirt.
        Assert.Contains("if current and maximum and maximum > 0 then", LuaInventory.ReadScript, StringComparison.Ordinal);
        Assert.Contains("for bag = 0, 4 do", LuaInventory.ReadScript, StringComparison.Ordinal);
        Assert.Contains("for slot = 1, 19 do", LuaInventory.ReadScript, StringComparison.Ordinal);
    }

    private static string UseQuery(uint itemId) =>
        "(function() for bag = 0, 4 do "
        + "for slot = 1, (GetContainerNumSlots(bag) or 0) do "
        + "local link = GetContainerItemLink(bag, slot) "
        + $"if link and string.match(link, \"item:{itemId}:\") then "
        + "UseContainerItem(bag, slot) return true end "
        + "end end return false end)()";
}
