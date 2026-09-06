using WoWBuddy.Core.Tests;
using WoWBuddy.Live;
using Xunit;

namespace WoWBuddy.Live.Tests;

public sealed class LuaWorldEntryTests
{
    /// <summary>A client showing the character-select screen.</summary>
    private static FakeLua AtCharacterSelect() =>
        new FakeLua().With("EnterWorld", "CharacterSelect_SelectCharacter");

    [Fact]
    public void ACharacterSelectScreenIsSomewhereItCanEnterFrom()
    {
        Assert.True(new LuaWorldEntry(AtCharacterSelect()).CanEnterWorld);
    }

    [Fact]
    public void ALoginScreenIsNotAndItDoesNotPretendOtherwise()
    {
        // The line this project will not cross: with no credentials there is nothing to answer
        // a login screen with, so the honest answer is no.
        Assert.False(new LuaWorldEntry(new FakeLua()).CanEnterWorld);
    }

    [Fact]
    public void AClientThatCannotAnswerAtAllIsNotGuessedAt()
    {
        FakeLua lua = new() { CanReadResults = false };
        lua.With("EnterWorld");

        Assert.False(new LuaWorldEntry(lua).CanEnterWorld);
    }

    [Fact]
    public void EnteringAsWhoeverIsChosenTouchesNothingElse()
    {
        // Character select opens on the last-played character, which is the one that just fell
        // out of the world — so the common path needs one call and no unverified ones.
        FakeLua lua = AtCharacterSelect();

        Assert.True(new LuaWorldEntry(lua).EnterWorld(slot: 0));

        Assert.Contains("execute: EnterWorld()", lua.Asked);
        Assert.DoesNotContain(
            lua.Asked,
            asked => asked.Contains("execute: CharacterSelect_SelectCharacter", StringComparison.Ordinal));
    }

    [Fact]
    public void ANamedSlotIsChosenFirst()
    {
        FakeLua lua = AtCharacterSelect();

        new LuaWorldEntry(lua).EnterWorld(slot: 2);

        Assert.Contains("execute: CharacterSelect_SelectCharacter(2)", lua.Asked);
        Assert.Contains("execute: EnterWorld()", lua.Asked);
    }

    [Fact]
    public void AClientWithNoWayToChooseStillEntersAsWhoeverIsSelected()
    {
        // Degrading to the last-played character beats refusing: it is nearly always the right
        // one, and the alternative is a session that ends because of a call that is missing.
        FakeLua lua = new FakeLua().With("EnterWorld");

        Assert.True(new LuaWorldEntry(lua).EnterWorld(slot: 2));
        Assert.Contains("execute: EnterWorld()", lua.Asked);
    }

    [Fact]
    public void NothingIsRunAtAScreenItCannotEnterFrom()
    {
        FakeLua lua = new();

        Assert.False(new LuaWorldEntry(lua).EnterWorld(slot: 0));
        Assert.DoesNotContain(
            lua.Asked,
            asked => asked.StartsWith("execute:", StringComparison.Ordinal));
    }

    [Fact]
    public void ItNeverAsksForCredentials()
    {
        // There are none to ask for, and this is the test that keeps it that way.
        FakeLua lua = AtCharacterSelect();

        new LuaWorldEntry(lua).EnterWorld(slot: 1);

        Assert.DoesNotContain(
            lua.Asked,
            asked => asked.Contains("Login", StringComparison.OrdinalIgnoreCase)
                || asked.Contains("password", StringComparison.OrdinalIgnoreCase));
    }
}
