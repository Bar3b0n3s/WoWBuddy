using WoWBuddy.Common.Scheduling;
using WoWBuddy.Core.Tests;
using WoWBuddy.GameApi.Capabilities;
using WoWBuddy.Live;
using Xunit;

namespace WoWBuddy.Live.Tests;

public sealed class LuaWhispersTests
{
    private const string Field = "\u001F";
    private const string Row = "\u001E";

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static LuaWhispers Build(FakeLua lua) =>
        new(lua, CapabilityProbes.Probe(lua), () => Now);

    [Fact]
    public void ReadsWhatTheClientCollectedWhileTheBotWasBusy()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            string.Join(Field, "Alice", "are you a bot?"),
            string.Join(Field, "Gamemaster", "please respond"));

        IReadOnlyList<Whisper> whispers = Build(lua).Drain();

        Assert.Equal(2, whispers.Count);
        Assert.Equal("Alice", whispers[0].From);
        Assert.Equal("please respond", whispers[1].Text);
        Assert.Equal(Now, whispers[0].At);
    }

    [Fact]
    public void TheListenerIsSetUpBeforeEveryDrain()
    {
        // A UI reload destroys the frame. Setting it up once at attach would leave the bot
        // hearing nothing for the rest of the night while believing it was listening.
        FakeLua lua = FakeLua.Typical335a();
        LuaWhispers whispers = Build(lua);

        whispers.Drain();
        whispers.Drain();

        Assert.Equal(
            2,
            lua.Asked.Count(asked =>
                asked.Contains("CreateFrame(\"Frame\"", StringComparison.Ordinal)));
    }

    [Fact]
    public void ReadingAndClearingHappenInTheSameRoundTrip()
    {
        // Anything read and not cleared is acted on twice; anything cleared and not read is
        // lost. One script leaves no gap for a whisper to arrive in between.
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Join(Field, "Alice", "hello");

        Build(lua).Drain();

        string drain = Assert.Single(
            lua.Asked,
            asked => asked.Contains("__wowbuddy_whispers = \"\"", StringComparison.Ordinal)
                && asked.Contains("__wowbuddy_result = __wowbuddy_whispers", StringComparison.Ordinal));

        Assert.StartsWith("execute: ", drain, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyBufferIsNoWhispersRatherThanOneBlankOne()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Empty;

        Assert.Empty(Build(lua).Drain());
    }

    [Fact]
    public void ARowThatCannotBeReadIsSkippedRatherThanGuessedAt()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            "nonsense-with-no-sender",
            string.Join(Field, "Alice", "hello"));

        Whisper only = Assert.Single(Build(lua).Drain());

        Assert.Equal("Alice", only.From);
    }

    [Fact]
    public void WithoutTheCallItNeedsItHearsNothingAndSaysSo()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Functions.Remove("CreateFrame");
        lua.Answers["__wowbuddy_result"] = string.Join(Field, "Alice", "hello");

        LuaWhispers whispers = Build(lua);

        Assert.Empty(whispers.Drain());
        Assert.Equal(0, whispers.Drains);
    }

    [Fact]
    public void ItNeverSendsAnything()
    {
        // Answering a whisper automatically would be worse than silence: a bot that says "hi"
        // to a game master has still shown exactly what it is.
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Join(Field, "Alice", "hello");

        Build(lua).Drain();

        Assert.DoesNotContain(
            lua.Asked,
            asked => asked.Contains("SendChatMessage", StringComparison.Ordinal));
    }
}
