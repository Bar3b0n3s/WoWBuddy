namespace WoWBuddy.Behavior;

/// <summary>A node wrapping exactly one child.</summary>
public abstract class Decorator<TContext>(Node<TContext> child) : Node<TContext>
{
    /// <summary>The wrapped node.</summary>
    protected Node<TContext> Child { get; } = child ?? throw new ArgumentNullException(nameof(child));

    /// <inheritdoc />
    public override IReadOnlyList<Node<TContext>> Children => [Child];

    /// <inheritdoc />
    public override void Reset()
    {
        base.Reset();
        Child.Reset();
    }
}

/// <summary>
/// Runs its child only while a condition holds.
/// </summary>
/// <remarks>
/// The workhorse of a bot tree: nearly every branch is "when this is true, do that". Returns
/// <see cref="RunStatus.Failure"/> when the condition is false, which is what lets the parent
/// selector move on to the next branch.
/// </remarks>
public sealed class If<TContext>(Func<TContext, bool> condition, Node<TContext> child)
    : Decorator<TContext>(child)
{
    private readonly Func<TContext, bool> _condition =
        condition ?? throw new ArgumentNullException(nameof(condition));

    protected override RunStatus OnTick(TContext context)
    {
        if (!_condition(context))
        {
            // Abandon partial progress: the reason for doing this has gone away.
            Child.Reset();
            return RunStatus.Failure;
        }

        return Child.Tick(context);
    }
}

/// <summary>Swaps success and failure. Running passes through unchanged.</summary>
public sealed class Inverter<TContext>(Node<TContext> child) : Decorator<TContext>(child)
{
    protected override RunStatus OnTick(TContext context) => Child.Tick(context) switch
    {
        RunStatus.Success => RunStatus.Failure,
        RunStatus.Failure => RunStatus.Success,
        _ => RunStatus.Running,
    };
}

/// <summary>
/// Reports success whatever the child did.
/// </summary>
/// <remarks>
/// For optional steps in a sequence: buffing before a pull is worth attempting but must not
/// stop the pull when there is nothing to buff.
/// </remarks>
public sealed class Optional<TContext>(Node<TContext> child) : Decorator<TContext>(child)
{
    protected override RunStatus OnTick(TContext context)
    {
        RunStatus status = Child.Tick(context);
        return status == RunStatus.Running ? RunStatus.Running : RunStatus.Success;
    }
}

/// <summary>
/// Stops its child from running more often than a given interval.
/// </summary>
/// <remarks>
/// <para>
/// Some things are cheap to decide and expensive to do. Checking whether the bags are full is
/// a memory read; running the vendor errand is a two-minute round trip. Others are simply
/// rude to repeat: re-issuing a cast every tick spams the server and looks nothing like a
/// person.
/// </para>
/// <para>
/// The clock is injected rather than read from <see cref="DateTimeOffset.UtcNow"/> so that
/// throttling can be tested without waiting in real time.
/// </para>
/// </remarks>
public sealed class Throttle<TContext>(
    TimeSpan interval,
    Func<TContext, DateTimeOffset> clock,
    Node<TContext> child) : Decorator<TContext>(child)
{
    private readonly Func<TContext, DateTimeOffset> _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    private DateTimeOffset _lastRun = DateTimeOffset.MinValue;

    protected override RunStatus OnTick(TContext context)
    {
        DateTimeOffset now = _clock(context);

        // Once the child is running it keeps running: throttling is about how often it may
        // start, not about interrupting it half way.
        if (Child.LastStatus != RunStatus.Running && now - _lastRun < interval)
        {
            return RunStatus.Failure;
        }

        RunStatus status = Child.Tick(context);

        if (status != RunStatus.Running)
        {
            _lastRun = now;
        }

        return status;
    }

    public override void Reset()
    {
        base.Reset();

        // The interval is deliberately not cleared. Being pre-empted is not a reason to let
        // an expensive action run again immediately.
    }
}
