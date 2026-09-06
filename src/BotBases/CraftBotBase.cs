using WoWBuddy.Behavior;
using WoWBuddy.BotBases.Support;
using WoWBuddy.Common.Logging;

namespace WoWBuddy.BotBases;

/// <summary>What to make, and how much of it.</summary>
public sealed record CraftSettings
{
    /// <summary>The profession to open, as the client names it.</summary>
    /// <remarks>
    /// Required, and in the client's own language. There is no list of professions in this
    /// project to check it against, so a wrong name simply opens nothing and the base says so.
    /// </remarks>
    public string Profession { get; init; } = string.Empty;

    /// <summary>
    /// What to make. Empty means whatever raises the skill fastest.
    /// </summary>
    public string Recipe { get; init; } = string.Empty;

    /// <summary>How many to make in one go before looking at the list again.</summary>
    /// <remarks>
    /// The client queues them and casts them in sequence. A large batch is fewer round trips;
    /// a small one notices sooner that the skill went up and something better is now available.
    /// </remarks>
    public int BatchSize { get; init; } = 5;

    /// <summary>Stop once the skill reaches the cap for its current training.</summary>
    /// <remarks>
    /// Carrying on past the cap consumes materials and raises nothing. The character needs a
    /// trainer, which this project cannot take it to.
    /// </remarks>
    public bool StopAtCap { get; init; } = true;

    /// <summary>True when there is enough here to run.</summary>
    public bool IsUsable => Profession.Length > 0 && BatchSize > 0;
}

/// <summary>Why the crafting base stopped.</summary>
public enum CraftStop
{
    /// <summary>It has not.</summary>
    None,

    /// <summary>No profession was named, or the batch size makes no sense.</summary>
    NotConfigured,

    /// <summary>The window would not open. Usually the profession name is wrong.</summary>
    NoWindow,

    /// <summary>The skill has reached the cap for its current training.</summary>
    Capped,

    /// <summary>Nothing left to make it from.</summary>
    OutOfMaterials,

    /// <summary>The named recipe is not one the character knows.</summary>
    UnknownRecipe,
}

/// <summary>
/// Standing at an anvil and working through a queue.
/// </summary>
/// <remarks>
/// <para>
/// The simplest bot base here, because the client does the hard part: it knows what the
/// character can make and what the bags can make it from, so this only has to decide what is
/// worth making and keep asking.
/// </para>
/// <para>
/// <b>It does not buy materials.</b> It makes what the character is carrying and then stops
/// with a reason, which is a better outcome than standing at a vendor working out what a recipe
/// needs — that would take item data this project does not ship.
/// </para>
/// <para>
/// Every way it can stop is a named reason rather than silence, because a crafting session that
/// quietly does nothing looks exactly like one that is working.
/// </para>
/// </remarks>
public sealed class CraftBotBase
{
    private readonly CraftSettings _settings;

    private CraftStop _stopped = CraftStop.None;
    private bool _reported;

    public CraftBotBase(CraftSettings? settings = null)
    {
        _settings = settings ?? new CraftSettings();
    }

    /// <summary>What it is making.</summary>
    public CraftSettings Settings => _settings;

    /// <summary>Why it stopped, or <see cref="CraftStop.None"/> while it is working.</summary>
    public CraftStop Stopped => _stopped;

    /// <summary>How many it has asked the client to make.</summary>
    public int Crafted { get; private set; }

    /// <summary>Builds the subtree the root tree runs.</summary>
    /// <param name="skills">The character's professions.</param>
    public Node<IBotState> Build(ITradeSkills skills)
    {
        ArgumentNullException.ThrowIfNull(skills);

        return new Do<IBotState>(state => Tick(state, skills)) { Name = "Craft" };
    }

    /// <summary>Forgets that it stopped, so it can be started again.</summary>
    public void Reset()
    {
        _stopped = CraftStop.None;
        _reported = false;
        Crafted = 0;
    }

    private RunStatus Tick(IBotState state, ITradeSkills skills)
    {
        if (_stopped != CraftStop.None)
        {
            return RunStatus.Failure;
        }

        if (!_settings.IsUsable)
        {
            return Stop(CraftStop.NotConfigured, "No profession was named.");
        }

        if (!skills.Line.IsOpen)
        {
            // Opening is a cast, so it takes a moment; running rather than failing gives it
            // that moment, and the next tick finds the window.
            return skills.Open(_settings.Profession)
                ? RunStatus.Running
                : Stop(CraftStop.NoWindow, $"Could not open {_settings.Profession}.");
        }

        if (_settings.StopAtCap && skills.Line.IsCapped)
        {
            return Stop(
                CraftStop.Capped,
                $"{skills.Line.Name} is at {skills.Line.Rank}, the cap for this training. "
                + "The character needs a trainer.");
        }

        // Still casting the last batch. Asking again now would queue on top of it.
        if (state.Combat.IsCasting)
        {
            return RunStatus.Running;
        }

        if (Choose(skills) is not { } recipe)
        {
            return _settings.Recipe.Length > 0 && !Knows(skills, _settings.Recipe)
                ? Stop(CraftStop.UnknownRecipe, $"The character does not know how to make {_settings.Recipe}.")
                : Stop(CraftStop.OutOfMaterials, "Nothing left to make.");
        }

        int made = skills.Craft(recipe, _settings.BatchSize);

        if (made == 0)
        {
            return Stop(CraftStop.OutOfMaterials, $"Nothing left to make {recipe.Name} from.");
        }

        Crafted += made;
        return RunStatus.Running;
    }

    /// <summary>
    /// What to make next.
    /// </summary>
    /// <remarks>
    /// A named recipe is made until the materials run out. With no name, whatever raises the
    /// skill fastest — which changes as the skill goes up, so it is chosen fresh each batch
    /// rather than once.
    /// </remarks>
    private TradeSkillRecipe? Choose(ITradeSkills skills)
    {
        if (_settings.Recipe.Length == 0)
        {
            return skills.BestForSkillUp();
        }

        foreach (TradeSkillRecipe recipe in skills.Recipes)
        {
            if (string.Equals(recipe.Name, _settings.Recipe, StringComparison.OrdinalIgnoreCase)
                && recipe.CanMake)
            {
                return recipe;
            }
        }

        return null;
    }

    private static bool Knows(ITradeSkills skills, string name) =>
        skills.Recipes.Any(recipe =>
            string.Equals(recipe.Name, name, StringComparison.OrdinalIgnoreCase));

    private RunStatus Stop(CraftStop why, string message)
    {
        _stopped = why;

        if (!_reported)
        {
            _reported = true;
            Log.For<CraftBotBase>().Information("Crafting stopped: {Message}", message);
        }

        return RunStatus.Failure;
    }
}
