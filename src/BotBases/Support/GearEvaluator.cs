namespace WoWBuddy.BotBases.Support;

/// <summary>
/// How much each stat is worth to a particular specialisation.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately crude. A proper answer needs a simulator and knowledge of the character's
/// whole set; what a grinding bot needs is to notice that the level 12 shoulders it just
/// looted beat the level 4 ones it is wearing, and to not equip a spellpower staff on a
/// warrior. Weights get that right and cost nothing.
/// </para>
/// <para>
/// Stat names are the client's own, from the item tooltip, so they are localised. That is a
/// real limitation and the reason item level is used as a tiebreaker rather than the whole
/// answer being stat-driven.
/// </para>
/// </remarks>
public sealed record StatWeights
{
    /// <summary>A name for the UI.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Weight per point of each stat, keyed by the client's stat name.</summary>
    public IReadOnlyDictionary<string, double> Weights { get; init; } =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Armour subclasses the character may wear, for example Plate or Cloth. Empty means any.
    /// </summary>
    /// <remarks>
    /// Without this a warrior happily equips cloth, because cloth with good stamina scores
    /// perfectly well on weights alone.
    /// </remarks>
    public IReadOnlySet<string> AllowedArmour { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Weapon subclasses the character may use. Empty means any.</summary>
    public IReadOnlySet<string> AllowedWeapons { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Scores an item's stats.</summary>
    public double Score(IReadOnlyDictionary<string, int> stats)
    {
        ArgumentNullException.ThrowIfNull(stats);

        double total = 0d;
        foreach ((string stat, int amount) in stats)
        {
            if (Weights.TryGetValue(stat, out double weight))
            {
                total += weight * amount;
            }
        }

        return total;
    }
}

/// <summary>An item being considered for a slot, with the stats the client reported.</summary>
/// <param name="Item">The item.</param>
/// <param name="Stats">Its stats, keyed by the client's stat names.</param>
public readonly record struct GearCandidate(ItemInfo Item, IReadOnlyDictionary<string, int> Stats);

/// <summary>Whether an item is an upgrade, and by how much.</summary>
/// <param name="IsUpgrade">Whether to equip it.</param>
/// <param name="CandidateScore">What the candidate scored.</param>
/// <param name="EquippedScore">What the currently equipped item scored.</param>
/// <param name="Reason">Why, in a sentence, for the log.</param>
public readonly record struct UpgradeDecision(
    bool IsUpgrade,
    double CandidateScore,
    double EquippedScore,
    string Reason);

/// <summary>
/// Decides whether a looted item beats what the character is wearing.
/// </summary>
/// <remarks>
/// Pure arithmetic over item data, so a whole levelling run's worth of decisions can be
/// checked without a character. That matters because the failure mode is quiet: a bot wearing
/// the wrong armour class kills things more slowly for hours without anything looking broken.
/// </remarks>
public sealed class GearEvaluator
{
    private readonly StatWeights _weights;

    /// <summary>Slots the user has asked the bot not to touch.</summary>
    private readonly IReadOnlySet<string> _lockedSlots;

    /// <param name="weights">What the specialisation values.</param>
    /// <param name="lockedSlots">
    /// Equip locations the bot must leave alone, for the heirloom or the trinket the user
    /// cares about more than the bot's arithmetic does.
    /// </param>
    public GearEvaluator(StatWeights weights, IReadOnlySet<string>? lockedSlots = null)
    {
        _weights = weights ?? throw new ArgumentNullException(nameof(weights));
        _lockedSlots = lockedSlots ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Decides whether to equip <paramref name="candidate"/> in place of what is worn.
    /// </summary>
    /// <param name="candidate">The looted item.</param>
    /// <param name="equipped">What is currently in that slot, or null when the slot is empty.</param>
    /// <param name="characterLevel">The character's level.</param>
    public UpgradeDecision Evaluate(GearCandidate candidate, GearCandidate? equipped, int characterLevel)
    {
        ItemInfo item = candidate.Item;

        if (!item.IsEquippable)
        {
            return new UpgradeDecision(false, 0d, 0d, "Not equippable.");
        }

        if (_lockedSlots.Contains(item.EquipSlot))
        {
            return new UpgradeDecision(false, 0d, 0d, $"{item.EquipSlot} is locked by the user.");
        }

        if (item.RequiredLevel > characterLevel)
        {
            return new UpgradeDecision(
                false, 0d, 0d, $"Requires level {item.RequiredLevel}; character is {characterLevel}.");
        }

        if (!IsUsableType(item))
        {
            return new UpgradeDecision(
                false, 0d, 0d, $"{item.ItemSubClass} is not something this specialisation uses.");
        }

        double candidateScore = _weights.Score(candidate.Stats);

        if (equipped is not { } worn)
        {
            // An empty slot is filled by anything usable. A blank slot is always worse than
            // something, even something poor.
            return new UpgradeDecision(true, candidateScore, 0d, "The slot is empty.");
        }

        double equippedScore = _weights.Score(worn.Stats);

        if (candidateScore > equippedScore)
        {
            return new UpgradeDecision(
                true, candidateScore, equippedScore,
                $"Scores {candidateScore:F1} against {equippedScore:F1}.");
        }

        // Weights say nothing useful when neither item has stats the specialisation values,
        // which happens constantly at low level. Item level is the tiebreaker then, and only
        // then: preferring item level generally would equip the wrong armour class.
        if (candidateScore == equippedScore && item.ItemLevel > worn.Item.ItemLevel)
        {
            return new UpgradeDecision(
                true, candidateScore, equippedScore,
                $"Same score; higher item level ({item.ItemLevel} against {worn.Item.ItemLevel}).");
        }

        return new UpgradeDecision(
            false, candidateScore, equippedScore,
            $"Scores {candidateScore:F1} against {equippedScore:F1}; not an upgrade.");
    }

    private bool IsUsableType(ItemInfo item)
    {
        bool isArmour = item.ItemClass.Equals("Armor", StringComparison.OrdinalIgnoreCase);
        bool isWeapon = item.ItemClass.Equals("Weapon", StringComparison.OrdinalIgnoreCase);

        if (isArmour && _weights.AllowedArmour.Count > 0)
        {
            // Rings, trinkets, necks and cloaks are classed as armour but have no armour
            // subclass restriction; the client reports them as Miscellaneous or Cloth.
            return _weights.AllowedArmour.Contains(item.ItemSubClass)
                || item.EquipSlot is "INVTYPE_FINGER" or "INVTYPE_TRINKET" or "INVTYPE_NECK" or "INVTYPE_CLOAK";
        }

        if (isWeapon && _weights.AllowedWeapons.Count > 0)
        {
            return _weights.AllowedWeapons.Contains(item.ItemSubClass);
        }

        return true;
    }
}
