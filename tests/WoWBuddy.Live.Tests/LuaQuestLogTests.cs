using WoWBuddy.BotBases.Questing;
using WoWBuddy.Core.Tests;
using WoWBuddy.GameApi.Capabilities;
using WoWBuddy.Live;
using Xunit;

namespace WoWBuddy.Live.Tests;

public sealed class LuaQuestLogTests
{
    private const string Field = "\u001F";
    private const string Row = "\u001E";

    /// <summary>Builds what the client's loop would have returned.</summary>
    private static string Payload(params string[] rows) => string.Join(Row, rows);

    private static string Quest(uint id, string title, bool complete, params (string Text, bool Done)[] objectives)
    {
        string row = $"{id}{Field}{title}{Field}{(complete ? "1" : "0")}";

        foreach ((string text, bool done) in objectives)
        {
            row += $"{Field}{text}{Field}{(done ? "1" : "0")}";
        }

        return row;
    }

    private static (LuaQuestLog Log, FakeLua Lua) Build(string? payload, DateTimeOffset? now = null)
    {
        FakeLua lua = FakeLua.Typical335a();

        if (payload is not null)
        {
            lua.Answers["__wowbuddy_result"] = payload;
        }

        DateTimeOffset clock = now ?? DateTimeOffset.UnixEpoch;

        return (new LuaQuestLog(lua, CapabilityProbes.Probe(lua), () => clock), lua);
    }

    [Fact]
    public void ReadsAWholeLogInOneRoundTrip()
    {
        // Reading a field at a time would be five calls per quest, twenty-five quests, every
        // tick. That is the difference between a bot that stutters and one that does not.
        (LuaQuestLog log, FakeLua lua) = Build(Payload(
            Quest(9001, "First", complete: false, ("Wolves slain: 3/8", false)),
            Quest(9002, "Second", complete: true)));

        IReadOnlyList<QuestLogEntry> entries = log.Entries;

        Assert.Equal(2, entries.Count);
        Assert.Equal(1, log.Reads);

        Assert.Equal(9001u, entries[0].QuestId);
        Assert.Equal("First", entries[0].Title);
        Assert.False(entries[0].IsComplete);
        Assert.Equal("Wolves slain: 3/8", Assert.Single(entries[0].Objectives).Text);

        Assert.True(entries[1].IsComplete);
        Assert.Empty(entries[1].Objectives);

        // One script, one result read. Nothing per quest.
        Assert.Single(lua.Asked, asked => asked == "__wowbuddy_result");
    }

    [Fact]
    public void ObjectivesCarryWhetherTheyAreFinished()
    {
        (LuaQuestLog log, _) = Build(Payload(
            Quest(9001, "First", complete: false, ("Wolves: 8/8", true), ("Bears: 1/8", false))));

        QuestLogEntry entry = Assert.Single(log.Entries);

        Assert.Equal(2, entry.Objectives.Count);
        Assert.True(entry.Objective(1)!.Value.IsDone);
        Assert.False(entry.Objective(2)!.Value.IsDone);
        Assert.Null(entry.Objective(3));
    }

    [Fact]
    public void TheSameQuestionTwiceCostsOneRead()
    {
        // The tree asks about the quest log several times per tick, and the log changes a few
        // times an hour.
        (LuaQuestLog log, _) = Build(Payload(Quest(9001, "First", false)));

        _ = log.Entries;
        _ = log.Entries;
        _ = log.Entries;

        Assert.Equal(1, log.Reads);
    }

    [Fact]
    public void ChangingTheLogClearsTheCacheImmediately()
    {
        // Otherwise accepting a quest would be invisible for the best part of a second, and
        // the questing base would try to accept it again.
        (LuaQuestLog log, _) = Build(Payload(Quest(9001, "First", false)));

        _ = log.Entries;
        Assert.Equal(1, log.Reads);

        log.MarkCompleted(9002);

        _ = log.Entries;
        Assert.Equal(2, log.Reads);
    }

    [Fact]
    public void TheCacheExpires()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = Payload(Quest(9001, "First", false));

        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        LuaQuestLog log = new(lua, CapabilityProbes.Probe(lua), () => now);

        _ = log.Entries;
        Assert.Equal(1, log.Reads);

        now += LuaQuestLog.CacheLifetime + TimeSpan.FromMilliseconds(1);

        _ = log.Entries;
        Assert.Equal(2, log.Reads);
    }

    [Fact]
    public void AnEmptyLogIsNotAFailure()
    {
        (LuaQuestLog log, _) = Build(string.Empty);

        Assert.Empty(log.Entries);
        Assert.Equal(0, log.Dropped);
    }

    [Fact]
    public void ARowItCannotReadIsDroppedRatherThanGuessedAt()
    {
        // A quest with a wrong id has the bot working the wrong objective, which is worse than
        // a quest the bot cannot see at all.
        (LuaQuestLog log, _) = Build(Payload(
            Quest(9001, "Fine", false),
            $"notanumber{Field}Broken{Field}0",
            $"0{Field}Zero{Field}0",
            "missingfields"));

        QuestLogEntry entry = Assert.Single(log.Entries);

        Assert.Equal(9001u, entry.QuestId);
        Assert.Equal(3, log.Dropped);
    }

    [Fact]
    public void AFailedReadIsNotMistakenForAnEmptyLog()
    {
        // Keeping the previous reading would look like nothing had changed. Reading as unknown
        // is what makes the questing base stop rather than work from stale facts.
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = Payload(Quest(9001, "First", false));

        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        LuaQuestLog log = new(lua, CapabilityProbes.Probe(lua), () => now);

        Assert.Single(log.Entries);

        lua.Answers["__wowbuddy_result"] = null;
        now += LuaQuestLog.CacheLifetime * 2;

        Assert.Empty(log.Entries);
    }

    [Fact]
    public void AClientWithoutTheQuestCallsIsNotAskedAtAll()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Functions.Remove("GetQuestLink");
        lua.Answers["__wowbuddy_result"] = Payload(Quest(9001, "First", false));

        LuaQuestLog log = new(lua, CapabilityProbes.Probe(lua), () => DateTimeOffset.UnixEpoch);

        Assert.Empty(log.Entries);
        Assert.Equal(0, log.Reads);
    }

    [Fact]
    public void CompletionIsRememberedRatherThanAsked()
    {
        // 3.3.5a has no call for it: IsQuestFlaggedCompleted arrived in 4.0.
        (LuaQuestLog log, _) = Build(string.Empty);

        Assert.False(log.IsCompleted(9001));

        log.MarkCompleted(9001);

        Assert.True(log.IsCompleted(9001));
    }

    [Fact]
    public void AcceptingWaitsForTheQuestWindowRatherThanCallingItARefusal()
    {
        // A slow window read as a refusal makes the bot walk away from quests it could take.
        (LuaQuestLog log, FakeLua lua) = Build(string.Empty);
        lua.Answers["(QuestFrame ~= nil and QuestFrame:IsVisible()) and true or false"] = "false";

        Assert.Equal(QuestGiverResult.NotReady, log.Accept(9001));
        Assert.DoesNotContain(lua.Asked, asked => asked.StartsWith("execute: AcceptQuest", StringComparison.Ordinal));
    }

    [Fact]
    public void AQuestThatAppearsInTheLogWasAccepted()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["(QuestFrame ~= nil and QuestFrame:IsVisible()) and true or false"] = "true";
        lua.Answers["__wowbuddy_result"] = string.Empty;

        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        LuaQuestLog log = new(lua, CapabilityProbes.Probe(lua), () => now);

        // The client does not report whether accepting worked, so the log is the answer.
        lua.Answers["__wowbuddy_result"] = Payload(Quest(9001, "First", false));

        Assert.Equal(QuestGiverResult.Done, log.Accept(9001));
    }

    [Fact]
    public void AQuestThatDoesNotAppearWasNotOnOffer()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["(QuestFrame ~= nil and QuestFrame:IsVisible()) and true or false"] = "true";
        lua.Answers["__wowbuddy_result"] = string.Empty;

        LuaQuestLog log = new(lua, CapabilityProbes.Probe(lua), () => DateTimeOffset.UnixEpoch);

        Assert.Equal(QuestGiverResult.NotOffered, log.Accept(9001));
    }

    [Fact]
    public void AQuestNotInTheLogCannotBeHandedIn()
    {
        (LuaQuestLog log, _) = Build(string.Empty);

        Assert.Equal(QuestGiverResult.NotOffered, log.TurnIn(9001, 0));
    }

    [Fact]
    public void AQuestThatLeavesTheLogWasHandedIn()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["(QuestFrame ~= nil and QuestFrame:IsVisible()) and true or false"] = "true";
        lua.Answers["__wowbuddy_result"] = Payload(Quest(9001, "First", complete: true));

        LuaQuestLog log = new(lua, CapabilityProbes.Probe(lua), () => DateTimeOffset.UnixEpoch);

        Assert.Single(log.Entries);

        // Handing in is what empties the log, so the fake changes when the bot acts rather
        // than before it does — otherwise the quest would be gone before TurnIn looked.
        lua.OnExecute = script =>
        {
            if (script.Contains("GetQuestReward", StringComparison.Ordinal))
            {
                lua.Answers["__wowbuddy_result"] = string.Empty;
            }
        };

        // The reward index is passed through even when the quest offers no choice; the client
        // ignores it.
        Assert.Equal(QuestGiverResult.Done, log.TurnIn(9001, 2));
        Assert.Contains(lua.Asked, asked => asked.Contains("GetQuestReward(2)", StringComparison.Ordinal));
    }

    [Fact]
    public void AClientWithoutTheQuestGiverCallsWaitsRatherThanClaimingARefusal()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Functions.Remove("AcceptQuest");

        LuaQuestLog log = new(lua, CapabilityProbes.Probe(lua), () => DateTimeOffset.UnixEpoch);

        Assert.Equal(QuestGiverResult.NotReady, log.Accept(9001));
    }

    [Fact]
    public void AbandoningNeedsTheClientsOwnIndexRatherThanThePositionInTheParsedList()
    {
        // The client numbers headers too, so the parsed list and the log do not line up.
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = Payload(Quest(9001, "First", false));

        LuaQuestLog log = new(lua, CapabilityProbes.Probe(lua), () => DateTimeOffset.UnixEpoch);

        Assert.False(log.Abandon(9001));
        Assert.Contains(lua.Asked, asked => asked.Contains("quest:9001:", StringComparison.Ordinal));
    }

    [Fact]
    public void TheReadScriptSkipsHeadersAndFindsIdsInLinks()
    {
        // The two assumptions this class rests on, kept visible: headers have no link, and the
        // id comes out of the hyperlink because GetQuestLogTitle does not return one on 3.3.5a.
        Assert.Contains("if not isHeader then", LuaQuestLog.ReadScript, StringComparison.Ordinal);
        Assert.Contains("quest:(%d+)", LuaQuestLog.ReadScript, StringComparison.Ordinal);
        Assert.Contains("__wowbuddy_result = table.concat", LuaQuestLog.ReadScript, StringComparison.Ordinal);
    }
}
