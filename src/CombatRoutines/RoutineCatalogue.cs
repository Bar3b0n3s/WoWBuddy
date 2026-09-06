using System.Reflection;
using WoWBuddy.Common.Logging;
using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.CombatRoutines;

/// <summary>
/// Every routine the bot knows about.
/// </summary>
/// <remarks>
/// <para>
/// Built by looking at the assembly rather than from a hand-written list, so that adding a
/// routine is one file and nothing else. A list would be a second place to forget.
/// </para>
/// <para>
/// <see cref="Add"/> takes an assembly, which is how a plugin contributes routines of its own.
/// </para>
/// </remarks>
public sealed class RoutineCatalogue
{
    private readonly List<ICombatRoutine> _routines = [];

    /// <summary>Builds a catalogue of the routines that ship with the bot.</summary>
    public RoutineCatalogue()
    {
        Add(typeof(RoutineCatalogue).Assembly);
    }

    /// <summary>Every routine, ordered by class and then by name.</summary>
    public IReadOnlyList<ICombatRoutine> All =>
        [.. _routines.OrderBy(routine => routine.Class).ThenBy(routine => routine.Name, StringComparer.Ordinal)];

    /// <summary>
    /// Adds every routine an assembly offers.
    /// </summary>
    /// <remarks>
    /// A routine has to be public, concrete and constructible without arguments. Anything that
    /// is not is skipped with a line saying so, rather than silently: a plugin author whose
    /// routine does not appear deserves to know why.
    /// </remarks>
    public int Add(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        int added = 0;

        foreach (Type type in assembly.GetTypes())
        {
            if (!typeof(ICombatRoutine).IsAssignableFrom(type) || type.IsAbstract || type.IsInterface)
            {
                continue;
            }

            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                Log.For<RoutineCatalogue>().Warning(
                    "{Type} is a combat routine but has no parameterless constructor, so it "
                    + "cannot be listed", type.FullName);
                continue;
            }

            try
            {
                if (Activator.CreateInstance(type) is ICombatRoutine routine)
                {
                    _routines.Add(routine);
                    added++;
                }
            }
            catch (TargetInvocationException exception)
            {
                Log.For<RoutineCatalogue>().Warning(
                    exception, "{Type} threw while being created, so it cannot be listed",
                    type.FullName);
            }
        }

        return added;
    }

    /// <summary>Every routine that plays a class, by name.</summary>
    public IReadOnlyList<ICombatRoutine> For(WoWClass wowClass) =>
        [.. _routines.Where(routine => routine.Class == wowClass)
            .OrderBy(routine => routine.Name, StringComparer.Ordinal)];

    /// <summary>The routine with this name, or null when there is none.</summary>
    public ICombatRoutine? ByName(string name) =>
        _routines.FirstOrDefault(routine =>
            string.Equals(routine.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A reasonable routine to start a class on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are chosen for how well the specialisation survives being played unattended, not
    /// for how much damage it does. A character that kills half as fast and never dies finishes
    /// the night far ahead of one that kills quickly and spends the night running back from a
    /// graveyard.
    /// </para>
    /// <para>
    /// The user is expected to override this; it exists so that picking a class does something
    /// sensible before anyone has chosen.
    /// </para>
    /// </remarks>
    public ICombatRoutine? Default(WoWClass wowClass)
    {
        string? preferred = wowClass switch
        {
            WoWClass.Warrior => "Fury Warrior",
            WoWClass.Paladin => "Retribution Paladin",
            WoWClass.Hunter => "Beast Mastery Hunter",
            WoWClass.Rogue => "Combat Rogue",
            WoWClass.Priest => "Shadow Priest",
            WoWClass.DeathKnight => "Blood Death Knight",
            WoWClass.Shaman => "Enhancement Shaman",
            WoWClass.Mage => "Frost Mage",
            WoWClass.Warlock => "Affliction Warlock",
            WoWClass.Druid => "Feral Druid",
            _ => null,
        };

        if (preferred is not null && ByName(preferred) is { } routine)
        {
            return routine;
        }

        // A plugin may have replaced the usual one, or a build may be missing it. Anything for
        // the right class beats nothing.
        return For(wowClass).FirstOrDefault();
    }

    /// <summary>Classes that have at least one routine.</summary>
    public IReadOnlyList<WoWClass> Classes =>
        [.. _routines.Select(routine => routine.Class).Distinct().OrderBy(wowClass => wowClass)];
}
