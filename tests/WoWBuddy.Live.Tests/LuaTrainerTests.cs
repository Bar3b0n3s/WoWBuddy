using WoWBuddy.BotBases.Support;
using WoWBuddy.Core.Tests;
using WoWBuddy.GameApi.Capabilities;
using WoWBuddy.Live;
using Xunit;

namespace WoWBuddy.Live.Tests;

public sealed class LuaTrainerTests
{
    private const string Field = "\u001F";
    private const string Row = "\u001E";

    private static string Service(int index, string name, string rank, long cost) =>
        string.Join(Field, index, name, rank, cost);

    private static LuaTrainer Build(FakeLua lua, DateTimeOffset? now = null)
    {
        DateTimeOffset clock = now ?? DateTimeOffset.UnixEpoch;
        return new LuaTrainer(lua, CapabilityProbes.Probe(lua), () => clock);
    }

    private static FakeLua WithTrainer(bool open = true)
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["(ClassTrainerFrame and ClassTrainerFrame:IsVisible()) and true or false"] =
            open ? "true" : "false";
        return lua;
    }

    [Fact]
    public void ReadsWhatTheTrainerWillTeachInOneRoundTrip()
    {
        FakeLua lua = WithTrainer();
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            Service(1, "Heroic Strike", "Rank 2", 5000),
            Service(4, "Rend", string.Empty, 8000));

        LuaTrainer trainer = Build(lua);
        IReadOnlyList<TrainerService> services = trainer.Available;

        Assert.Equal(2, services.Count);
        Assert.Equal("Heroic Strike (Rank 2)", services[0].ToString());
        Assert.Equal(5000L, services[0].Cost);

        // The client's own index, not a position in the list read back: the second row is the
        // trainer's fourth service, and learning by position would learn the wrong thing.
        Assert.Equal(4, services[1].Index);
        Assert.Equal(1, trainer.Reads);
    }

    [Fact]
    public void TheListIsFilteredToWhatIsAvailableBeforeItIsRead()
    {
        // A trainer's window also holds what the character already knows and what it is too low
        // to learn. The filter is a display setting the player may have changed, so leaving it
        // as found would mean reading one list and learning from another.
        FakeLua lua = WithTrainer();
        lua.Answers["__wowbuddy_result"] = Service(1, "Heroic Strike", string.Empty, 5000);

        Build(lua).Available.ToList();

        Assert.Contains(
            lua.Asked,
            asked => asked.Contains("SetTrainerServiceTypeFilter(\"available\", 1)", StringComparison.Ordinal));
    }

    [Fact]
    public void WithNoTrainerOpenThereIsNothingToLearnAndNothingIsAsked()
    {
        FakeLua lua = WithTrainer(open: false);
        lua.Answers["__wowbuddy_result"] = Service(1, "Heroic Strike", string.Empty, 5000);

        LuaTrainer trainer = Build(lua);

        Assert.Empty(trainer.Available);
        Assert.Equal(0, trainer.Reads);
    }

    [Fact]
    public void LearningAsksTheClientForTheServicesOwnIndex()
    {
        FakeLua lua = WithTrainer();
        lua.Answers["__wowbuddy_result"] = Service(4, "Rend", string.Empty, 8000);

        LuaTrainer trainer = Build(lua);
        TrainerService rend = Assert.Single(trainer.Available);

        Assert.True(trainer.Learn(rend));
        Assert.Contains("execute: BuyTrainerService(4)", lua.Asked);
    }

    [Fact]
    public void NothingIsLearnedWithTheWindowShut()
    {
        FakeLua lua = WithTrainer(open: false);

        Assert.False(Build(lua).Learn(new TrainerService(1, "Rend", string.Empty, 8000)));
        Assert.DoesNotContain(
            lua.Asked,
            asked => asked.Contains("execute: BuyTrainerService", StringComparison.Ordinal));
    }

    [Fact]
    public void LearningForgetsTheListBecauseEveryIndexAfterItHasMoved()
    {
        FakeLua lua = WithTrainer();
        lua.Answers["__wowbuddy_result"] = Service(1, "Heroic Strike", string.Empty, 5000);

        LuaTrainer trainer = Build(lua);
        TrainerService first = Assert.Single(trainer.Available);

        trainer.Learn(first);

        Assert.Single(trainer.Available);
        Assert.Equal(2, trainer.Reads);
    }

    [Fact]
    public void WithoutTheTrainerCallsNothingIsReadOrLearned()
    {
        FakeLua lua = WithTrainer();
        lua.Functions.Remove("BuyTrainerService");
        lua.Answers["__wowbuddy_result"] = Service(1, "Heroic Strike", string.Empty, 5000);

        LuaTrainer trainer = Build(lua);

        Assert.Empty(trainer.Available);
        Assert.False(trainer.Learn(new TrainerService(1, "Heroic Strike", string.Empty, 5000)));
    }

    [Fact]
    public void ARowThatCannotBeReadIsSkippedRatherThanGuessedAt()
    {
        FakeLua lua = WithTrainer();
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            "nonsense",
            Service(2, "Rend", string.Empty, 8000));

        TrainerService rend = Assert.Single(Build(lua).Available);

        Assert.Equal("Rend", rend.Name);
    }

    [Fact]
    public void AnUnreadableAnswerIsUnknownRatherThanNothingToLearn()
    {
        FakeLua lua = WithTrainer();
        lua.Answers["__wowbuddy_result"] = null;

        Assert.Empty(Build(lua).Available);
    }
}
