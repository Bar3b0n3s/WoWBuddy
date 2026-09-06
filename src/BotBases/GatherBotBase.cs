using WoWBuddy.Behavior;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Objects;
using WoWBuddy.WorldData;

namespace WoWBuddy.BotBases;

/// <summary>What to gather and where.</summary>
public sealed record GatherSettings
{
    /// <summary>Game object entries to gather. Discovered from the user's own database.</summary>
    /// <remarks>
    /// No default list ships with the bot. Inventing one would be exactly the sort of
    /// unverified fact this project refuses, and node ids differ between server databases
    /// anyway. <c>WorldDataSet.FindTemplatesByName</c> is how a user finds them.
    /// </remarks>
    public IReadOnlySet<uint> NodeEntries { get; init; } = new HashSet<uint>();

    /// <summary>How far from the character to consider a node worth going to.</summary>
    public float SearchRadius { get; init; } = 300f;

    /// <summary>How close the character must be to interact with a node.</summary>
    public float InteractRange { get; init; } = 4f;

    /// <summary>
    /// How long to leave a node alone after visiting it.
    /// </summary>
    /// <remarks>
    /// Nodes respawn on a timer, and a bot that returns to one it has just emptied loops
    /// between two points forever. This is the single most important setting here.
    /// </remarks>
    public TimeSpan NodeCooldown { get; init; } = TimeSpan.FromMinutes(8);

    /// <summary>Whether to fight things that attack the character rather than fleeing.</summary>
    public bool FightBack { get; init; } = true;

    /// <summary>Places never to go, as a centre and a radius.</summary>
    public IReadOnlyList<(Vector3 Centre, float Radius)> Blackspots { get; init; } = [];

    /// <summary>Route to walk when nothing is in range, looping.</summary>
    public IReadOnlyList<Vector3> Route { get; init; } = [];
}

/// <summary>
/// Walks a route collecting nodes.
/// </summary>
/// <remarks>
/// <para>
/// Node positions come from the user's exported server database rather than from the client's
/// memory. Phase 1 could not establish where a game object keeps its position and refused to
/// guess; the database sidesteps the question entirely, and does it better — the bot knows
/// where every node in the zone spawns rather than only the handful currently in view.
/// </para>
/// <para>
/// The object manager is still needed for one thing: whether a node is actually there right
/// now. A spawn point in the database says a node appears there, not that it has respawned
/// since the last person emptied it.
/// </para>
/// </remarks>
public sealed class GatherBotBase
{
    private readonly GatherSettings _settings;
    private readonly WorldDataSet _worldData;
    private readonly Dictionary<uint, DateTimeOffset> _visited = [];
    private int _routeIndex;

    public GatherBotBase(GatherSettings settings, WorldDataSet worldData)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _worldData = worldData ?? throw new ArgumentNullException(nameof(worldData));
    }

    /// <summary>The point on the route the bot is heading for.</summary>
    public Vector3? CurrentRoutePoint =>
        _settings.Route.Count > 0 ? _settings.Route[_routeIndex] : null;

    /// <summary>Nodes visited recently and still on cooldown.</summary>
    public int NodesOnCooldown => _visited.Count;

    /// <summary>
    /// The nearest node worth going to, or null.
    /// </summary>
    /// <remarks>
    /// A node qualifies when the database says it spawns nearby, the client currently shows a
    /// game object of that entry, it is not blackspotted, and it has not just been emptied.
    /// The client check is what stops the bot walking to an empty spawn point.
    /// </remarks>
    public GameObjectSpawn? SelectNode(IBotState state, IReadOnlySet<uint> visibleEntries, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(visibleEntries);

        if (_settings.NodeEntries.Count == 0)
        {
            return null;
        }

        ExpireCooldowns(now);

        IReadOnlyList<GameObjectSpawn> nearby = _worldData.FindNodes(
            state.MapId, _settings.NodeEntries, state.Position, _settings.SearchRadius);

        foreach (GameObjectSpawn spawn in nearby)
        {
            if (_visited.ContainsKey(spawn.Guid))
            {
                continue;
            }

            // The database says a node spawns here; only the client knows whether one is
            // there now.
            if (!visibleEntries.Contains(spawn.Entry))
            {
                continue;
            }

            if (IsBlackspotted(spawn.Position))
            {
                continue;
            }

            return spawn;
        }

        return null;
    }

    /// <summary>Records that a node has been visited, so the bot leaves it alone for a while.</summary>
    public void NoteVisited(uint spawnGuid, DateTimeOffset now)
    {
        _visited[spawnGuid] = now + _settings.NodeCooldown;
        Log.For<GatherBotBase>().Debug("Node {Guid} on cooldown until {Until:HH:mm}", spawnGuid, _visited[spawnGuid]);
    }

    /// <summary>Moves to the next point on the route.</summary>
    public void AdvanceRoute()
    {
        if (_settings.Route.Count == 0)
        {
            return;
        }

        _routeIndex = (_routeIndex + 1) % _settings.Route.Count;
    }

    /// <summary>True when a position is somewhere the profile says not to go.</summary>
    public bool IsBlackspotted(Vector3 position) =>
        _settings.Blackspots.Any(spot => position.Distance(spot.Centre) <= spot.Radius);

    private void ExpireCooldowns(DateTimeOffset now)
    {
        if (_visited.Count == 0)
        {
            return;
        }

        foreach (uint guid in _visited.Where(entry => entry.Value <= now).Select(entry => entry.Key).ToList())
        {
            _visited.Remove(guid);
        }
    }

    /// <summary>Builds the subtree the root tree runs.</summary>
    public Node<IBotState> Build(Func<IBotState, IReadOnlySet<uint>> visibleEntries) =>
        new PrioritySelector<IBotState>(
            // Something attacking is dealt with by the root tree's combat branch; this only
            // decides whether to keep gathering while it happens.
            new If<IBotState>(
                state => SelectNode(state, visibleEntries(state), state.Now) is not null,
                new Do<IBotState>(state =>
                {
                    GameObjectSpawn node = SelectNode(state, visibleEntries(state), state.Now)!.Value;

                    if (state.Position.Distance(node.Position) > _settings.InteractRange)
                    {
                        return state.MoveTo(node.Position) ? RunStatus.Running : RunStatus.Failure;
                    }

                    state.StopMoving();
                    NoteVisited(node.Guid, state.Now);

                    // Gathering is an interaction with the object, which the movement layer
                    // performs by walking to it and interacting; the node is left on cooldown
                    // either way so a failure does not loop.
                    return RunStatus.Running;
                })
                { Name = "Gather the nearest node" })
            { Name = "A node is available" },

            new If<IBotState>(
                _ => _settings.Route.Count > 0,
                new Do<IBotState>(state =>
                {
                    Vector3 point = CurrentRoutePoint!.Value;

                    if (state.Position.Distance(point) <= _settings.InteractRange * 4f)
                    {
                        AdvanceRoute();
                        point = CurrentRoutePoint!.Value;
                    }

                    if (state.MovementFailed)
                    {
                        AdvanceRoute();
                        return RunStatus.Failure;
                    }

                    return state.MoveTo(point) ? RunStatus.Running : RunStatus.Failure;
                })
                { Name = "Walk the route" })
            { Name = "Has a route" })
        { Name = "Gather" };
}
