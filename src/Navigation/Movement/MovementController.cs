using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Execution;

namespace WoWBuddy.Navigation.Movement;

/// <summary>What the movement controller is doing.</summary>
public enum MovementState
{
    /// <summary>No destination.</summary>
    Idle = 0,

    /// <summary>Walking a path.</summary>
    Moving = 1,

    /// <summary>Arrived at the destination.</summary>
    Arrived = 2,

    /// <summary>Trying to get free of something.</summary>
    Recovering = 3,

    /// <summary>Given up: the destination could not be reached.</summary>
    Failed = 4,
}

/// <summary>Why movement stopped.</summary>
public enum MovementFailure
{
    /// <summary>Not failed.</summary>
    None = 0,

    /// <summary>No route could be found.</summary>
    NoPath,

    /// <summary>Stuck and unable to recover.</summary>
    Stuck,

    /// <summary>The click-to-move block could not be written.</summary>
    CannotSteer,

    /// <summary>Movement was not permitted to start.</summary>
    NotPermitted,
}

/// <summary>
/// Walks the character along a path using the client's click-to-move system.
/// </summary>
/// <remarks>
/// <para>
/// Movement is done by handing the client a destination and letting its own pathing walk
/// there, rather than by holding down keys. The result is movement the server sees as
/// ordinary, produced by the same code that runs when a player right-clicks the ground.
/// </para>
/// <para>
/// The controller is a state machine driven by <see cref="Tick"/> rather than a loop, so it
/// composes with the behaviour tree that arrives in phase 4 and can be interrupted between
/// any two steps.
/// </para>
/// <para>
/// <b>It refuses to run until click-to-move has been confirmed against the client.</b> Those
/// offsets have a single published source and have not been verified first-hand; writing an
/// unverified destination into a live client is how a character ends up walking somewhere
/// nobody asked for. <see cref="Enable"/> is the deliberate act of saying the check has been
/// done, and until it is called the controller reports
/// <see cref="MovementFailure.NotPermitted"/> and touches nothing.
/// </para>
/// </remarks>
public sealed class MovementController
{
    /// <summary>How close counts as having reached a waypoint.</summary>
    /// <remarks>
    /// Click-to-move stops a little short of its target by design, so insisting on a tighter
    /// tolerance would leave the controller waiting for an arrival that never registers.
    /// </remarks>
    public const float WaypointTolerance = 3f;

    /// <summary>How close counts as having reached the final destination.</summary>
    public const float DestinationTolerance = 2f;

    /// <summary>Longest leg handed to click-to-move at once.</summary>
    /// <remarks>
    /// Long legs mean long stretches with nothing to check against. Subdividing gives the
    /// controller regular opportunities to notice it is off course.
    /// </remarks>
    public const float MaximumLegLength = 40f;

    private readonly ClickToMoveWriter _clickToMove;
    private readonly StuckDetector _stuck = new();

    private IReadOnlyList<Vector3> _path = [];
    private int _waypointIndex;
    private Vector3 _lastSteeredTo;
    private bool _enabled;

    public MovementController(ClickToMoveWriter clickToMove)
    {
        _clickToMove = clickToMove ?? throw new ArgumentNullException(nameof(clickToMove));
    }

    /// <summary>Current state.</summary>
    public MovementState State { get; private set; } = MovementState.Idle;

    /// <summary>Why movement failed, when it did.</summary>
    public MovementFailure Failure { get; private set; }

    /// <summary>The path being walked.</summary>
    public IReadOnlyList<Vector3> Path => _path;

    /// <summary>The waypoint currently being walked toward, if any.</summary>
    public Vector3? CurrentWaypoint =>
        _waypointIndex < _path.Count ? _path[_waypointIndex] : null;

    /// <summary>The final destination, if any.</summary>
    public Vector3? Destination => _path.Count > 0 ? _path[^1] : null;

    /// <summary>How stuck the character currently appears.</summary>
    public StuckSeverity Stuck => _stuck.Severity;

    /// <summary>True once movement has been permitted.</summary>
    public bool IsEnabled => _enabled;

    /// <summary>
    /// Permits the controller to write to the client.
    /// </summary>
    /// <remarks>
    /// Call only after confirming, against a real client, that the click-to-move block is
    /// where the offsets say it is. The inspector's <c>ctm</c> command exists for that; the
    /// procedure is in <c>docs/phase-3-manual-test.md</c>.
    /// </remarks>
    public void Enable()
    {
        _enabled = true;
        Log.For<MovementController>().Information(
            "Movement enabled; click-to-move at 0x{Address:X8}", _clickToMove.BaseAddress);
    }

    /// <summary>
    /// Starts walking <paramref name="path"/>.
    /// </summary>
    /// <remarks>
    /// The path is simplified to drop redundant waypoints, then subdivided so no single leg
    /// runs longer than <see cref="MaximumLegLength"/>.
    /// </remarks>
    public bool Follow(IReadOnlyList<Vector3> path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!_enabled)
        {
            Fail(MovementFailure.NotPermitted);
            return false;
        }

        if (path.Count == 0)
        {
            Fail(MovementFailure.NoPath);
            return false;
        }

        _path = PathSmoother.Subdivide(PathSmoother.Simplify(path), MaximumLegLength);
        _waypointIndex = 0;
        _stuck.Reset();
        State = MovementState.Moving;
        Failure = MovementFailure.None;

        Log.For<MovementController>().Debug(
            "Following a path of {Count} waypoint(s) to {Destination}", _path.Count, Destination);

        return true;
    }

    /// <summary>Stops the character where it stands.</summary>
    public void Stop()
    {
        if (_enabled)
        {
            _clickToMove.Stop();
        }

        _path = [];
        _waypointIndex = 0;
        _stuck.Reset();
        State = MovementState.Idle;
        Failure = MovementFailure.None;
    }

    /// <summary>
    /// Advances the movement by one step.
    /// </summary>
    /// <param name="position">Where the character is now.</param>
    /// <param name="now">The current time. Injected so this is testable.</param>
    public MovementState Tick(Vector3 position, DateTimeOffset now)
    {
        if (State is not (MovementState.Moving or MovementState.Recovering))
        {
            return State;
        }

        if (!_enabled)
        {
            Fail(MovementFailure.NotPermitted);
            return State;
        }

        AdvancePastReachedWaypoints(position);

        if (_waypointIndex >= _path.Count)
        {
            State = MovementState.Arrived;
            _clickToMove.Stop();
            return State;
        }

        StuckSeverity severity = _stuck.Update(position, now);

        if (severity == StuckSeverity.Persistent)
        {
            Log.For<MovementController>().Warning(
                "Stuck at {Position} after {Episodes} recovery attempts; giving up on this path",
                position, _stuck.Episodes);
            Fail(MovementFailure.Stuck);
            return State;
        }

        if (severity == StuckSeverity.Stuck)
        {
            return AttemptRecovery(position, now);
        }

        State = MovementState.Moving;
        return SteerToward(_path[_waypointIndex]) ? State : Fail(MovementFailure.CannotSteer);
    }

    /// <summary>
    /// How many waypoints ahead of the current one are considered already reached.
    /// </summary>
    /// <remarks>
    /// Overshooting is normal: click-to-move stops when it decides it has arrived, momentum
    /// carries the character on, and a tick can land past the next waypoint or two. Without a
    /// look-ahead the controller would steer back to a point the character has already gone
    /// past, which reads as the character dithering on the spot.
    /// <para>
    /// Bounded rather than scanning the whole path, because a route that doubles back can
    /// pass close to a much later waypoint. Jumping to that would silently cut out the middle
    /// of the path, and on a route that loops around an obstacle the shortcut goes straight
    /// through it.
    /// </para>
    /// </remarks>
    public const int WaypointLookAhead = 5;

    /// <summary>
    /// Advances past every waypoint the character can be considered to have reached.
    /// </summary>
    private void AdvancePastReachedWaypoints(Vector3 position)
    {
        int limit = Math.Min(_path.Count - 1, _waypointIndex + WaypointLookAhead);
        int furthestReached = -1;

        for (int i = _waypointIndex; i <= limit; i++)
        {
            bool isLast = i == _path.Count - 1;
            float tolerance = isLast ? DestinationTolerance : WaypointTolerance;

            // Flat distance: small height differences along a slope must not prevent
            // arrival, and a waypoint on the floor above is excluded by being far away
            // horizontally too.
            if (position.Distance2D(_path[i]) <= tolerance)
            {
                furthestReached = i;
            }
        }

        if (furthestReached >= 0)
        {
            _waypointIndex = furthestReached + 1;
        }
    }

    /// <summary>
    /// Tries to break out of whatever the character is caught on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Escalating and deliberately simple. The first response is to re-issue the destination,
    /// which resolves the common case where the client's own pathing gave up but the route is
    /// fine. The next is to aim at the waypoint after the current one, which unpicks the case
    /// where a single waypoint sits inside geometry.
    /// </para>
    /// <para>
    /// Jumping and strafing are the traditional next steps and are not implemented here,
    /// because both need input the bot cannot yet send: they are movement <em>commands</em>
    /// rather than destinations, and click-to-move has no way to express them. That waits for
    /// the input layer rather than being faked with a nearby destination, which tends to walk
    /// the character further into whatever it is stuck on.
    /// </para>
    /// </remarks>
    private MovementState AttemptRecovery(Vector3 position, DateTimeOffset now)
    {
        State = MovementState.Recovering;
        _stuck.NoteRecoveryAttempt(position, now);

        // Skipping a waypoint is only safe when there is another one to aim at; the
        // destination itself is never skipped.
        if (_stuck.Episodes >= 2 && _waypointIndex < _path.Count - 1)
        {
            Log.For<MovementController>().Debug(
                "Stuck at {Position}; skipping waypoint {Index} and aiming at the next one",
                position, _waypointIndex);
            _waypointIndex++;
        }
        else
        {
            Log.For<MovementController>().Debug(
                "Stuck at {Position}; re-issuing the destination", position);
        }

        Vector3 target = _path[Math.Min(_waypointIndex, _path.Count - 1)];

        // Force a fresh write even if the target is unchanged: re-issuing is the point.
        _lastSteeredTo = Vector3.Zero;
        return SteerToward(target) ? State : Fail(MovementFailure.CannotSteer);
    }

    /// <summary>
    /// Points the client at <paramref name="target"/>, skipping the write when it is already
    /// heading there.
    /// </summary>
    /// <remarks>
    /// Rewriting the same destination every tick would be harmless but wasteful, and it makes
    /// the click-to-move block harder to read while debugging.
    /// </remarks>
    private bool SteerToward(Vector3 target)
    {
        if (target == _lastSteeredTo)
        {
            return true;
        }

        if (!_clickToMove.Write(ClickToMoveAction.Move, target))
        {
            Log.For<MovementController>().Error("Could not write a click-to-move destination of {Target}", target);
            return false;
        }

        _lastSteeredTo = target;
        return true;
    }

    private MovementState Fail(MovementFailure failure)
    {
        Failure = failure;
        State = MovementState.Failed;

        if (_enabled && failure != MovementFailure.NotPermitted)
        {
            _clickToMove.Stop();
        }

        return State;
    }
}
