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
/// <b>It buys materials when it can, and stops with a reason when it cannot.</b> The client
/// says what a recipe takes; the user's world data says who sells it; between them the base can
/// walk to a vendor, restock and carry on. Most trade materials are gathered rather than sold,
/// so stopping is still the ordinary outcome — but stopping for want of a stack of thread is
/// not, and that is the case this fixes.
/// </para>
/// <para>
/// Every way it can stop is a named reason rather than silence, because a crafting session that
/// quietly does nothing looks exactly like one that is working.
/// </para>
/// </remarks>
public sealed class CraftBotBase
{
    /// <summary>
    /// How many shopping trips are worth making before giving up.
    /// </summary>
    /// <remarks>
    /// Two, because a trip that comes back and still leaves the recipe unmakeable means the
    /// bot has misunderstood something — the wrong item id, a vendor that sells one of the four
    /// reagents — and a third trip would misunderstand it again. Walking back and forth all
    /// evening is worse than stopping with a reason.
    /// </remarks>
    public const int MaxSupplyRuns = 2;

    private readonly CraftSettings _settings;
    private readonly SupplyRun? _supply;

    private CraftStop _stopped = CraftStop.None;
    private bool _reported;
    private int _trips;

    /// <summary>Builds a crafting base.</summary>
    /// <param name="settings">What to make, and how much of it.</param>
    /// <param name="supply">
    /// How to restock when the materials run out. Omit and the base stops instead, which is
    /// what it did before world data existed and is still right when there is none.
    /// </param>
    public CraftBotBase(CraftSettings? settings = null, SupplyRun? supply = null)
    {
        _settings = settings ?? new CraftSettings();
        _supply = supply;
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
        _trips = 0;
        _supply?.Reset();
    }

    /// <summary>How many shopping trips it has made.</summary>
    public int SupplyRuns => _trips;

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

        // Before the window, because there is no window while the character is walking to a
        // shop: casting the profession again here would cancel its own journey every tick.
        if (ContinueTrip(state) is { } shopping)
        {
            return shopping;
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
            if (_settings.Recipe.Length > 0 && !Knows(skills, _settings.Recipe))
            {
                return Stop(
                    CraftStop.UnknownRecipe,
                    $"The character does not know how to make {_settings.Recipe}.");
            }

            return GoShopping(state, skills)
                ?? Stop(CraftStop.OutOfMaterials, ShoppingFailure("Nothing left to make."));
        }

        int made = skills.Craft(recipe, _settings.BatchSize);

        if (made == 0)
        {
            return GoShopping(state, skills)
                ?? Stop(
                    CraftStop.OutOfMaterials,
                    ShoppingFailure($"Nothing left to make {recipe.Name} from."));
        }

        Crafted += made;
        return RunStatus.Running;
    }

    /// <summary>
    /// Carries on a shopping trip that is already under way.
    /// </summary>
    /// <returns>What the caller should return, or null when there is no trip in progress.</returns>
    private RunStatus? ContinueTrip(IBotState state)
    {
        if (_supply is not { Status: SupplyStatus.Shopping })
        {
            return null;
        }

        SupplyStatus status = _supply.Tick(state);

        if (status == SupplyStatus.Shopping)
        {
            return RunStatus.Running;
        }

        // Captured before the reset, which clears it.
        string explanation = _supply.Explanation;
        int bought = _supply.Bought;

        _supply.Reset();

        if (status == SupplyStatus.Done)
        {
            Log.For<CraftBotBase>().Information(
                "Back from the shops with {Count} item(s); carrying on", bought);

            // Running rather than crafting immediately: the window closed while walking, and
            // the next tick reopens it against bags that have had a moment to catch up.
            return RunStatus.Running;
        }

        return Stop(CraftStop.OutOfMaterials, explanation);
    }

    /// <summary>
    /// Sets off to buy what the recipe is short of.
    /// </summary>
    /// <returns>What the caller should return, or null when shopping is not possible.</returns>
    private RunStatus? GoShopping(IBotState state, ITradeSkills skills)
    {
        if (_supply is null || _trips >= MaxSupplyRuns)
        {
            return null;
        }

        // What it would make if it had the materials, which is not what Choose answers: Choose
        // only offers recipes there are materials for, and by here there are none.
        if (Wanted(skills) is not { } recipe)
        {
            return null;
        }

        if (!_supply.Begin(state, skills, recipe, _settings.BatchSize))
        {
            Log.For<CraftBotBase>().Information(
                "Not going shopping: {Reason}", _supply.Explanation);

            return null;
        }

        _trips++;
        return RunStatus.Running;
    }

    /// <summary>Adds why the shopping did not save the day, when it was tried.</summary>
    private string ShoppingFailure(string message) =>
        _trips > 0 ? $"{message} {_trips} shopping trip(s) did not fix it." : message;

    /// <summary>
    /// What the character would make if it had the materials.
    /// </summary>
    /// <remarks>
    /// Availability is ignored deliberately. This is only asked when nothing is makeable, and
    /// the question being answered is what to go shopping for.
    /// </remarks>
    private TradeSkillRecipe? Wanted(ITradeSkills skills)
    {
        if (_settings.Recipe.Length > 0)
        {
            foreach (TradeSkillRecipe recipe in skills.Recipes)
            {
                if (string.Equals(recipe.Name, _settings.Recipe, StringComparison.OrdinalIgnoreCase))
                {
                    return recipe;
                }
            }

            return null;
        }

        TradeSkillRecipe? best = null;

        foreach (TradeSkillRecipe recipe in skills.Recipes)
        {
            if (recipe.RaisesSkill && (best is null || recipe.Difficulty < best.Value.Difficulty))
            {
                best = recipe;
            }
        }

        return best;
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
