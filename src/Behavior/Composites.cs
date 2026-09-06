namespace WoWBuddy.Behavior;

/// <summary>A node with children.</summary>
public abstract class Composite<TContext> : Node<TContext>
{
    private readonly Node<TContext>[] _children;

    protected Composite(params Node<TContext>[] children)
    {
        _children = children ?? throw new ArgumentNullException(nameof(children));
    }

    /// <inheritdoc />
    public override IReadOnlyList<Node<TContext>> Children => _children;

    /// <summary>Index of the child currently being run, when the composite is resumable.</summary>
    protected int CurrentIndex { get; set; }

    /// <inheritdoc />
    public override void Reset()
    {
        base.Reset();
        CurrentIndex = 0;

        foreach (Node<TContext> child in _children)
        {
            child.Reset();
        }
    }
}

/// <summary>
/// Runs its children in order until one fails.
/// </summary>
/// <remarks>
/// <para>
/// The "and then" node: walk to the vendor <em>and then</em> open the window <em>and then</em>
/// sell. A child returning <see cref="RunStatus.Running"/> suspends the sequence there, and
/// the next tick resumes at that child rather than starting over.
/// </para>
/// <para>
/// Resuming matters, and so does not resuming too eagerly. A sequence that restarted from the
/// beginning every tick would never get past a step that takes time; one that resumed after
/// being pre-empted by a higher-priority branch might carry on selling to a vendor the
/// character has since walked away from. <see cref="Node{TContext}.Reset"/> is what settles
/// that, and the parent selector calls it when it switches branches.
/// </para>
/// </remarks>
public sealed class Sequence<TContext>(params Node<TContext>[] children) : Composite<TContext>(children)
{
    protected override RunStatus OnTick(TContext context)
    {
        while (CurrentIndex < Children.Count)
        {
            RunStatus status = Children[CurrentIndex].Tick(context);

            if (status == RunStatus.Running)
            {
                return RunStatus.Running;
            }

            if (status == RunStatus.Failure)
            {
                CurrentIndex = 0;
                return RunStatus.Failure;
            }

            CurrentIndex++;
        }

        CurrentIndex = 0;
        return RunStatus.Success;
    }
}

/// <summary>
/// Runs its children in order until one does not fail, resuming where it left off.
/// </summary>
/// <remarks>
/// The "or else" node. Use it when the alternatives are genuinely a fallback chain and a
/// long-running child should not be re-evaluated against its earlier siblings on every tick.
/// For anything where priority must be reconsidered — which is most of a bot — use
/// <see cref="PrioritySelector{TContext}"/> instead.
/// </remarks>
public sealed class Selector<TContext>(params Node<TContext>[] children) : Composite<TContext>(children)
{
    protected override RunStatus OnTick(TContext context)
    {
        while (CurrentIndex < Children.Count)
        {
            RunStatus status = Children[CurrentIndex].Tick(context);

            if (status == RunStatus.Running)
            {
                return RunStatus.Running;
            }

            if (status == RunStatus.Success)
            {
                CurrentIndex = 0;
                return RunStatus.Success;
            }

            CurrentIndex++;
        }

        CurrentIndex = 0;
        return RunStatus.Failure;
    }
}

/// <summary>
/// Tries its children from the top on every tick, and lets a higher-priority one take over.
/// </summary>
/// <remarks>
/// <para>
/// This is the node the bot's root is built from, and the reason it behaves sensibly. Because
/// it starts from the first child every tick, a branch that becomes relevant — the character
/// is dying, something is attacking, the bags are full — pre-empts whatever was running.
/// </para>
/// <para>
/// When the branch that runs is not the one that ran last tick, the previous one is reset, so
/// it abandons its partial progress rather than resuming minutes later against a world that
/// has changed.
/// </para>
/// </remarks>
public sealed class PrioritySelector<TContext>(params Node<TContext>[] children) : Composite<TContext>(children)
{
    private int _runningIndex = -1;

    /// <summary>Index of the branch that ran on the last tick, or -1.</summary>
    public int RunningIndex => _runningIndex;

    protected override RunStatus OnTick(TContext context)
    {
        for (int i = 0; i < Children.Count; i++)
        {
            RunStatus status = Children[i].Tick(context);

            if (status == RunStatus.Failure)
            {
                continue;
            }

            // A different branch than last time means the old one was pre-empted part way
            // through, and must not resume from there when it next gets a turn.
            if (_runningIndex >= 0 && _runningIndex != i)
            {
                Children[_runningIndex].Reset();
            }

            _runningIndex = status == RunStatus.Running ? i : -1;
            return status;
        }

        if (_runningIndex >= 0)
        {
            Children[_runningIndex].Reset();
            _runningIndex = -1;
        }

        return RunStatus.Failure;
    }

    public override void Reset()
    {
        base.Reset();
        _runningIndex = -1;
    }
}
