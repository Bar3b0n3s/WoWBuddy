using WoWBuddy.Core.Tests;
using WoWBuddy.GameApi.Capabilities;
using WoWBuddy.Live;
using Xunit;

namespace WoWBuddy.Live.Tests;

public sealed class LuaTravelTests
{
    private const string Mount = "Swift Palomino";

    private static LuaTravel Build(FakeLua lua, bool mounted = false, string mount = Mount) =>
        new(lua, CapabilityProbes.Probe(lua), () => mounted, mount);

    private static FakeLua Usable(bool usable = true)
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers[$"(function() local usable = IsUsableSpell(\"{Mount}\") return usable and true or false end)()"] =
            usable ? "true" : "false";
        return lua;
    }

    [Fact]
    public void MountsByCastingTheNamedMount()
    {
        // Which mounts a character owns is in its spellbook, but working out which is fastest
        // or usable here would need spell data this project does not ship.
        FakeLua lua = Usable();

        Assert.True(Build(lua).Mount());
        Assert.Contains(lua.Asked, a => a == $"execute: CastSpellByName(\"{Mount}\")");
    }

    [Fact]
    public void AsksTheClientRatherThanReasoningAboutWhereMountingWorks()
    {
        // Indoors, underwater, in combat, on a taxi, in a battleground before the gates open.
        // Enumerating those would be a worse copy of something the client already knows.
        FakeLua lua = Usable(usable: false);

        Assert.False(Build(lua).CanMount);
        Assert.False(Build(lua).Mount());
        Assert.DoesNotContain(lua.Asked, a => a.StartsWith("execute: CastSpellByName", StringComparison.Ordinal));
    }

    [Fact]
    public void AlreadyMountedIsNotAReasonToMountAgain()
    {
        FakeLua lua = Usable();

        Assert.False(Build(lua, mounted: true).CanMount);
    }

    [Fact]
    public void NoMountNamedMeansNoMounting()
    {
        FakeLua lua = Usable();

        Assert.False(Build(lua, mount: string.Empty).CanMount);
    }

    [Fact]
    public void DismountingUsesTheCallRatherThanCastingTheMountAgain()
    {
        // Casting it again does dismount, but it also starts a cast the character interrupts,
        // and on a flying mount over water that is a swim.
        FakeLua lua = Usable();

        Assert.True(Build(lua, mounted: true).Dismount());
        Assert.Contains(lua.Asked, a => a == "execute: Dismount()");
    }

    [Fact]
    public void DismountingOnFootDoesNothing()
    {
        FakeLua lua = Usable();

        Assert.False(Build(lua).Dismount());
    }

    [Fact]
    public void AMountNameWithAQuoteInItCannotEndTheString()
    {
        FakeLua lua = FakeLua.Typical335a();
        LuaTravel travel = new(lua, CapabilityProbes.Probe(lua), () => false, "Od\"d");

        _ = travel.CanMount;

        Assert.Contains(lua.Asked, a => a.Contains("Od\\\"d", StringComparison.Ordinal));
    }

    [Fact]
    public void AClientWithoutTheTravelCallsWalks()
    {
        FakeLua lua = Usable();
        lua.Functions.Remove("Dismount");

        LuaTravel travel = Build(lua, mounted: true);

        Assert.False(travel.CanMount);
        Assert.False(travel.Dismount());
    }

    [Fact]
    public void WhetherItIsMountedComesFromMemoryRatherThanScripting()
    {
        // The unit flag is verified, so it is a better answer than anything the scripting
        // would give.
        FakeLua lua = Usable();

        Assert.True(Build(lua, mounted: true).IsMounted);
        Assert.False(Build(lua, mounted: false).IsMounted);
    }
}
