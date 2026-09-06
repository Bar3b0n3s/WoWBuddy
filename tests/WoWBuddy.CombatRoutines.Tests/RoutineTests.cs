using WoWBuddy.CombatRoutines;
using WoWBuddy.CombatRoutines.Routines;
using WoWBuddy.GameApi.Enums;
using Xunit;

namespace WoWBuddy.CombatRoutines.Tests;

/// <summary>
/// Checks the four routines make sensible decisions in situations that matter.
/// </summary>
/// <remarks>
/// Not an attempt to prove the rotations are optimal — that is a matter for a simulator and
/// for the user's own editing. These cover the decisions that are actively wrong rather than
/// merely suboptimal: healing after damaging, shooting inside a hunter's dead zone, resting
/// for a resource that does not regenerate.
/// </remarks>
public sealed class RoutineTests
{
    private static ICombatRoutine[] AllRoutines() =>
        [new FuryWarrior(), new FrostMage(), new HolyPriest(), new BeastMasteryHunter()];

    [Fact]
    public void EveryRoutineNamesItselfAndItsClass()
    {
        foreach (ICombatRoutine routine in AllRoutines())
        {
            Assert.False(string.IsNullOrWhiteSpace(routine.Name));
            Assert.NotEqual(WoWClass.None, routine.Class);
            Assert.True(routine.PullRange > 0f);
        }
    }

    [Fact]
    public void MeleeAndCasterRoutinesDisagreeAboutPullRange()
    {
        // The bot base uses this to decide when to stop walking and start fighting, so a
        // caster claiming melee range would walk it into every fight it should have opened
        // from thirty yards.
        Assert.True(new FrostMage().PullRange > new FuryWarrior().PullRange);
        Assert.True(new BeastMasteryHunter().PullRange > new FuryWarrior().PullRange);
    }

    [Fact]
    public void AWarriorNeverWaitsForRageToRefill()
    {
        // Rage decays out of combat, so a warrior that rested for it would rest forever.
        var warrior = new FuryWarrior();
        var combat = new FakeCombat().WithHealth(95d).WithPower(0d);

        Assert.True(warrior.IsReadyToFight(combat));
    }

    [Fact]
    public void ACasterWaitsForManaBeforeFightingAgain()
    {
        var mage = new FrostMage();
        var combat = new FakeCombat().WithHealth(100d).WithPower(20d);

        Assert.False(mage.IsReadyToFight(combat));
    }

    [Fact]
    public void EveryRoutineRefusesToFightWhileBadlyHurt()
    {
        foreach (ICombatRoutine routine in AllRoutines())
        {
            var combat = new FakeCombat().WithHealth(20d).WithPower(100d);
            Assert.False(routine.IsReadyToFight(combat), routine.Name);
        }
    }

    [Fact]
    public void AWarriorOpensWithChargeRatherThanARageSpender()
    {
        var warrior = new FuryWarrior();
        var combat = new FakeCombat()
            .WithReady("Charge", "Bloodthirst", "Heroic Strike")
            .WithTarget(distance: 15f);

        warrior.Pull(combat);

        Assert.Equal(["Charge"], combat.CastLog);
    }

    [Fact]
    public void AWarriorTakesVictoryRushOnlyWhenTheHealIsWorthSomething()
    {
        var warrior = new FuryWarrior();

        var healthy = new FakeCombat().WithReady("Victory Rush", "Bloodthirst").WithTarget().WithHealth(100d);
        warrior.Combat(healthy);
        Assert.Equal(["Bloodthirst"], healthy.CastLog);

        var hurt = new FakeCombat().WithReady("Victory Rush", "Bloodthirst").WithTarget().WithHealth(60d);
        warrior.Combat(hurt);
        Assert.Equal(["Victory Rush"], hurt.CastLog);
    }

    [Fact]
    public void AWarriorExecutesOnlyOnceTheTargetIsNearlyDead()
    {
        var warrior = new FuryWarrior();
        var combat = new FakeCombat().WithReady("Execute").WithTarget(healthPercent: 15d);

        warrior.Combat(combat);

        Assert.Equal(["Execute"], combat.CastLog);
    }

    [Fact]
    public void AHealerHealsBeforeItDamages()
    {
        // A rotation that damages first and heals with what is left over is how a healer dies
        // with a full mana bar.
        var priest = new HolyPriest();
        var combat = new FakeCombat()
            .WithReady("Flash Heal", "Smite", "Holy Fire")
            .WithTarget()
            .WithHealth(30d);

        priest.Combat(combat);

        Assert.Equal(["Flash Heal"], combat.CastLog);
    }

    [Fact]
    public void AHealerDoesNotShieldThroughWeakenedSoul()
    {
        // Casting it anyway wastes the global cooldown and the mana for nothing.
        var priest = new HolyPriest();
        var combat = new FakeCombat().WithReady("Power Word: Shield", "Smite").WithTarget().WithHealth(60d);
        combat.MyAuras.Add("Weakened Soul");

        priest.Combat(combat);

        Assert.Equal(["Smite"], combat.CastLog);
    }

    [Fact]
    public void AHunterMeleesRatherThanShootingInsideTheDeadZone()
    {
        // Ranged attacks simply do not fire this close, so a rotation that ignores it
        // produces a character standing still doing nothing.
        var hunter = new BeastMasteryHunter();
        var combat = new FakeCombat()
            .WithReady("Raptor Strike", "Steady Shot", "Arcane Shot")
            .WithTarget(distance: 3f)
            .WithPet(onTarget: true);
        combat.EnemiesInMelee = 1;

        hunter.Combat(combat);

        Assert.Equal(["Raptor Strike"], combat.CastLog);
    }

    [Fact]
    public void AHunterKeepsItsPetAliveBeforeAnythingElse()
    {
        // The pet is the tank; losing it means the hunter takes the damage instead.
        var hunter = new BeastMasteryHunter();
        var combat = new FakeCombat()
            .WithReady("Mend Pet", "Kill Command", "Steady Shot")
            .WithTarget()
            .WithPet(healthPercent: 20d, onTarget: true);

        hunter.Combat(combat);

        Assert.Equal(["Mend Pet"], combat.CastLog);
    }

    [Fact]
    public void AHunterSendsItsPetAtWhateverItIsFighting()
    {
        var hunter = new BeastMasteryHunter();
        var combat = new FakeCombat().WithTarget().WithPet(onTarget: false);

        Assert.True(hunter.PetControl(combat));
        Assert.Equal(["Attack"], combat.CastLog);
    }

    [Fact]
    public void AHunterLeavesItsPetAloneWhenItIsAlreadyOnTheTarget()
    {
        var hunter = new BeastMasteryHunter();
        var combat = new FakeCombat().WithTarget().WithPet(onTarget: true);

        Assert.False(hunter.PetControl(combat));
        Assert.Empty(combat.CastLog);
    }

    [Fact]
    public void RoutinesWithoutPetsDoNothingForPetControl()
    {
        var combat = new FakeCombat().WithTarget();

        Assert.False(new FuryWarrior().PetControl(combat));
        Assert.False(new FrostMage().PetControl(combat));
        Assert.Empty(combat.CastLog);
    }

    [Fact]
    public void BuffsAreOnlyCastWhenMissing()
    {
        var mage = new FrostMage();

        var buffed = new FakeCombat().WithReady("Frost Armor", "Arcane Intellect");
        buffed.MyAuras.Add("Frost Armor");
        buffed.MyAuras.Add("Arcane Intellect");
        Assert.False(mage.Buff(buffed));
        Assert.Empty(buffed.CastLog);

        var unbuffed = new FakeCombat().WithReady("Frost Armor", "Arcane Intellect");
        Assert.True(mage.Buff(unbuffed));
        Assert.Equal(["Frost Armor"], unbuffed.CastLog);
    }

    [Fact]
    public void RestReportsWhetherTheCharacterStillNeedsTo()
    {
        var mage = new FrostMage();

        Assert.False(mage.Rest(new FakeCombat().WithHealth(100d).WithPower(100d)));
        Assert.True(mage.Rest(new FakeCombat().WithHealth(100d).WithPower(20d)));
        Assert.True(mage.Rest(new FakeCombat().WithHealth(40d).WithPower(100d)));
    }

    [Fact]
    public void AMageEvocatesRatherThanDrinkingWhenItCan()
    {
        var mage = new FrostMage();
        var combat = new FakeCombat().WithReady("Evocation").WithPower(10d);

        mage.Rest(combat);

        Assert.Equal(["Evocation"], combat.CastLog);
    }
}
