namespace WoWBuddy.BotBases.Support;

/// <summary>How much skill a recipe is still worth.</summary>
/// <remarks>
/// The client's own words, in its own colours: orange always gives a point, yellow usually,
/// green sometimes, grey never. A character levelling a profession wants the highest colour it
/// can still make; one making something to sell does not care.
/// </remarks>
public enum RecipeDifficulty
{
    /// <summary>The client said something this project does not recognise.</summary>
    Unknown,

    /// <summary>Orange. Always gives a skill point.</summary>
    Optimal,

    /// <summary>Yellow. Usually gives one.</summary>
    Medium,

    /// <summary>Green. Sometimes gives one.</summary>
    Easy,

    /// <summary>Grey. Never gives one.</summary>
    Trivial,
}

/// <summary>Something the character knows how to make.</summary>
/// <param name="Index">Its place in the client's list, which is what making it takes.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Difficulty">How much skill it is still worth.</param>
/// <param name="Available">How many the character has the materials for.</param>
public readonly record struct TradeSkillRecipe(
    int Index,
    string Name,
    RecipeDifficulty Difficulty,
    int Available)
{
    /// <summary>True when the character could make at least one right now.</summary>
    public bool CanMake => Available > 0;

    /// <summary>True when making it might still raise the skill.</summary>
    public bool RaisesSkill => Difficulty is RecipeDifficulty.Optimal
        or RecipeDifficulty.Medium
        or RecipeDifficulty.Easy;

    public override string ToString() => $"{Name} ({Difficulty}, {Available} makeable)";
}

/// <summary>Which profession window is open, and how far along it is.</summary>
/// <param name="Name">The profession, as the client names it.</param>
/// <param name="Rank">Current skill.</param>
/// <param name="MaxRank">What this rank of training allows.</param>
public readonly record struct TradeSkillLine(string Name, int Rank, int MaxRank)
{
    /// <summary>True when the character has hit the cap for its current training.</summary>
    public bool IsCapped => MaxRank > 0 && Rank >= MaxRank;

    /// <summary>True when a window is actually open.</summary>
    public bool IsOpen => !string.IsNullOrEmpty(Name);
}

/// <summary>
/// What the character can make, and making it.
/// </summary>
/// <remarks>
/// An interface so the crafting base can be written and tested without a client, and because
/// what a character can make comes from its own spellbook rather than from any data this
/// project ships.
/// </remarks>
public interface ITradeSkills
{
    /// <summary>Which profession window is open, and how far along it is.</summary>
    TradeSkillLine Line { get; }

    /// <summary>Everything the character knows how to make, headers excluded.</summary>
    IReadOnlyList<TradeSkillRecipe> Recipes { get; }

    /// <summary>Opens a profession's window by name, as the client names the profession.</summary>
    bool Open(string profession);

    /// <summary>The best thing to make to raise the skill, or null when nothing would.</summary>
    TradeSkillRecipe? BestForSkillUp();

    /// <summary>Makes something. Returns how many it asked for, or zero.</summary>
    int Craft(TradeSkillRecipe recipe, int count = 1);

    /// <summary>Closes the window.</summary>
    bool Close();
}
