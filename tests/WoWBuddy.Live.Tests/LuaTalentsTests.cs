using WoWBuddy.BotBases.Support;
using WoWBuddy.Core.Tests;
using WoWBuddy.GameApi.Capabilities;
using WoWBuddy.Live;
using Xunit;

namespace WoWBuddy.Live.Tests;

public sealed class LuaTalentsTests
{
    private static LuaTalents Build(FakeLua lua) => new(lua, CapabilityProbes.Probe(lua));

    [Fact]
    public void ReadsHowManyPointsAreGoingSpare()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["GetUnspentTalentPoints()"] = "4";

        Assert.Equal(4, Build(lua).UnspentPoints);
    }

    [Fact]
    public void ARankThatMovedMeansThePointWentIn()
    {
        // The client reports nothing useful about whether a point went in, so the rank is read
        // before and after.
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["select(5, GetTalentInfo(1, 3))"] = "0";

        lua.OnExecute = script =>
        {
            if (script.Contains("LearnTalent(1, 3)", StringComparison.Ordinal))
            {
                lua.Answers["select(5, GetTalentInfo(1, 3))"] = "1";
            }
        };

        Assert.True(Build(lua).Learn(new TalentPick(1, 3)));
        Assert.Contains(lua.Asked, a => a == "execute: LearnTalent(1, 3)");
    }

    [Fact]
    public void ARankThatDidNotMoveMeansItWasRefused()
    {
        // Almost always a prerequisite the build did not account for.
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["select(5, GetTalentInfo(2, 5))"] = "0";

        Assert.False(Build(lua).Learn(new TalentPick(2, 5)));
    }

    [Fact]
    public void ATalentTheCharacterDoesNotHaveIsNotAttempted()
    {
        // The build is for a different class or specialisation.
        FakeLua lua = FakeLua.Typical335a();

        Assert.False(Build(lua).Learn(new TalentPick(3, 40)));
        Assert.DoesNotContain(lua.Asked, a => a.StartsWith("execute: LearnTalent", StringComparison.Ordinal));
    }

    [Fact]
    public void AnImplausiblePickIsRefusedWithoutAskingTheClient()
    {
        FakeLua lua = FakeLua.Typical335a();

        Assert.False(Build(lua).Learn(new TalentPick(9, 1)));
        Assert.DoesNotContain(lua.Asked, a => a.StartsWith("execute: LearnTalent", StringComparison.Ordinal));
    }

    [Fact]
    public void AClientWithoutTheTalentCallsLeavesPointsUnspent()
    {
        // Which costs nothing, unlike spending them wrongly.
        FakeLua lua = FakeLua.Typical335a();
        lua.Functions.Remove("LearnTalent");
        lua.Answers["GetUnspentTalentPoints()"] = "5";

        LuaTalents talents = Build(lua);

        Assert.Equal(0, talents.UnspentPoints);
        Assert.False(talents.Learn(new TalentPick(1, 1)));
    }

    [Fact]
    public void NothingHereCanUndoAPoint()
    {
        // Unlearning costs gold and is not a decision a bot should make at three in the morning.
        string[] members = [.. typeof(LuaTalents).GetMethods().Select(method => method.Name)];

        Assert.DoesNotContain(members, name =>
            name.Contains("Reset", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Unlearn", StringComparison.OrdinalIgnoreCase));
    }
}
