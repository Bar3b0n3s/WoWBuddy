using WoWBuddy.GameApi.Enums;
using WoWBuddy.Live;
using Xunit;

namespace WoWBuddy.Live.Tests;

public sealed class HostilityRuleTests
{
    [Fact]
    public void AnOrdinaryCreatureCouldBeHostile()
    {
        Assert.True(HostilityRule.CouldBeHostile(isPlayer: false, NpcFlags.None, UnitFlags.None));
    }

    [Fact]
    public void AnotherPlayerIsNever()
    {
        // Attacking one is a decision the grinding bases should never make on their own. The
        // battleground base picks its own targets and does not come through here.
        Assert.False(HostilityRule.CouldBeHostile(isPlayer: true, NpcFlags.None, UnitFlags.None));
    }

    [Theory]
    [InlineData(NpcFlags.Vendor)]
    [InlineData(NpcFlags.Trainer)]
    [InlineData(NpcFlags.QuestGiver)]
    [InlineData(NpcFlags.FlightMaster)]
    [InlineData(NpcFlags.Banker)]
    [InlineData(NpcFlags.Repair)]
    public void AnythingOfferingAServiceIsNot(NpcFlags flags)
    {
        // The rule doing most of the work: none of these is a target, and all of them stand
        // exactly where a grinding character walks past.
        Assert.False(HostilityRule.CouldBeHostile(isPlayer: false, flags, UnitFlags.None));
    }

    [Theory]
    [InlineData(UnitFlags.NotSelectable)]
    [InlineData(UnitFlags.Pacified)]
    public void TheClientWouldRefuseTheseAnyway(UnitFlags flags)
    {
        // Trying produces an error message per attempt, which is both useless and noisy.
        Assert.False(HostilityRule.CouldBeHostile(isPlayer: false, NpcFlags.None, flags));
    }

    [Fact]
    public void BeingInCombatDoesNotDisqualifyAnything()
    {
        // Something already fighting is frequently the thing that most needs hitting.
        Assert.True(HostilityRule.CouldBeHostile(isPlayer: false, NpcFlags.None, UnitFlags.InCombat));
    }

    [Fact]
    public void AVendorThatIsAlsoSomethingElseIsStillNotATarget()
    {
        Assert.False(HostilityRule.CouldBeHostile(
            isPlayer: false,
            NpcFlags.Vendor | NpcFlags.Repair,
            UnitFlags.None));
    }

    [Fact]
    public void ThisIsAnApproximationAndTheRuleSaysSo()
    {
        // A neutral critter carries no NPC flags and is selectable, so it passes. That is the
        // documented limitation rather than a bug: without faction data the rule cannot tell,
        // and the profile's avoid list is what covers the difference.
        Assert.True(HostilityRule.CouldBeHostile(isPlayer: false, NpcFlags.None, UnitFlags.None));
    }
}
