using WoWBuddy.CombatRoutines;
using WoWBuddy.GameApi.Enums;
using Xunit;

namespace WoWBuddy.CombatRoutines.Tests;

public sealed class RoutineCatalogueTests
{
    private static readonly RoutineCatalogue Catalogue = new();

    [Fact]
    public void EveryClassHasThreeRoutines()
    {
        // Ten classes, three specialisations each. A class with fewer means one was written
        // and never picked up by the catalogue, which is exactly the failure a hand-written
        // list would hide.
        foreach (WoWClass wowClass in Enum.GetValues<WoWClass>().Where(c => c != WoWClass.None))
        {
            Assert.Equal(3, Catalogue.For(wowClass).Count);
        }

        Assert.Equal(30, Catalogue.All.Count);
    }

    [Fact]
    public void NamesAreUniqueAndUsable()
    {
        Assert.Equal(Catalogue.All.Count, Catalogue.All.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (ICombatRoutine routine in Catalogue.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(routine.Name));
            Assert.NotEqual(WoWClass.None, routine.Class);
            Assert.Same(routine, Catalogue.ByName(routine.Name));
        }
    }

    [Fact]
    public void EveryClassHasASensibleDefault()
    {
        foreach (WoWClass wowClass in Catalogue.Classes)
        {
            ICombatRoutine routine = Assert.IsAssignableFrom<ICombatRoutine>(Catalogue.Default(wowClass));
            Assert.Equal(wowClass, routine.Class);
        }

        Assert.Null(Catalogue.Default(WoWClass.None));
    }

    [Fact]
    public void EveryRoutineDoesSomethingInAFight()
    {
        // A rotation whose rules all fail to apply would leave the character standing there.
        // Every spell is treated as ready and everything as hurt, so any routine that casts
        // nothing here has a rotation that cannot fire at all.
        foreach (ICombatRoutine routine in Catalogue.All)
        {
            var combat = new FakeCombat { ReadyForAnything = true }
                .WithTarget(healthPercent: 50d)
                .WithHealth(50d);

            Assert.True(routine.Combat(combat), $"{routine.Name} cast nothing in combat");
        }
    }

    [Fact]
    public void EveryRoutineOpensAFight()
    {
        foreach (ICombatRoutine routine in Catalogue.All)
        {
            var combat = new FakeCombat { ReadyForAnything = true }.WithTarget();

            Assert.True(routine.Pull(combat), $"{routine.Name} cast nothing to open a fight");
        }
    }

    [Fact]
    public void EveryRoutineBuffsItself()
    {
        foreach (ICombatRoutine routine in Catalogue.All)
        {
            // Combo points are given because a rogue's only thing to keep up between pulls is
            // Slice and Dice, which needs one. Wrath rogues have no other self-buff: poisons
            // are applied to the weapon from the bags, not cast.
            var combat = new FakeCombat { ReadyForAnything = true, ComboPoints = 5 };

            Assert.True(routine.Buff(combat), $"{routine.Name} has no buffs to keep up");
        }
    }

    [Fact]
    public void NoRoutineCastsAtNothing()
    {
        // A character standing with nothing targeted must not burn its global cooldown on a
        // spell the client will refuse. Healing routines are the exception: they have someone
        // to heal even with no target, and that someone is themselves.
        foreach (ICombatRoutine routine in Catalogue.All)
        {
            var combat = new FakeCombat { ReadyForAnything = true }.WithHealth(100d);

            Assert.False(
                routine.Combat(combat),
                $"{routine.Name} cast {string.Join(", ", combat.CastLog)} with no target and nothing hurt");
        }
    }

    [Fact]
    public void ClassesWithoutDrinkablePowerDoNotWaitForABar()
    {
        // Rage, energy and runic power refill on their own and are wasted out of combat, so a
        // warrior or rogue that waited for a full bar would wait forever.
        foreach (ICombatRoutine routine in Catalogue.All)
        {
            if (routine.Class is not (WoWClass.Warrior or WoWClass.Rogue or WoWClass.DeathKnight))
            {
                continue;
            }

            var combat = new FakeCombat().WithHealth(100d);
            combat.Me = combat.Me with { PowerPercent = 0d };

            Assert.True(
                routine.IsReadyToFight(combat),
                $"{routine.Name} refuses to fight on an empty power bar it cannot refill by waiting");
        }
    }

    [Fact]
    public void APluginAssemblyCanContributeRoutines()
    {
        RoutineCatalogue catalogue = new();
        int before = catalogue.All.Count;

        // The bot's own assembly again: adding it twice is what a plugin that shipped a copy
        // would look like, and it must not throw.
        int added = catalogue.Add(typeof(RoutineCatalogue).Assembly);

        Assert.Equal(before, added);
        Assert.Equal(before * 2, catalogue.All.Count);
    }
}
