using WoWBuddy.Behavior;
using WoWBuddy.Common.Logging;

namespace WoWBuddy.BotBases.Activities;

/// <summary>When to break off what you are doing for a node you happened to pass.</summary>
public sealed record OpportunisticSettings
{
    /// <summary>Game object entries worth stopping for.</summary>
    /// <remarks>
    /// Empty by default, and nothing is assumed: node ids differ between servers, and inventing
    /// a list would be the same kind of unverified fact this project refuses elsewhere.
    /// </remarks>
    public IReadOnlySet<uint> NodeEntries { get; init; } = new HashSet<uint>();

    /// <summary>
    /// How far out of the way to go, in yards.
    /// </summary>
    /// <remarks>
    /// Deliberately short. The point of this is "there is a vein on the way", not "abandon the
    /// quest and go mining" — that is what a gathering activity in the plan is for. A large
    /// radius turns a questing character into a bad gatherer that never finishes anything.
    /// </remarks>
    public float DetourRange { get; init; } = 40f;

    /// <summary>How close the character must be to interact.</summary>
    public float InteractRange { get; init; } = 4f;

    /// <summary>How long to leave a node alone after visiting it.</summary>
    public TimeSpan NodeCooldown { get; init; } = TimeSpan.FromMinutes(8);

    /// <summary>
    /// How many nodes to take in a row before getting on with the main task.
    /// </summary>
    /// <remarks>
    /// A dense field would otherwise hold a questing character indefinitely: every node it
    /// walks to puts it within range of the next. The cap is what guarantees the main task
    /// makes progress.
    /// </remarks>
    public int ConsecutiveLimit { get; init; } = 3;

    /// <summary>How long to get on with the main task before detouring again.</summary>
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>True when there is anything to look for.</summary>
    public bool IsUsable => NodeEntries.Count > 0 && DetourRange > InteractRange;
}

/// <summary>
/// Picking up a node you happened to walk past, without losing the plot.
/// </summary>
/// <remarks>
/// <para>
/// This is the piece that makes a character look like a person playing rather than a program
/// executing a plan. A player questing through Elwynn who walks past a copper vein mines it.
/// One who walks past nine and mines none, or who abandons the quest to strip the zone, does
/// not look like either.
/// </para>
/// <para>
/// So the whole design is limits. A short detour range, a cap on how many can be taken in a
/// row, a rest before detouring again, a per-node cooldown, and never while there is anything
/// to fight. Remove any of them and this stops being opportunism and becomes a second bot base
/// fighting the first for control.
/// </para>
/// </remarks>
public sealed class OpportunisticGathering
{
    private readonly OpportunisticSettings _settings;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<ulong, DateTimeOffset> _recentlyVisited = [];

    private int _consecutive;
    private DateTimeOffset _restingUntil = DateTimeOffset.MinValue;
    private bool _warned;

    public OpportunisticGathering(OpportunisticSettings? settings = null, Func<DateTimeOffset>? clock = null)
    {
        _settings = settings ?? new OpportunisticSettings();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>What it is looking for and how far it will go.</summary>
    public OpportunisticSettings Settings => _settings;

    /// <summary>How many nodes have been taken without a break.</summary>
    public int Consecutive => _consecutive;

    /// <summary>True while it is leaving nodes alone to let the main task progress.</summary>
    public bool IsResting => _clock() < _restingUntil;

    /// <summary>Nodes being left alone because they were recently visited.</summary>
    public int NodesOnCooldown => _recentlyVisited.Count;

    /// <summary>
    /// The node worth stopping for, or null.
    /// </summary>
    /// <remarks>
    /// Nearest first, because the whole justification is that it is barely a detour.
    /// </remarks>
    public VisibleObject? Choose(IBotState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!_settings.IsUsable)
        {
            if (!_warned && _settings.NodeEntries.Count > 0)
            {
                _warned = true;
                Log.For<OpportunisticGathering>().Warning(
                    "Opportunistic gathering is configured with a detour range no larger than "
                    + "the interact range, so it can never reach anything.");
            }

            return null;
        }

        // Never mid-fight, and never with something targeted: stopping to mine while a wolf
        // chews on the character is the single most obvious bot behaviour there is.
        if (state.IsInCombat || state.Target is { IsAlive: true })
        {
            return null;
        }

        DateTimeOffset now = _clock();

        if (now < _restingUntil)
        {
            return null;
        }

        Forget(now);

        return state.VisibleObjects
            .Where(visible => _settings.NodeEntries.Contains(visible.Entry))
            .Where(visible => visible.HasPosition && visible.Distance <= _settings.DetourRange)
            .Where(visible => !_recentlyVisited.ContainsKey(visible.Guid.Value))
            .OrderBy(visible => visible.Distance)
            .Select(visible => (VisibleObject?)visible)
            .FirstOrDefault();
    }

    /// <summary>Builds the subtree that takes a node when one is worth taking.</summary>
    /// <remarks>
    /// Returns failure when there is nothing to do, so the bot base below it runs. That is what
    /// makes this a decorator on someone else's plan rather than a plan of its own.
    /// </remarks>
    public Node<IBotState> Build() =>
        new Do<IBotState>(state =>
        {
            if (Choose(state) is not { } node)
            {
                return RunStatus.Failure;
            }

            if (node.Distance > _settings.InteractRange)
            {
                if (state.MovementFailed)
                {
                    // Cannot get to it — over a fence, under the world, on a ledge. Leave it
                    // alone rather than walking into the same wall for the rest of the night.
                    Note(node.Guid.Value);
                    return RunStatus.Failure;
                }

                return state.MoveTo(node.Position) ? RunStatus.Running : RunStatus.Failure;
            }

            state.StopMoving();

            bool interacted = state.Interact(node.Guid);

            Note(node.Guid.Value);
            _consecutive++;

            if (_consecutive >= _settings.ConsecutiveLimit)
            {
                // Enough. A dense field would otherwise hold a questing character indefinitely.
                Log.For<OpportunisticGathering>().Debug(
                    "Took {Count} nodes in a row; getting on with the main task for {Rest}",
                    _consecutive, _settings.Cooldown);

                _consecutive = 0;
                _restingUntil = _clock() + _settings.Cooldown;
            }

            return interacted ? RunStatus.Running : RunStatus.Failure;
        })
        { Name = "Take a node on the way" };

    /// <summary>Forgets everything, for a new session.</summary>
    public void Reset()
    {
        _recentlyVisited.Clear();
        _consecutive = 0;
        _restingUntil = DateTimeOffset.MinValue;
    }

    private void Note(ulong guid) => _recentlyVisited[guid] = _clock() + _settings.NodeCooldown;

    private void Forget(DateTimeOffset now)
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
}
