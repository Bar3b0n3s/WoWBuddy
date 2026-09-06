using WoWBuddy.CombatRoutines;
using Xunit;

namespace WoWBuddy.CombatRoutines.Tests;

public sealed class RotationTests
{
    [Fact]
    public void CastsTheHighestPriorityRuleThatApplies()
    {
        var rotation = new Rotation()
            .Cast("Execute", c => c.Target is { HealthPercent: < 20d })
            .Cast("Bloodthirst")
            .Cast("Heroic Strike");

        var combat = new FakeCombat()
            .WithReady("Execute", "Bloodthirst", "Heroic Strike")
            .WithTarget(healthPercent: 10d);

        Assert.Equal("Execute", rotation.Execute(combat)?.SpellName);
        Assert.Equal(["Execute"], combat.CastLog);
    }

    [Fact]
    public void SkipsRulesWhoseConditionDoesNotHold()
    {
        var rotation = new Rotation()
            .Cast("Execute", c => c.Target is { HealthPercent: < 20d })
            .Cast("Bloodthirst");

        var combat = new FakeCombat()
            .WithReady("Execute", "Bloodthirst")
            .WithTarget(healthPercent: 80d);

        Assert.Equal("Bloodthirst", rotation.Execute(combat)?.SpellName);
    }

    [Fact]
    public void SkipsSpellsThatAreNotReady()
    {
        var rotation = new Rotation().Cast("Bloodthirst").Cast("Heroic Strike");
        var combat = new FakeCombat().WithReady("Heroic Strike");

        Assert.Equal("Heroic Strike", rotation.Execute(combat)?.SpellName);
    }

    [Fact]
    public void CastsAtMostOneSpellPerDecision()
    {
        // The global cooldown means a second cast in the same instant cancels the first, so a
        // rotation that fired several rules at once would land fewer spells, not more.
        var rotation = new Rotation().Cast("A").Cast("B").Cast("C");
        var combat = new FakeCombat().WithReady("A", "B", "C");

        rotation.Execute(combat);

        Assert.Single(combat.CastLog);
    }

    [Fact]
    public void DoesNothingWhileTheCharacterIsAlreadyCasting()
    {
        // Starting a second cast during the first cancels it, which turns a working rotation
        // into a character standing still doing nothing.
        var rotation = new Rotation().Cast("Frostbolt");
        var combat = new FakeCombat { IsCasting = true }.WithReady("Frostbolt");

        Assert.Null(rotation.Execute(combat));
        Assert.Empty(combat.CastLog);
    }

    [Fact]
    public void ReportsNothingWhenNoRuleApplies()
    {
        var rotation = new Rotation().Cast("Bloodthirst");
        var combat = new FakeCombat();

        Assert.Null(rotation.Execute(combat));
    }

    [Fact]
    public void ARefusedCastIsReportedAsNothingHavingHappened()
    {
        // Otherwise the bot believes it cast something and moves on, and the fight quietly
        // stops progressing.
        var rotation = new Rotation().Cast("Bloodthirst");
        var combat = new FakeCombat().WithReady("Bloodthirst");
        combat.Refused.Add("Bloodthirst");

        Assert.Null(rotation.Execute(combat));
    }

    [Fact]
    public void PreviewReportsTheDecisionWithoutCasting()
    {
        var rotation = new Rotation().Cast("Bloodthirst");
        var combat = new FakeCombat().WithReady("Bloodthirst");

        Assert.Equal("Bloodthirst", rotation.Preview(combat)?.SpellName);
        Assert.Empty(combat.CastLog);
    }

    [Fact]
    public void AMissingUnitCountsAsNotHavingAnAura()
    {
        // Lets a rule say "refresh when the target lacks it" without restating what to do
        // when there is no target.
        var rotation = new Rotation()
            .Cast("Serpent Sting", c => !c.HasAura(c.Target, "Serpent Sting"));

        var combat = new FakeCombat().WithReady("Serpent Sting");

        Assert.Equal("Serpent Sting", rotation.Preview(combat)?.SpellName);
    }
}
