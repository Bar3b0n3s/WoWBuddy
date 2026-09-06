namespace WoWBuddy.Behavior;

/// <summary>Does something and reports what happened.</summary>
public sealed class Do<TContext>(Func<TContext, RunStatus> action) : Node<TContext>
{
    private readonly Func<TContext, RunStatus> _action =
        action ?? throw new ArgumentNullException(nameof(action));

    /// <summary>An action that always reports success.</summary>
    public static Do<TContext> Always(Action<TContext> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new Do<TContext>(context =>
        {
            action(context);
            return RunStatus.Success;
        });
    }

    /// <summary>An action that reports success or failure from a predicate.</summary>
    public static Do<TContext> Try(Func<TContext, bool> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new Do<TContext>(context => action(context) ? RunStatus.Success : RunStatus.Failure);
    }

    protected override RunStatus OnTick(TContext context) => _action(context);
}

/// <summary>Reports success or failure without doing anything.</summary>
public sealed class Check<TContext>(Func<TContext, bool> condition) : Node<TContext>
{
    private readonly Func<TContext, bool> _condition =
        condition ?? throw new ArgumentNullException(nameof(condition));

    protected override RunStatus OnTick(TContext context) =>
        _condition(context) ? RunStatus.Success : RunStatus.Failure;
}

/// <summary>
/// Reports Running until a condition holds, then succeeds.
/// </summary>
/// <remarks>
/// <para>
/// Waiting is the normal way a bot spends much of its time: for a cast to land, a corpse to
/// become lootable, a global cooldown to pass. Expressing it as a node keeps the tick
/// non-blocking, so a wait can be pre-empted by anything more important.
/// </para>
/// <para>
/// The timeout is not optional. A wait without one is how an unattended bot stands still for
/// six hours because something it was waiting for never happened.
/// </para>
/// </remarks>
public sealed class WaitUntil<TContext>(
    Func<TContext, bool> condition,
    TimeSpan timeout,
    Func<TContext, DateTimeOffset> clock) : Node<TContext>
{
    private readonly Func<TContext, bool> _condition =
        condition ?? throw new ArgumentNullException(nameof(condition));

    private readonly Func<TContext, DateTimeOffset> _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    private DateTimeOffset? _startedAt;

    /// <summary>True when the wait gave up rather than being satisfied.</summary>
    public bool TimedOut { get; private set; }

    protected override RunStatus OnTick(TContext context)
    {
        DateTimeOffset now = _clock(context);
        _startedAt ??= now;

        if (_condition(context))
        {
            _startedAt = null;
            TimedOut = false;
            return RunStatus.Success;
        }

        if (now - _startedAt.Value >= timeout)
        {
            _startedAt = null;
            TimedOut = true;
            return RunStatus.Failure;
        }

        return RunStatus.Running;
    }

    public override void Reset()
    {
        base.Reset();
        _startedAt = null;
        TimedOut = false;
    }
}

/// <summary>Reports Running for a fixed duration, then succeeds.</summary>
/// <remarks>
/// Used for the deliberate pauses that make a bot look less like a machine: a beat before
/// looting, a moment between pulls.
/// </remarks>
public sealed class Wait<TContext>(TimeSpan duration, Func<TContext, DateTimeOffset> clock) : Node<TContext>
{
    private readonly Func<TContext, DateTimeOffset> _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    private DateTimeOffset? _startedAt;

    protected override RunStatus OnTick(TContext context)
    {
        DateTimeOffset now = _clock(context);
        _startedAt ??= now;

        if (now - _startedAt.Value >= duration)
        {
            _startedAt = null;
            return RunStatus.Success;
        }

        return RunStatus.Running;
    }

    public override void Reset()
    {
        base.Reset();
        _startedAt = null;
    }
}
