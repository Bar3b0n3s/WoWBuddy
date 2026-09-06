namespace WoWBuddy.CombatRoutines;

/// <summary>
/// Choosing who to heal.
/// </summary>
/// <remarks>
/// <para>
/// Kept out of the individual routines because every healer answers this the same way and gets
/// it wrong the same way. The failure that kills groups is not picking the wrong spell, it is
/// healing the wrong person: topping up a damage dealer at 70 per cent while the tank drops
/// from 30 to nothing.
/// </para>
/// <para>
/// So: lowest health first, and the character itself counts as a group member. A healer that
/// never heals itself dies with the group at full health, which helps nobody.
/// </para>
/// </remarks>
public static class GroupHealing
{
    /// <summary>How far most heals reach in 3.3.5a.</summary>
    public const float HealRange = 40f;

    /// <summary>Whoever is worst off and worth healing, or null when nobody is.</summary>
    /// <param name="context">The decision being made.</param>
    /// <param name="below">Health below which someone counts as needing a heal.</param>
    /// <param name="range">How far the heal reaches.</param>
    public static UnitSnapshot? MostHurt(
        ICombatContext context,
        double below = 100d,
        float range = HealRange)
    {
        ArgumentNullException.ThrowIfNull(context);

        UnitSnapshot? worst = null;

        // The character itself first, so that a solo healer behaves exactly as it did before
        // groups existed and a grouped one does not forget about itself.
        if (context.Me.IsAlive && context.Me.HealthPercent < below)
        {
            worst = context.Me;
        }

        foreach (UnitSnapshot member in context.Group)
        {
            if (!member.IsAlive || member.Distance > range || member.HealthPercent >= below)
            {
                continue;
            }

            if (worst is null || member.HealthPercent < worst.Value.HealthPercent)
            {
                worst = member;
            }
        }

        return worst;
    }

    /// <summary>How many of the group, the character included, are below a health level.</summary>
    /// <remarks>
    /// What a group heal is worth casting for. Casting one to save a single person wastes both
    /// the mana and the cooldown.
    /// </remarks>
    public static int CountBelow(ICombatContext context, double below, float range = HealRange)
    {
        ArgumentNullException.ThrowIfNull(context);

        int count = context.Me.IsAlive && context.Me.HealthPercent < below ? 1 : 0;

        foreach (UnitSnapshot member in context.Group)
        {
            if (member.IsAlive && member.Distance <= range && member.HealthPercent < below)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>True when anyone in the group, the character included, is below a health level.</summary>
    public static bool AnyoneBelow(ICombatContext context, double below, float range = HealRange) =>
        MostHurt(context, below, range) is not null;
}
