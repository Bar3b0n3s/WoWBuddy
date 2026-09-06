using WoWBuddy.Behavior;
using WoWBuddy.BotBases.Support;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Objects;

namespace WoWBuddy.BotBases.Group;

/// <summary>How the character plays its part in a group.</summary>
public sealed record DungeonSettings
{
    /// <summary>How closely to keep up with whoever is being followed.</summary>
    public FollowSettings Follow { get; init; } = new();

    /// <summary>
    /// Where a tank walks the group, in order. Empty means it follows instead of leading.
    /// </summary>
    /// <remarks>
    /// A route is per-dungeon data, so it comes from a profile rather than from this project.
    /// Without one the bot cannot lead a run: it does not know where the instance goes, and
    /// guessing means walking a party into a wall. Following is the honest fallback.
    /// </remarks>
    public IReadOnlyList<Vector3> Route { get; init; } = [];

    /// <summary>How far from the anchor a healer or ranged character stands.</summary>
    public float StandOffRange { get; init; } = 25f;

    /// <summary>How far the group may be spread before a tank stops pulling.</summary>
    /// <remarks>
    /// Pulling with the healer three rooms back is the most reliable way to wipe a group, and
    /// it is entirely avoidable: waiting costs seconds, a wipe costs a run.
    /// </remarks>
    public float GroupWaitDistance { get; init; } = 30f;

    /// <summary>How far from the current route point a tank will pull.</summary>
    public float PullSearchRange { get; init; } = 30f;

    /// <summary>How close counts as having reached a route point.</summary>
    public float RouteArrivalRange { get; init; } = 8f;

    /// <summary>Creature template ids never to attack.</summary>
    public IReadOnlySet<uint> AvoidEntries { get; init; } = new HashSet<uint>();
}

/// <summary>
/// Plays a part in a group: follows, assists, and stays out of the way.
/// </summary>
/// <remarks>
/// <para>
/// What this base is really for is not being the reason a group fails. A bot in a party is
/// judged by four other people in real time, and the ways it can go wrong are all social:
/// pulling extra packs, breaking crowd control, standing in the wrong place, being three rooms
/// behind. So the rules here are deliberately conservative — attack only what the anchor
/// attacks, never pull anything of your own unless tanking, and wait for the group before
/// starting anything.
/// </para>
/// <para>
/// <b>What is deliberately not here: boss encounters.</b> Every fight worth scripting is
/// scripted differently, and the data that would drive it — which spell to run from, where the
/// safe ground is, when the add spawns — is per-encounter game data this project does not ship
/// and cannot invent. A bot that pretended otherwise would look competent right up until it
/// stood in the fire. The base fights bosses exactly as it fights anything else, and that is
/// stated rather than hidden.
/// </para>
/// </remarks>
public sealed class DungeonBotBase
{
    private readonly DungeonSettings _settings;
    private readonly FollowController _follow;
    private int _routeIndex;
    private bool _warnedAboutNoGroup;
    private bool _warnedAboutNoRoute;

    public DungeonBotBase(DungeonSettings? settings = null)
    {
        _settings = settings ?? new DungeonSettings();
        _follow = new FollowController(_settings.Follow);
    }

    /// <summary>How the character is playing its part.</summary>
    public DungeonSettings Settings => _settings;

    /// <summary>The route point a tank is heading for, or null when there is no route.</summary>
    public Vector3? CurrentRoutePoint =>
        _settings.Route.Count > 0 ? _settings.Route[_routeIndex] : null;

    /// <summary>Moves on to the next point of the route.</summary>
    public void AdvanceRoute()
    {
        if (_settings.Route.Count == 0)
        {
            return;
        }

        _routeIndex = (_routeIndex + 1) % _settings.Route.Count;
    }

    /// <summary>
    /// True when the group is together enough to start something.
    /// </summary>
    /// <remarks>
    /// Everyone alive, connected and within the wait distance. Dead members do not count:
    /// waiting for a corpse run before every pull would stop the run entirely, and the group's
    /// own decision to keep going is not the bot's to override.
    /// </remarks>
    public bool IsGroupReady(IBotState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        foreach (PartyMember member in state.Party.Members)
        {
            if (!member.IsAlive || !member.IsOnline)
            {
                continue;
            }

            if (member.Distance > _settings.GroupWaitDistance)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Picks what to attack, according to the character's role.</summary>
    public CandidateTarget? SelectTarget(IBotState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        // A tank picks its own targets — that is the job. Everyone else assists, because a
        // damage character choosing its own target breaks crowd control and pulls the next
        // room, in front of four people who will notice.
        if (state.Party.MyRole != PartyRole.Tank)
        {
            return AssistTargeting.Choose(state, state.Party.Anchor());
        }

        return state.NearbyEnemies
            .Where(enemy => enemy.IsAlive)
            .Where(enemy => !_settings.AvoidEntries.Contains(enemy.Entry))
            .Where(enemy => enemy.Distance <= _settings.PullSearchRange)
            .OrderBy(enemy => enemy.Distance)
            .Select(enemy => (CandidateTarget?)enemy)
            .FirstOrDefault();
    }

    /// <summary>Builds the subtree the root tree runs when nothing has gone wrong.</summary>
    public Node<IBotState> Build() =>
        new PrioritySelector<IBotState>(
            // Not in a group at all. Almost always a misconfiguration — this base has nothing
            // to do alone — so say it once and stand still rather than wander off.
            new If<IBotState>(
                state => !state.Party.IsInGroup(),
                new Do<IBotState>(state =>
                {
                    if (!_warnedAboutNoGroup)
                    {
                        _warnedAboutNoGroup = true;
                        Log.For<DungeonBotBase>().Warning(
                            "The dungeon base is running without a group, so there is nobody to "
                            + "follow or assist. Join a group, or choose a different bot base.");
                    }

                    state.StopMoving();
                    return RunStatus.Running;
                })
                { Name = "No group" })
            { Name = "Alone" },

            Engagement.Build(),

            new Do<IBotState>(Act) { Name = "Play the role" })
        { Name = "Dungeon" };

    private RunStatus Act(IBotState state)
    {
        if (SelectTarget(state) is { } target && ShouldEngage(state))
        {
            return state.SetTarget(target.Guid) ? RunStatus.Running : RunStatus.Failure;
        }

        return state.Party.MyRole switch
        {
            PartyRole.Tank => Lead(state),
            PartyRole.Healer => HoldPosition(state),
            _ => Keep(state),
        };
    }

    /// <summary>
    /// True when starting a fight is the character's to start.
    /// </summary>
    /// <remarks>
    /// A tank waits for the group; everyone else is only ever reacting to something the tank
    /// or the world already started, so there is nothing for them to wait for.
    /// </remarks>
    private bool ShouldEngage(IBotState state) =>
        state.Party.MyRole != PartyRole.Tank || IsGroupReady(state);

    /// <summary>Walks the route, which is what a tank does between pulls.</summary>
    private RunStatus Lead(IBotState state)
    {
        if (_settings.Route.Count == 0)
        {
            if (!_warnedAboutNoRoute)
            {
                _warnedAboutNoRoute = true;
                Log.For<DungeonBotBase>().Warning(
                    "Tanking without a route. This bot does not know where the instance goes — "
                    + "that is per-dungeon data it does not ship — so it will follow instead. "
                    + "Give the profile a route to lead.");
            }

            return Keep(state);
        }

        if (!IsGroupReady(state))
        {
            // Waiting costs seconds. Pulling with the healer three rooms back costs the run.
            state.StopMoving();
            return RunStatus.Running;
        }

        Vector3 destination = _settings.Route[_routeIndex];

        if (state.Position.Distance(destination) <= _settings.RouteArrivalRange)
        {
            AdvanceRoute();
            destination = _settings.Route[_routeIndex];
        }

        if (state.MovementFailed)
        {
            Log.For<DungeonBotBase>().Warning(
                "Could not reach route point {Point}; trying the next one", destination);
            AdvanceRoute();
            return RunStatus.Failure;
        }

        return state.MoveTo(destination) ? RunStatus.Running : RunStatus.Failure;
    }

    /// <summary>Stays at healing range of whoever the group is following.</summary>
    private RunStatus HoldPosition(IBotState state)
    {
        if (state.Party.Anchor() is not { } anchor)
        {
            return Keep(state);
        }

        // Close enough to heal, far enough not to be hit. Only move when actually outside the
        // band: a healer that repositions every tick is not casting.
        if (anchor.Distance <= _settings.StandOffRange)
        {
            state.StopMoving();
            return RunStatus.Running;
        }

        if (_follow.Decide(anchor.Distance) == FollowAction.GiveUp)
        {
            return LostTheGroup(state, anchor);
        }

        Vector3 spot = FollowController.StandOff(
            anchor.Position, state.Position, _settings.StandOffRange);

        return state.MoveTo(spot) ? RunStatus.Running : RunStatus.Failure;
    }

    /// <summary>Keeps up with whoever the group is following.</summary>
    private RunStatus Keep(IBotState state)
    {
        if (state.Party.Anchor() is not { } anchor)
        {
            state.StopMoving();
            return RunStatus.Running;
        }

        switch (_follow.Decide(anchor.Distance))
        {
            case FollowAction.Close:
                // The client's own follow keeps a sensible distance, handles doorways, and
                // looks like a person following someone. Pathing to a moving target does none
                // of that, so it is only the fallback.
                if (state.Party.Follow(anchor.Guid))
                {
                    return RunStatus.Running;
                }

                return state.MoveTo(anchor.Position) ? RunStatus.Running : RunStatus.Failure;

            case FollowAction.GiveUp:
                return LostTheGroup(state, anchor);

            default:
                state.StopMoving();
                return RunStatus.Running;
        }
    }

    private static RunStatus LostTheGroup(IBotState state, PartyMember anchor)
    {
        // Far enough away that they have zoned, hearthed or died somewhere else. Walking
        // across a continent to catch up is worse than stopping and saying so.
        Log.For<DungeonBotBase>().Warning(
            "{Name} is {Distance:F0} yards away, which is too far to follow. Stopping.",
            anchor.Name, anchor.Distance);

        state.StopMoving();
        return RunStatus.Failure;
    }
}
