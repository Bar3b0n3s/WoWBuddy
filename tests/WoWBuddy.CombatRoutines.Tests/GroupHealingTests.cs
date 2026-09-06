using WoWBuddy.CombatRoutines;
using WoWBuddy.CombatRoutines.Routines;
using Xunit;

namespace WoWBuddy.CombatRoutines.Tests;

public sealed class GroupHealingTests
{
    [Fact]
    public void HealsWhoeverIsWorstOffRatherThanWhoeverIsNearest()
    {
        // The failure that kills groups: topping up a damage dealer at 70 per cent while the
        // tank drops from 30 to nothing.
        var combat = new FakeCombat().WithReady("Flash Heal").WithHealth(100d);

        combat.WithGroupMember(0x11, healthPercent: 70d, distance: 5f);
        UnitSnapshot tank = combat.WithGroupMember(0x22, healthPercent: 30d, distance: 30f);

        new HolyPriest().Combat(combat);

        Assert.Equal(["Flash Heal"], combat.CastLog);
        Assert.Equal(tank.Guid, Assert.Single(combat.CastOnLog).Unit);
    }

    [Fact]
    public void HealsItselfWhenItIsTheWorstOff()
    {
        // A healer that never heals itself dies with the group at full health.
        var combat = new FakeCombat().WithReady("Flash Heal").WithHealth(20d);
        combat.WithGroupMember(0x11, healthPercent: 90d);

        new HolyPriest().Combat(combat);

        Assert.Equal(combat.Me.Guid, Assert.Single(combat.CastOnLog).Unit);
    }

    [Fact]
    public void IgnoresTheDeadAndTheOutOfRange()
    {
        var combat = new FakeCombat().WithReady("Flash Heal").WithHealth(100d);

        combat.WithGroupMember(0x11, healthPercent: 5d, alive: false);
        combat.WithGroupMember(0x22, healthPercent: 10d, distance: 200f);
        UnitSnapshot reachable = combat.WithGroupMember(0x33, healthPercent: 40d, distance: 20f);

        Assert.Equal(reachable.Guid, GroupHealing.MostHurt(combat, 45d)!.Value.Guid);
    }

    [Fact]
    public void ASoloHealerBehavesExactlyAsItDidBeforeGroupsExisted()
    {
        // The group is empty, so every group rule resolves to the character itself.
        var combat = new FakeCombat().WithReady("Flash Heal", "Smite").WithTarget().WithHealth(30d);

        new HolyPriest().Combat(combat);

        Assert.Equal(["Flash Heal"], combat.CastLog);
        Assert.Equal(combat.Me.Guid, Assert.Single(combat.CastOnLog).Unit);
    }

    [Fact]
    public void DamagesOnlyWhenNobodyNeedsHealing()
    {
        var combat = new FakeCombat().WithReady("Smite", "Flash Heal").WithTarget().WithHealth(100d);
        combat.WithGroupMember(0x11, healthPercent: 100d);

        new HolyPriest().Combat(combat);

        Assert.Equal(["Smite"], combat.CastLog);
    }

    [Fact]
    public void DoesNotDamageWithNoTarget()
    {
        // A healer stood behind the tank with nothing targeted must not burn the global
        // cooldown on a spell the client will refuse.
        var combat = new FakeCombat().WithReady("Smite", "Holy Fire", "Shadow Word: Pain")
            .WithHealth(100d);

        new HolyPriest().Combat(combat);

        Assert.Empty(combat.CastLog);
    }

    [Fact]
    public void GroupHealsWaitForEnoughPeopleToBeHurt()
    {
        // Casting a group heal to save one person wastes both the mana and the cooldown.
        var combat = new FakeCombat().WithReady("Prayer of Healing").WithHealth(100d);
        combat.WithGroupMember(0x11, healthPercent: 60d);
        combat.WithGroupMember(0x22, healthPercent: 100d);

        new HolyPriest().Combat(combat);
        Assert.Empty(combat.CastLog);

        var damaged = new FakeCombat().WithReady("Prayer of Healing").WithHealth(60d);
        damaged.WithGroupMember(0x11, healthPercent: 60d);
        damaged.WithGroupMember(0x22, healthPercent: 60d);

        new HolyPriest().Combat(damaged);
        Assert.Equal(["Prayer of Healing"], damaged.CastLog);
    }

    [Fact]
    public void DoesNotShieldSomeoneWhoAlreadyHasWeakenedSoul()
    {
        var combat = new FakeCombat().WithReady("Power Word: Shield").WithHealth(100d);
        UnitSnapshot hurt = combat.WithGroupMember(0x11, healthPercent: 50d);
        combat.WithGroupAura(hurt, "Weakened Soul");

        new HolyPriest().Combat(combat);

        Assert.Empty(combat.CastLog);
    }

    [Fact]
    public void CountsWhoIsHurtWithoutCountingTheDeadOrTheDistant()
    {
        var combat = new FakeCombat().WithHealth(50d);
        combat.WithGroupMember(0x11, healthPercent: 50d, distance: 10f);
        combat.WithGroupMember(0x22, healthPercent: 50d, alive: false);
        combat.WithGroupMember(0x33, healthPercent: 50d, distance: 100f);

        Assert.Equal(2, GroupHealing.CountBelow(combat, 65d));
        Assert.True(GroupHealing.AnyoneBelow(combat, 65d));
        Assert.False(GroupHealing.AnyoneBelow(combat, 40d));
    }

    [Fact]
    public void ARuleThatNamesNobodyDoesNotApply()
    {
        // What lets "heal whoever is hurt" sit in a rotation without a condition repeating the
        // same search: no candidate means the rule is simply skipped.
        var rotation = new Rotation()
            .CastOn("Flash Heal", c => GroupHealing.MostHurt(c, 50d))
            .Cast("Smite");

        var healthy = new FakeCombat().WithReady("Flash Heal", "Smite").WithTarget().WithHealth(100d);

        Assert.Equal("Smite", rotation.Preview(healthy)!.SpellName);
    }
}
