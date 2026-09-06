using WoWBuddy.BotBases.Battlegrounds;
using WoWBuddy.Core.Tests;
using WoWBuddy.GameApi.Capabilities;
using WoWBuddy.Live;
using Xunit;

namespace WoWBuddy.Live.Tests;

public sealed class LuaBattlegroundsTests
{
    private const string Field = "\u001F";

    private static string State(string status, string name = "Warsong Gulch", long runTime = 0, bool finished = false) =>
        string.Join(Field, status, name, runTime.ToString(), finished ? "1" : "0");

    private static LuaBattlegrounds Build(FakeLua lua, DateTimeOffset? now = null)
    {
        DateTimeOffset clock = now ?? DateTimeOffset.UnixEpoch;
        return new LuaBattlegrounds(lua, CapabilityProbes.Probe(lua), () => clock);
    }

    [Theory]
    [InlineData("none", BattlegroundStatus.None)]
    [InlineData("queued", BattlegroundStatus.Queued)]
    [InlineData("confirm", BattlegroundStatus.Confirmed)]
    [InlineData("active", BattlegroundStatus.Active)]
    [InlineData("ACTIVE", BattlegroundStatus.Active)]
    [InlineData("something else", BattlegroundStatus.None)]
    public void TheFourStringsTheClientUsesAreUnderstood(string text, BattlegroundStatus expected)
    {
        // Anything unrecognised is treated as not queued, which is the conservative answer: the
        // bot will try to join, and the client refuses if it already has.
        Assert.Equal(expected, LuaBattlegrounds.ParseStatus(text));
    }

    [Fact]
    public void ReadsTheWholeStateInOneRoundTrip()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = State("active", "Arathi Basin", runTime: 65000);

        LuaBattlegrounds battlegrounds = Build(lua);

        Assert.Equal(BattlegroundStatus.Active, battlegrounds.Status);
        Assert.True(battlegrounds.IsInside);
        Assert.True(battlegrounds.HasStarted);
        Assert.False(battlegrounds.IsFinished);
        Assert.Equal("Arathi Basin", battlegrounds.Name);

        Assert.Equal(1, battlegrounds.Reads);
    }

    [Fact]
    public void BehindTheGatesIsInsideButNotStarted()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = State("active", runTime: 0);

        LuaBattlegrounds battlegrounds = Build(lua);

        Assert.True(battlegrounds.IsInside);
        Assert.False(battlegrounds.HasStarted);
    }

    [Fact]
    public void QueuedIsNotInside()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = State("queued");

        LuaBattlegrounds battlegrounds = Build(lua);

        Assert.Equal(BattlegroundStatus.Queued, battlegrounds.Status);
        Assert.False(battlegrounds.IsInside);
        Assert.False(battlegrounds.HasStarted);
    }

    [Fact]
    public void AWinnerMeansTheMatchIsOver()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = State("active", runTime: 900000, finished: true);

        Assert.True(Build(lua).IsFinished);
    }

    [Fact]
    public void QueueingIsTwoCallsWithTwoDifferentIndices()
    {
        // The first takes the battleground's place in the list, the second an instance number
        // where zero means whichever is available. They are not interchangeable.
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = State("none");
        lua.Answers[BattlegroundIndexQuery("Warsong Gulch")] = "2";

        Assert.True(Build(lua).Queue("Warsong Gulch"));

        Assert.Contains(lua.Asked, a => a == "execute: RequestBattlegroundInstanceInfo(2)");
        Assert.Contains(lua.Asked, a => a == "execute: JoinBattlefield(0)");
    }

    [Fact]
    public void ABattlegroundTheClientDoesNotOfferIsRefusedWithAReason()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = State("none");
        lua.Answers[BattlegroundIndexQuery("Nagrand Arena")] = "0";

        Assert.False(Build(lua).Queue("Nagrand Arena"));
        Assert.DoesNotContain(lua.Asked, a => a.StartsWith("execute: JoinBattlefield", StringComparison.Ordinal));
    }

    [Fact]
    public void TheInvitationIsOnlyAcceptedWhenThereIsOne()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = State("queued");

        LuaBattlegrounds battlegrounds = Build(lua);

        Assert.False(battlegrounds.AcceptInvitation());
        Assert.DoesNotContain(lua.Asked, a => a.StartsWith("execute: AcceptBattlefieldPort", StringComparison.Ordinal));
    }

    [Fact]
    public void AWaitingInvitationIsAccepted()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = State("confirm");

        Assert.True(Build(lua).AcceptInvitation());
        Assert.Contains(lua.Asked, a => a == "execute: AcceptBattlefieldPort(1, 1)");
    }

    [Fact]
    public void LeavingIsOneCall()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = State("active", runTime: 1, finished: true);

        Assert.True(Build(lua).Leave());
        Assert.Contains(lua.Asked, a => a == "execute: LeaveBattlefield()");
    }

    [Fact]
    public void AClientWithoutTheBattlegroundCallsRefusesEverything()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Functions.Remove("JoinBattlefield");
        lua.Answers["__wowbuddy_result"] = State("active", runTime: 1);

        LuaBattlegrounds battlegrounds = Build(lua);

        Assert.Equal(BattlegroundStatus.None, battlegrounds.Status);
        Assert.False(battlegrounds.Queue("Warsong Gulch"));
        Assert.False(battlegrounds.AcceptInvitation());
        Assert.False(battlegrounds.Leave());
        Assert.Equal(0, battlegrounds.Reads);
    }

    [Fact]
    public void AFailedReadIsNotMistakenForBeingInAMatch()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = null;

        LuaBattlegrounds battlegrounds = Build(lua);

        Assert.Equal(BattlegroundStatus.None, battlegrounds.Status);
        Assert.False(battlegrounds.IsInside);
        Assert.False(battlegrounds.IsFinished);
    }

    [Fact]
    public void AMalformedReadingIsNotGuessedAt()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = "active";

        Assert.Equal(BattlegroundStatus.None, Build(lua).Status);
    }

    [Fact]
    public void ActingOnTheQueueClearsTheCacheImmediately()
    {
        // Otherwise accepting a port would leave the bot believing it was still queued, and it
        // would try to accept again.
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = State("confirm");

        LuaBattlegrounds battlegrounds = Build(lua);

        Assert.True(battlegrounds.AcceptInvitation());
        Assert.Equal(1, battlegrounds.Reads);

        _ = battlegrounds.Status;

        Assert.Equal(2, battlegrounds.Reads);
    }

    [Fact]
    public void TheSameQuestionTwiceCostsOneRead()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = State("active", runTime: 1);

        LuaBattlegrounds battlegrounds = Build(lua);

        _ = battlegrounds.Status;
        _ = battlegrounds.IsInside;
        _ = battlegrounds.HasStarted;
        _ = battlegrounds.Name;

        Assert.Equal(1, battlegrounds.Reads);
    }

    private static string BattlegroundIndexQuery(string name) =>
        "(function() for i = 1, GetNumBattlegroundTypes() do "
        + "local localised = GetBattlegroundInfo(i) "
        + $"if localised and string.lower(localised) == string.lower(\"{name}\") then return i end "
        + "end return 0 end)()";
}
