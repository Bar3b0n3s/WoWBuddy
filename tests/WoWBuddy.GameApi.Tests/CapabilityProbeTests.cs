using WoWBuddy.Core.Tests;
using WoWBuddy.GameApi.Capabilities;
using Xunit;

namespace WoWBuddy.GameApi.Tests;

public sealed class CapabilityProbeTests
{
    [Fact]
    public void AnOrdinaryClientSupportsEverythingButTheTwoKnownGaps()
    {
        CapabilityReport report = CapabilityProbes.Probe(FakeLua.Typical335a());

        Assert.True(report.NothingUnexpected, report.Describe());

        Assert.True(report.Supports(GameCapability.QuestLog));
        Assert.True(report.Supports(GameCapability.QuestIds));
        Assert.True(report.Supports(GameCapability.Party));
        Assert.True(report.Supports(GameCapability.Battlegrounds));
        Assert.True(report.Supports(GameCapability.TradeSkills));

        // The two this project believes 12340 does not have.
        Assert.False(report.Supports(GameCapability.QuestCompletion));
        Assert.False(report.Supports(GameCapability.PartyRoles));
        Assert.Equal(2, report.KnownGaps.Count);
    }

    [Fact]
    public void AKnownGapIsNotASurprise()
    {
        // The difference matters to a user: a documented limitation they can read about, or a
        // sign that their client is not the build the bot thinks it is.
        CapabilityReport report = CapabilityProbes.Probe(FakeLua.Typical335a());

        Assert.Empty(report.Surprises);
        Assert.Contains("known limitation", report.Explain(GameCapability.QuestCompletion), StringComparison.Ordinal);
        Assert.Contains("walk to some quest givers once", report.Explain(GameCapability.QuestCompletion), StringComparison.Ordinal);
    }

    [Fact]
    public void SomethingMissingThatShouldBeThereIsASurprise()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Functions.Remove("GetQuestLink");

        CapabilityReport report = CapabilityProbes.Probe(lua);

        Assert.False(report.Supports(GameCapability.QuestIds));
        Assert.False(report.NothingUnexpected);

        CapabilityResult surprise = Assert.Single(report.Surprises);
        Assert.Equal(GameCapability.QuestIds, surprise.Capability);
        Assert.Equal("GetQuestLink", Assert.Single(surprise.Missing));

        Assert.Contains("the offset table is suspect", report.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void ACapabilityNeedsEveryFunctionItUses()
    {
        // Half a feature is not a feature: taking quests without being able to hand them in
        // leaves a character with a full log and nothing to do.
        FakeLua lua = FakeLua.Typical335a();
        lua.Functions.Remove("CompleteQuest");

        CapabilityReport report = CapabilityProbes.Probe(lua);

        Assert.False(report.Supports(GameCapability.QuestGiver));
        Assert.Contains("CompleteQuest", report.Explain(GameCapability.QuestGiver), StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutTheRoundTripNothingIsClaimedEitherWay()
    {
        // Reporting ten capabilities as missing would be misleading: they are unknown, not
        // absent, and the reason is the round trip rather than the client.
        FakeLua lua = new()
        {
            CanReadResults = false,
            ResultsUnavailableReason = "The Lua self-test has not run yet.",
        };

        CapabilityReport report = CapabilityProbes.Probe(lua);

        Assert.Single(report.Results);
        Assert.False(report.Supports(GameCapability.LuaResults));
        Assert.False(report.Supports(GameCapability.QuestLog));
        Assert.Contains("never checked", report.Explain(GameCapability.QuestLog), StringComparison.Ordinal);
    }

    [Fact]
    public void ACapabilityNeverAskedAboutCountsAsUnavailable()
    {
        // Assuming otherwise would make a feature work by accident on a client never checked.
        CapabilityReport report = new();

        Assert.False(report.Supports(GameCapability.Party));
    }

    [Fact]
    public void AGlobalThatIsNotAFunctionDoesNotCount()
    {
        // On a heavily modified server a global can exist and not be callable.
        FakeLua lua = FakeLua.Typical335a();
        lua.Functions.Remove("GetItemCount");
        lua.Answers["type(GetItemCount)"] = "table";

        CapabilityReport report = CapabilityProbes.Probe(lua);

        Assert.False(report.Supports(GameCapability.Inventory));
    }

    [Fact]
    public void ItAsksAboutExistenceRatherThanAboutState()
    {
        // A party call returns nothing when solo and a quest call returns nothing with an empty
        // log, so asking what they return would switch features off for the wrong reason.
        FakeLua lua = FakeLua.Typical335a();

        CapabilityProbes.Probe(lua);

        Assert.All(
            lua.Asked,
            asked => Assert.StartsWith("type(", asked, StringComparison.Ordinal));
    }

    [Fact]
    public void TheReportSaysWhatBreaksAndWhatItWasFor()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Functions.Remove("GetBattlefieldStatus");

        string description = CapabilityProbes.Probe(lua).Describe();

        Assert.Contains("queueing for and leaving a battleground", description, StringComparison.Ordinal);
        Assert.Contains("cannot queue", description, StringComparison.Ordinal);
        Assert.Contains("GetBattlefieldStatus", description, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryCapabilityTheBotNamesIsActuallyProbed()
    {
        // A capability in the enum with no probe would silently read as unavailable forever.
        CapabilityReport report = CapabilityProbes.Probe(FakeLua.Typical335a());

        foreach (GameCapability capability in Enum.GetValues<GameCapability>())
        {
            Assert.Contains(report.Results, result => result.Capability == capability);
        }
    }
}
