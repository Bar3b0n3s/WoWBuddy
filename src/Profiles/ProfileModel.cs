using WoWBuddy.Common.Geometry;

namespace WoWBuddy.Profiles;

/// <summary>Which side a profile is written for.</summary>
public enum ProfileFaction
{
    /// <summary>Either side can run it.</summary>
    Any = 0,

    Alliance = 1,

    Horde = 2,
}

/// <summary>What a step in a quest order does.</summary>
public enum StepKind
{
    /// <summary>Take a quest from a giver.</summary>
    PickUp,

    /// <summary>Hand a quest in.</summary>
    TurnIn,

    /// <summary>Work on a quest's objective.</summary>
    Objective,

    /// <summary>Walk somewhere.</summary>
    RunTo,

    /// <summary>Grind in an area until the step's conditions stop holding.</summary>
    Grind,

    /// <summary>Run the child steps in order for as long as the conditions hold.</summary>
    Repeat,

    /// <summary>Run a named scripted behaviour the profile author asked for.</summary>
    CustomBehavior,
}

/// <summary>What an objective step asks the character to do.</summary>
public enum ObjectiveKind
{
    /// <summary>Kill things until the quest log says enough.</summary>
    Kill,

    /// <summary>Kill things and loot them for an item.</summary>
    Collect,

    /// <summary>Interact with a world object.</summary>
    Interact,

    /// <summary>Use an item, on a target or on the ground.</summary>
    UseItem,

    /// <summary>Talk to a creature.</summary>
    Gossip,

    /// <summary>Walk somewhere to discover it.</summary>
    Explore,

    /// <summary>
    /// Something the profile could not express. The bot works the area and waits for the
    /// quest log to change rather than pretending it knows what to do.
    /// </summary>
    Unknown,
}

/// <summary>Somewhere the bot can buy, sell, repair or post.</summary>
/// <param name="Name">Its name, for logs.</param>
/// <param name="Entry">Its creature or game object template id.</param>
/// <param name="MapId">Which map it is on.</param>
/// <param name="Position">Where it is.</param>
/// <param name="CanRepair">Whether it repairs.</param>
/// <param name="IsMailbox">Whether it is a mailbox rather than a creature.</param>
public sealed record ProfileVendor(
    string Name,
    uint Entry,
    int MapId,
    Vector3 Position,
    bool CanRepair = false,
    bool IsMailbox = false);

/// <summary>Somewhere the bot must not go.</summary>
/// <param name="Position">Its centre.</param>
/// <param name="Radius">How far the exclusion reaches, in yards.</param>
/// <param name="MapId">Which map it is on.</param>
/// <param name="Reason">Why, for the author's own benefit.</param>
public sealed record ProfileBlackspot(Vector3 Position, float Radius, int MapId = 0, string Reason = "")
{
    /// <summary>True when <paramref name="position"/> is inside the blackspot on this map.</summary>
    public bool Contains(int mapId, Vector3 position) =>
        mapId == MapId && position.Distance(Position) <= Radius;
}

/// <summary>One step of a quest order.</summary>
/// <param name="Kind">What the step does.</param>
/// <param name="QuestId">The quest it relates to, where it relates to one.</param>
/// <param name="QuestName">Its name, for logs.</param>
/// <param name="Entry">The creature or game object involved.</param>
/// <param name="Position">Where to go.</param>
/// <param name="MapId">Which map.</param>
/// <param name="Objective">What kind of objective, for objective steps.</param>
/// <param name="ObjectiveIndex">Which of the quest's objectives, one-based, or 0 for all of them.</param>
/// <param name="Count">How many are needed, where the profile says.</param>
/// <param name="ItemId">The item collected or used.</param>
/// <param name="Radius">How far around the position to work, in yards.</param>
/// <param name="RewardIndex">Which quest reward to take, 1-based, or 0 for no choice.</param>
/// <param name="Conditions">Every one of these must hold for the step to run.</param>
/// <param name="BehaviorName">The scripted behaviour to run, for custom steps.</param>
/// <param name="Arguments">Whatever attributes that behaviour was given.</param>
/// <param name="Hotspots">Points to move between while working the step.</param>
/// <param name="Children">Steps run in order, for <see cref="StepKind.Repeat"/>.</param>
/// <param name="LineNumber">Where the step was written, for messages.</param>
public sealed record ProfileStep(
    StepKind Kind,
    uint QuestId = 0,
    string QuestName = "",
    uint Entry = 0,
    Vector3 Position = default,
    int MapId = 0,
    ObjectiveKind Objective = ObjectiveKind.Unknown,
    int ObjectiveIndex = 0,
    int Count = 0,
    uint ItemId = 0,
    float Radius = 0f,
    int RewardIndex = 0,
    IReadOnlyList<ProfileCondition>? Conditions = null,
    string BehaviorName = "",
    IReadOnlyDictionary<string, string>? Arguments = null,
    IReadOnlyList<Vector3>? Hotspots = null,
    IReadOnlyList<ProfileStep>? Children = null,
    int LineNumber = 0)
{
    /// <summary>Every one of these must hold for the step to run.</summary>
    public IReadOnlyList<ProfileCondition> When => Conditions ?? [];

    /// <summary>Points to move between while working this step.</summary>
    public IReadOnlyList<Vector3> Spots => Hotspots ?? [];

    /// <summary>Steps run in order, for <see cref="StepKind.Repeat"/>.</summary>
    public IReadOnlyList<ProfileStep> Inner => Children ?? [];

    /// <summary>Whatever attributes a custom behaviour was given.</summary>
    public IReadOnlyDictionary<string, string> Args =>
        Arguments ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when every condition holds.</summary>
    public bool ConditionsHold(IProfileConditionContext context)
    {
        foreach (ProfileCondition condition in When)
        {
            if (!condition.Evaluate(context))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Where the step wants the character to be, including its hotspots.</summary>
    public IEnumerable<Vector3> AllPositions()
    {
        if (!Position.IsZero)
        {
            yield return Position;
        }

        foreach (Vector3 spot in Spots)
        {
            yield return spot;
        }
    }

    public override string ToString() => Kind switch
    {
        StepKind.PickUp => $"PickUp quest {QuestId} ({QuestName})",
        StepKind.TurnIn => $"TurnIn quest {QuestId} ({QuestName})",
        StepKind.Objective => $"{Objective} for quest {QuestId} ({QuestName})",
        StepKind.RunTo => $"RunTo {Position}",
        StepKind.Grind => $"Grind at {Position}",
        StepKind.Repeat => $"Repeat {Inner.Count} step(s)",
        StepKind.CustomBehavior => $"CustomBehavior {BehaviorName}",
        _ => Kind.ToString(),
    };
}

/// <summary>A questing or grinding plan.</summary>
public sealed record Profile
{
    /// <summary>Its name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Who wrote it.</summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>Which side it is for.</summary>
    public ProfileFaction Faction { get; init; } = ProfileFaction.Any;

    /// <summary>Lowest level it is written for.</summary>
    public int MinimumLevel { get; init; } = 1;

    /// <summary>Highest level it is written for.</summary>
    public int MaximumLevel { get; init; } = 80;

    /// <summary>The steps, in order.</summary>
    public IReadOnlyList<ProfileStep> Steps { get; init; } = [];

    /// <summary>Vendors, repairers and mailboxes the profile knows about.</summary>
    public IReadOnlyList<ProfileVendor> Vendors { get; init; } = [];

    /// <summary>Places the bot must not go.</summary>
    public IReadOnlyList<ProfileBlackspot> Blackspots { get; init; } = [];

    /// <summary>Creature entries never to attack.</summary>
    public IReadOnlySet<uint> AvoidMobs { get; init; } = new HashSet<uint>();

    /// <summary>Where the profile was read from, for logs.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>True when a character of this level is in range for the profile.</summary>
    public bool SuitsLevel(int level) => level >= MinimumLevel && level <= MaximumLevel;

    /// <summary>True when a character of this faction can use the profile.</summary>
    public bool SuitsFaction(ProfileFaction faction) =>
        Faction == ProfileFaction.Any || faction == ProfileFaction.Any || Faction == faction;

    /// <summary>Every step, including the children of repeats, in written order.</summary>
    public IEnumerable<ProfileStep> AllSteps()
    {
        static IEnumerable<ProfileStep> Walk(IReadOnlyList<ProfileStep> steps)
        {
            foreach (ProfileStep step in steps)
            {
                yield return step;

                foreach (ProfileStep child in Walk(step.Inner))
                {
                    yield return child;
                }
            }
        }

        return Walk(Steps);
    }

    /// <summary>True when any blackspot covers <paramref name="position"/>.</summary>
    public bool IsBlacklisted(int mapId, Vector3 position)
    {
        foreach (ProfileBlackspot blackspot in Blackspots)
        {
            if (blackspot.Contains(mapId, position))
            {
                return true;
            }
        }

        return false;
    }
}
