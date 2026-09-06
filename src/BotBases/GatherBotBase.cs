using WoWBuddy.Behavior;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;
using WoWBuddy.WorldData;

namespace WoWBuddy.BotBases;

/// <summary>What to gather and where.</summary>
public sealed record GatherSettings
{
    /// <summary>Game object entries to gather.</summary>
    /// <remarks>
    /// No default list ships with the bot: node ids differ between servers, and inventing one
    /// would be the same kind of unverified fact this project refuses elsewhere. A profile
    /// supplies them, or the user finds them by standing next to a node and reading what the
    /// inspector reports.
    /// </remarks>
    public IReadOnlySet<uint> NodeEntries { get; init; } = new HashSet<uint>();

    /// <summary>How far to go out of the way for a node the bot remembers.</summary>
    public float RememberedNodeRange { get; init; } = 150f;

    /// <summary>How close the character must be to interact with a node.</summary>
    public float InteractRange { get; init; } = 4f;

    /// <summary>
    /// How long to leave a node alone after visiting it.
    /// </summary>
    /// <remarks>
    /// Nodes respawn on a timer, and a bot that returns to one it has just emptied loops
    /// between two points forever. The single most important setting here.
    /// </remarks>
    public TimeSpan NodeCooldown { get; init; } = TimeSpan.FromMinutes(8);

    /// <summary>Places never to go, as a centre and a radius.</summary>
    public IReadOnlyList<(Vector3 Centre, float Radius)> Blackspots { get; init; } = [];

    /// <summary>Route to walk when nothing is in range, looping.</summary>
    public IReadOnlyList<Vector3> Route { get; init; } = [];
}

/// <summary>
/// Walks a route collecting nodes, and learns where they are as it goes.
/// </summary>
/// <remarks>
/// <para>
/// Two sources of nodes, in order of preference. What the client can see right now is the
/// only thing that proves a node is actually there; what the bot remembers from previous laps
/// tells it where to look when nothing is in view.
/// </para>
/// <para>
/// Everything it sees is written down. A first lap of a zone is slow and blind; by the third
/// the bot knows the route, which is roughly how a person learns one. Nothing external is
/// needed for this — no database, no downloaded node list — which matters because a person
/// botting on somebody else's realm has access to neither.
/// </para>
/// <para>
/// Nodes are only remembered when the client actually reported a position. That depends on
/// the game object position offset being worked out at attach; when it is not, gathering
/// reports that it cannot work rather than walking to zeroes.
/// </para>
/// </remarks>
public sealed class GatherBotBase
{
    private readonly GatherSettings _settings;
    private readonly WorldMemory _memory;
    private readonly Dictionary<ulong, DateTimeOffset> _recentlyVisited = [];
    private int _routeIndex;

    public GatherBotBase(GatherSettings settings, WorldMemory memory)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    /// <summary>The point on the route the bot is heading for.</summary>
    public Vector3? CurrentRoutePoint =>
        _settings.Route.Count > 0 ? _settings.Route[_routeIndex] : null;

    /// <summary>Nodes emptied recently and left alone for now.</summary>
    public int NodesOnCooldown => _recentlyVisited.Count;

    /// <summary>
    /// Writes down every gatherable node the client can currently see.
    /// </summary>
    /// <returns>How many were new.</returns>
    /// <remarks>
    /// Called every tick while gathering. This is the whole of the learning: walk past
    /// something once and the bot knows where it is next time.
    /// </remarks>
    public int LearnVisibleNodes(IBotState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        int learned = 0;

        foreach (VisibleObject visible in state.VisibleObjects)
        {
            if (!_settings.NodeEntries.Contains(visible.Entry) || !visible.HasPosition)
            {
                continue;
            }

            if (_memory.Remember(
                    RememberedKind.Node, visible.Entry, state.MapId, visible.Position,
                    name: string.Empty, state.Now))
            {
                learned++;
            }
        }

        return learned;
    }

    /// <summary>
    /// The nearest node actually in view and worth gathering, or null.
    /// </summary>
    /// <remarks>
    /// Only things the client can see. A remembered position says a node spawns there, not
    /// that it has respawned since the last person emptied it.
    /// </remarks>
    public VisibleObject? SelectVisibleNode(IBotState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);

        ExpireCooldowns(now);

        return state.VisibleObjects
            .Where(visible => _settings.NodeEntries.Contains(visible.Entry))
            .Where(visible => visible.HasPosition)
            .Where(visible => !_recentlyVisited.ContainsKey(visible.Guid.Value))
            .Where(visible => !IsBlackspotted(visible.Position))
            .OrderBy(visible => visible.Distance)
            .Select(visible => (VisibleObject?)visible)
            .FirstOrDefault();
    }

    /// <summary>
    /// A remembered node worth walking to when nothing is in view.
    /// </summary>
    /// <remarks>
    /// Speculative by nature: the node may have been taken. Walking there is still better
    /// than walking a fixed route past nothing, and if it turns out to be empty the bot
    /// simply carries on.
    /// </remarks>
    public RememberedPlace? SelectRememberedNode(IBotState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);

        return _memory
            .Nearest(RememberedKind.Node, state.MapId, state.Position, now, limit: 16)
            .Where(place => _settings.NodeEntries.Contains(place.Entry))
            .Where(place => place.Position.Distance(state.Position) <= _settings.RememberedNodeRange)
            .Where(place => !IsBlackspotted(place.Position))
            .Select(place => (RememberedPlace?)place)
            .FirstOrDefault();
    }

    /// <summary>Records that a node has been emptied, so the bot leaves it alone for a while.</summary>
    public void NoteGathered(ulong guid, DateTimeOffset now)
    {
        _recentlyVisited[guid] = now + _settings.NodeCooldown;
        Log.For<GatherBotBase>().Debug("Node {Guid:X} left alone until {Until:HH:mm}", guid, _recentlyVisited[guid]);
    }

    /// <summary>Moves to the next point on the route.</summary>
    public void AdvanceRoute()
    {
        if (_settings.Route.Count > 0)
        {
            _routeIndex = (_routeIndex + 1) % _settings.Route.Count;
        }
    }

    /// <summary>True when a position is somewhere the profile says not to go.</summary>
    public bool IsBlackspotted(Vector3 position) =>
        _settings.Blackspots.Any(spot => position.Distance(spot.Centre) <= spot.Radius);

    private void ExpireCooldowns(DateTimeOffset now)
    {
        if (_recentlyVisited.Count == 0)
        {
            return;
        }

        foreach (ulong guid in _recentlyVisited
                     .Where(entry => entry.Value <= now)
                     .Select(entry => entry.Key)
                     .ToList())
        {
            _recentlyVisited.Remove(guid);
        }
    }

    /// <summary>Builds the subtree the root tree runs.</summary>
    public Node<IBotState> Build() =>
        new Sequence<IBotState>(
            // Learning happens on every tick, whatever the bot then decides to do, because
            // the character sees things while walking past them as much as while gathering.
            new Do<IBotState>(state =>
            {
                LearnVisibleNodes(state);
                return RunStatus.Success;
            })
            { Name = "Note what is in view" },

            new PrioritySelector<IBotState>(
                new If<IBotState>(
                    state => SelectVisibleNode(state, state.Now) is not null,
                    new Do<IBotState>(state =>
                    {
                        VisibleObject node = SelectVisibleNode(state, state.Now)!.Value;

                        if (node.Distance > _settings.InteractRange)
                        {
                            return state.MoveTo(node.Position) ? RunStatus.Running : RunStatus.Failure;
                        }

                        state.StopMoving();

                        // Marked as emptied whether or not the interaction succeeds, so a
                        // node that cannot be gathered does not trap the bot in a loop.
                        NoteGathered(node.Guid.Value, state.Now);
                        state.Interact(node.Guid);
                        return RunStatus.Running;
                    })
                    { Name = "Gather what is in view" })
                { Name = "A node is in view" },

                new If<IBotState>(
                    state => SelectRememberedNode(state, state.Now) is not null,
                    new Do<IBotState>(state =>
                    {
                        RememberedPlace place = SelectRememberedNode(state, state.Now)!;
                        return state.MoveTo(place.Position) ? RunStatus.Running : RunStatus.Failure;
                    })
                    { Name = "Go and look at a remembered node" })
                { Name = "Somewhere worth checking" },

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
            { Name = "Decide" })
        { Name = "Gather" };
}
