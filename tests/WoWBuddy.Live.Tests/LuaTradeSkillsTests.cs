using WoWBuddy.BotBases.Support;
using WoWBuddy.Core.Tests;
using WoWBuddy.GameApi.Capabilities;
using WoWBuddy.Live;
using Xunit;

namespace WoWBuddy.Live.Tests;

public sealed class LuaTradeSkillsTests
{
    private const string Field = "\u001F";
    private const string Row = "\u001E";

    private static string Header(string line, int rank, int maxRank) =>
        string.Join(Field, line, rank, maxRank);

    private static string Recipe(int index, string name, string kind, int available) =>
        string.Join(Field, index, name, kind, available);

    private static LuaTradeSkills Build(FakeLua lua, DateTimeOffset? now = null)
    {
        DateTimeOffset clock = now ?? DateTimeOffset.UnixEpoch;
        return new LuaTradeSkills(lua, CapabilityProbes.Probe(lua), () => clock);
    }

    [Theory]
    [InlineData("optimal", RecipeDifficulty.Optimal)]
    [InlineData("medium", RecipeDifficulty.Medium)]
    [InlineData("easy", RecipeDifficulty.Easy)]
    [InlineData("trivial", RecipeDifficulty.Trivial)]
    [InlineData("something else", RecipeDifficulty.Unknown)]
    public void TheClientsOwnColourWordsAreUnderstood(string kind, RecipeDifficulty expected)
    {
        Assert.Equal(expected, LuaTradeSkills.ParseDifficulty(kind));
    }

    [Fact]
    public void ReadsTheOpenWindowInOneRoundTrip()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            Header("Blacksmithing", 150, 150),
            Recipe(2, "Rough Sharpening Stone", "trivial", 40),
            Recipe(5, "Copper Chain Belt", "optimal", 3));

        LuaTradeSkills skills = Build(lua);

        Assert.Equal("Blacksmithing", skills.Line.Name);
        Assert.Equal(150, skills.Line.Rank);

        // At the cap for this rank of training: the character needs a trainer, not more ore.
        Assert.True(skills.Line.IsCapped);
        Assert.True(skills.Line.IsOpen);

        Assert.Equal(2, skills.Recipes.Count);
        Assert.Equal(1, skills.Reads);
    }

    [Fact]
    public void HeadersAreSkippedButTheirIndicesAreNot()
    {
        // Making something takes the client's own index, and getting it wrong makes the wrong
        // thing — so the index recorded is never a position in the filtered list.
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            Header("Cooking", 60, 150),
            Recipe(4, "Spiced Wolf Meat", "optimal", 2));

        TradeSkillRecipe recipe = Assert.Single(Build(lua).Recipes);

        Assert.Equal(4, recipe.Index);
    }

    [Fact]
    public void TheBestThingToMakeIsTheHighestColourThereAreMaterialsFor()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            Header("Blacksmithing", 100, 150),
            Recipe(2, "Copper Bracers", "medium", 5),
            Recipe(3, "Copper Chain Belt", "optimal", 0),
            Recipe(4, "Rough Grinding Stone", "easy", 20));

        // Orange is best but there are no materials for it, so yellow wins.
        Assert.Equal("Copper Bracers", Build(lua).BestForSkillUp()!.Value.Name);
    }

    [Fact]
    public void GreyRecipesAreNeverChosenToLevelWith()
    {
        // Making them consumes materials and raises nothing, which is worse than stopping.
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            Header("Blacksmithing", 140, 150),
            Recipe(2, "Rough Sharpening Stone", "trivial", 99));

        Assert.Null(Build(lua).BestForSkillUp());
    }

    [Fact]
    public void ARecipeWithNoMaterialsIsNotChosen()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            Header("Blacksmithing", 100, 150),
            Recipe(2, "Copper Bracers", "optimal", 0));

        Assert.Null(Build(lua).BestForSkillUp());
    }

    [Fact]
    public void MakingSomethingUsesTheClientsIndex()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            Header("Cooking", 60, 150),
            Recipe(7, "Spiced Wolf Meat", "optimal", 4));

        Assert.Equal(3, Build(lua).Craft("Spiced Wolf Meat", 3));
        Assert.Contains(lua.Asked, asked => asked == "execute: DoTradeSkill(7, 3)");
    }

    [Fact]
    public void AskingForMoreThanTheMaterialsAllowIsClamped()
    {
        // Otherwise the bot believes it queued twenty and stops watching after five.
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            Header("Cooking", 60, 150),
            Recipe(7, "Spiced Wolf Meat", "optimal", 5));

        Assert.Equal(5, Build(lua).Craft("Spiced Wolf Meat", 20));
        Assert.Contains(lua.Asked, asked => asked == "execute: DoTradeSkill(7, 5)");
    }

    [Fact]
    public void ARecipeTheCharacterDoesNotKnowCannotBeMade()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = Header("Cooking", 60, 150);

        Assert.Equal(0, Build(lua).Craft("Dragonbreath Chili"));
    }

    [Fact]
    public void MakingSomethingClearsTheCacheBecauseTheMaterialsAreGone()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            Header("Cooking", 60, 150),
            Recipe(7, "Spiced Wolf Meat", "optimal", 4));

        LuaTradeSkills skills = Build(lua);

        skills.Craft("Spiced Wolf Meat");
        Assert.Equal(1, skills.Reads);

        _ = skills.Recipes;
        Assert.Equal(2, skills.Reads);
    }

    [Fact]
    public void NoWindowOpenIsNotAFailure()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = Header(string.Empty, 0, 0);

        LuaTradeSkills skills = Build(lua);

        Assert.False(skills.Line.IsOpen);
        Assert.Empty(skills.Recipes);
    }

    [Fact]
    public void AFailedReadLeavesNothingToActOn()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = null;

        LuaTradeSkills skills = Build(lua);

        Assert.Empty(skills.Recipes);
        Assert.False(skills.Line.IsOpen);
        Assert.Null(skills.BestForSkillUp());
    }

    [Fact]
    public void ARowItCannotReadIsDroppedRatherThanGuessedAt()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            Header("Cooking", 60, 150),
            Recipe(7, "Fine", "optimal", 1),
            "notanindex" + Field + "Broken" + Field + "optimal" + Field + "1",
            "0" + Field + "ZeroIndex" + Field + "optimal" + Field + "1");

        Assert.Equal("Fine", Assert.Single(Build(lua).Recipes).Name);
    }

    [Fact]
    public void AClientWithoutTheTradeSkillCallsIsNotAsked()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Functions.Remove("DoTradeSkill");
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            Header("Cooking", 60, 150),
            Recipe(7, "Spiced Wolf Meat", "optimal", 4));

        LuaTradeSkills skills = Build(lua);

        Assert.Empty(skills.Recipes);
        Assert.Equal(0, skills.Reads);
        Assert.Equal(0, skills.Craft("Spiced Wolf Meat"));
        Assert.False(skills.Close());
    }

    [Fact]
    public void AnUncappedSkillIsNotReportedAsCapped()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = Header("Blacksmithing", 120, 150);

        Assert.False(Build(lua).Line.IsCapped);
    }

    [Fact]
    public void ClosingTheWindowIsOneCall()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = Header("Cooking", 60, 150);

        Assert.True(Build(lua).Close());
        Assert.Contains(lua.Asked, asked => asked == "execute: CloseTradeSkill()");
    }

    [Fact]
    public void NoRecipeDataIsNeededBecauseTheClientHasIt()
    {
        // The whole point: what a character can make is in its own spellbook, so crafting is
        // one of the few things the bot does without asking the user to extract anything.
        Assert.Contains("GetNumTradeSkills()", LuaTradeSkills.ReadScript, StringComparison.Ordinal);
        Assert.Contains("GetTradeSkillInfo(i)", LuaTradeSkills.ReadScript, StringComparison.Ordinal);
        Assert.Contains("kind ~= \"header\"", LuaTradeSkills.ReadScript, StringComparison.Ordinal);
    }
}
