using System.Globalization;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Execution;
using WoWBuddy.GameApi.Capabilities;

namespace WoWBuddy.Live;

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
/// What the character can make, read from the client's own trade skill window.
/// </summary>
/// <remarks>
/// <para>
/// <b>No recipe data ships with this project, and none is needed.</b> What a character can make
/// is in its own spellbook, and the client will list it — names, difficulty colours, and how
/// many the materials in the bags allow. That makes crafting one of the few things the bot can
/// do without asking the user to extract anything.
/// </para>
/// <para>
/// The window has to be open. This class does not open it: casting a profession is a spell like
/// any other, and belongs with the casting that already exists rather than being reimplemented
/// here.
/// </para>
/// </remarks>
public sealed class LuaTradeSkills
{
    /// <summary>Separates fields within a row.</summary>
    private const char FieldSeparator = '\u001F';

    /// <summary>Separates rows.</summary>
    private const char RowSeparator = '\u001E';

    /// <summary>How long a reading stays good for.</summary>
    /// <remarks>
    /// Long: a recipe list changes when something is made or a material is bought, and both
    /// clear the cache directly.
    /// </remarks>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Reads the open trade skill window in one pass.
    /// </summary>
    /// <remarks>
    /// The list interleaves category headers with recipes, and a header has the type "header";
    /// they are skipped, but they still occupy an index, so the index recorded is the client's
    /// own and not a position in the filtered list. Making something takes that index, and
    /// getting it wrong makes the wrong thing.
    /// </remarks>
    internal const string ReadScript = """
        local line, rank, maxRank = GetTradeSkillLine()
        local rows = { (line or "") .. "\31" .. (rank or 0) .. "\31" .. (maxRank or 0) }
        for i = 1, GetNumTradeSkills() do
            local name, kind, available = GetTradeSkillInfo(i)
            if name and kind and kind ~= "header" then
                rows[#rows + 1] = i .. "\31" .. name .. "\31" .. kind .. "\31" .. (available or 0)
            end
        end
        __wowbuddy_result = table.concat(rows, "\30")
        """;

    private readonly ILuaEvaluator _lua;
    private readonly CapabilityReport _capabilities;
    private readonly Func<DateTimeOffset> _clock;

    private IReadOnlyList<TradeSkillRecipe> _recipes = [];
    private TradeSkillLine _line;
    private DateTimeOffset _readAt = DateTimeOffset.MinValue;
    private bool _warned;

    /// <summary>Reads the trade skills of a client.</summary>
    public LuaTradeSkills(
        ILuaEvaluator lua,
        CapabilityReport capabilities,
        Func<DateTimeOffset>? clock = null)
    {
        _lua = lua ?? throw new ArgumentNullException(nameof(lua));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Which profession is open, and how far along it is.</summary>
    public TradeSkillLine Line
    {
        get
        {
            Refresh();
            return _line;
        }
    }

    /// <summary>Everything the character knows how to make, headers excluded.</summary>
    public IReadOnlyList<TradeSkillRecipe> Recipes
    {
        get
        {
            Refresh();
            return _recipes;
        }
    }

    /// <summary>How many times the window has actually been read.</summary>
    public int Reads { get; private set; }

    /// <summary>
    /// The best thing to make to raise the skill, or null when nothing would.
    /// </summary>
    /// <remarks>
    /// Highest colour first among the recipes there are materials for. Grey recipes are
    /// excluded outright: making them consumes materials and raises nothing, which is worse
    /// than stopping.
    /// </remarks>
    public TradeSkillRecipe? BestForSkillUp()
    {
        TradeSkillRecipe? best = null;

        foreach (TradeSkillRecipe recipe in Recipes)
        {
            if (!recipe.CanMake || !recipe.RaisesSkill)
            {
                continue;
            }

            if (best is null || recipe.Difficulty < best.Value.Difficulty)
            {
                best = recipe;
            }
        }

        return best;
    }

    /// <summary>Makes something, by name.</summary>
    /// <param name="name">The recipe, as the client names it.</param>
    /// <param name="count">How many. Clamped to what the materials allow.</param>
    /// <returns>How many it asked for, or zero when it could not.</returns>
    public int Craft(string name, int count = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        TradeSkillRecipe? recipe = null;

        foreach (TradeSkillRecipe candidate in Recipes)
        {
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                recipe = candidate;
                break;
            }
        }

        return recipe is { } found ? Craft(found, count) : 0;
    }

    /// <summary>Makes something the reader already found.</summary>
    public int Craft(TradeSkillRecipe recipe, int count = 1)
    {
        if (!Supports() || !recipe.CanMake || count <= 0)
        {
            return 0;
        }

        // Asking for more than the materials allow is not an error in the client, but it makes
        // the bot's own accounting wrong: it would believe it had queued twenty and stop
        // watching after five.
        int wanted = Math.Min(count, recipe.Available);

        _lua.Execute(
            $"DoTradeSkill({recipe.Index.ToString(CultureInfo.InvariantCulture)}, "
            + $"{wanted.ToString(CultureInfo.InvariantCulture)})");

        // Materials are gone, so every count in the list is now wrong.
        Invalidate();

        Log.For<LuaTradeSkills>().Information(
            "Making {Count} x {Recipe}", wanted, recipe.Name);

        return wanted;
    }

    /// <summary>Closes the window.</summary>
    public bool Close()
    {
        if (!Supports())
        {
            return false;
        }

        Invalidate();
        return _lua.Execute("CloseTradeSkill()");
    }

    /// <summary>Forgets the last reading.</summary>
    public void Invalidate() => _readAt = DateTimeOffset.MinValue;

    /// <summary>Turns the client's own word for a colour into a difficulty.</summary>
    internal static RecipeDifficulty ParseDifficulty(string kind) => kind.ToUpperInvariant() switch
    {
        "OPTIMAL" => RecipeDifficulty.Optimal,
        "MEDIUM" => RecipeDifficulty.Medium,
        "EASY" => RecipeDifficulty.Easy,
        "TRIVIAL" => RecipeDifficulty.Trivial,
        _ => RecipeDifficulty.Unknown,
    };

    private bool Supports()
    {
        if (_capabilities.Supports(GameCapability.TradeSkills))
        {
            return true;
        }

        if (!_warned)
        {
            _warned = true;
            Log.For<LuaTradeSkills>().Warning(
                "{Explanation}", _capabilities.Explain(GameCapability.TradeSkills));
        }

        return false;
    }

    private void Refresh()
    {
        DateTimeOffset now = _clock();

        if (now - _readAt < CacheLifetime)
        {
            return;
        }

        _readAt = now;

        if (!Supports())
        {
            Reset();
            return;
        }

        _lua.Execute(ReadScript);
        Reads++;

        string? raw = _lua.Evaluate("__wowbuddy_result");

        if (raw is null)
        {
            Reset();
            return;
        }

        Parse(raw);
    }

    private void Parse(string raw)
    {
        string[] rows = raw.Split(RowSeparator, StringSplitOptions.RemoveEmptyEntries);

        if (rows.Length == 0)
        {
            Reset();
            return;
        }

        string[] header = rows[0].Split(FieldSeparator);

        _line = header.Length >= 3
            ? new TradeSkillLine(header[0], ParseInt(header[1]), ParseInt(header[2]))
            : default;

        List<TradeSkillRecipe> recipes = [];

        foreach (string row in rows.Skip(1))
        {
            string[] fields = row.Split(FieldSeparator);

            if (fields.Length < 4 || !int.TryParse(fields[0], out int index) || index <= 0)
            {
                Log.For<LuaTradeSkills>().Warning(
                    "A trade skill row could not be read and was skipped: {Row}", row);
                continue;
            }

            recipes.Add(new TradeSkillRecipe(
                index,
                fields[1],
                ParseDifficulty(fields[2]),
                ParseInt(fields[3])));
        }

        _recipes = recipes;
    }

    private void Reset()
    {
        _recipes = [];
        _line = default;
    }

    private static int ParseInt(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
}
