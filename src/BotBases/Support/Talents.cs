using System.Globalization;

namespace WoWBuddy.BotBases.Support;

/// <summary>One talent to put a point in.</summary>
/// <param name="Tab">Which tree, 1 to 3, in the order the client shows them.</param>
/// <param name="Index">Which talent within that tree, as the client numbers them.</param>
public readonly record struct TalentPick(int Tab, int Index)
{
    /// <summary>True when both numbers could refer to a real talent.</summary>
    /// <remarks>
    /// The upper bounds are not checked, because the number of talents in a tree differs by
    /// class and this project has no talent data. The client refuses an index it does not have,
    /// which is the honest place for that check to happen.
    /// </remarks>
    public bool IsPlausible => Tab is >= 1 and <= 3 && Index >= 1;

    public override string ToString() =>
        $"{Tab.ToString(CultureInfo.InvariantCulture)}:{Index.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// The order to spend talent points in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written by the user, because this project has no talent data and will not invent any.</b>
/// A build is a list of picks in the order they should be taken, and the bot works down it one
/// point at a time. Getting the order wrong wastes points that cost gold to get back, so
/// nothing here guesses: an unparseable build is rejected with the position that broke it, and
/// a build that runs out simply stops.
/// </para>
/// <para>
/// The format is deliberately the shortest thing that could work — <c>1:3, 1:3, 2:5</c>, meaning
/// two points in the third talent of the first tree, then one in the fifth of the second. It is
/// meant to be typed into a settings box, not authored in a tool.
/// </para>
/// </remarks>
public sealed record TalentBuild
{
    /// <summary>The picks, in the order to take them.</summary>
    public IReadOnlyList<TalentPick> Picks { get; init; } = [];

    /// <summary>True when there is anything to spend.</summary>
    public bool IsUsable => Picks.Count > 0;

    /// <summary>How many points the build accounts for.</summary>
    public int Points => Picks.Count;

    /// <summary>
    /// Reads a build from the compact form.
    /// </summary>
    /// <param name="text">Picks as <c>tab:index</c>, separated by commas or whitespace.</param>
    /// <param name="build">The build, when it parsed.</param>
    /// <param name="error">Which part could not be read, and why.</param>
    public static bool TryParse(string? text, out TalentBuild build, out string error)
    {
        build = new TalentBuild();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            // Nothing configured is not a mistake: most characters are levelled by hand.
            return true;
        }

        List<TalentPick> picks = [];

        foreach (string part in text.Split(new[] { ',', ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] halves = part.Split(':');

            if (halves.Length != 2
                || !int.TryParse(halves[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tab)
                || !int.TryParse(halves[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            {
                error = $"'{part}' is not a talent. Write each one as tab:index, such as 1:3.";
                return false;
            }

            TalentPick pick = new(tab, index);

            if (!pick.IsPlausible)
            {
                error = $"'{part}' names tree {tab}, and there are three trees numbered 1 to 3.";
                return false;
            }

            picks.Add(pick);
        }

        build = new TalentBuild { Picks = picks };
        return true;
    }

    /// <summary>The build written back out in the form it was read from.</summary>
    public override string ToString() => string.Join(", ", Picks);
}

/// <summary>Spending talent points.</summary>
/// <remarks>
/// Two calls, because that is all the bot needs: how many points are unspent, and put one here.
/// What a talent is called and what it does are not the bot's business.
/// </remarks>
public interface ITalents
{
    /// <summary>How many points the character has not spent.</summary>
    int UnspentPoints { get; }

    /// <summary>Puts a point in a talent. False when the client refused.</summary>
    bool Learn(TalentPick pick);
}
