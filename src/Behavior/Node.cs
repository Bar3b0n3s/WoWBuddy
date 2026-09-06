namespace WoWBuddy.Behavior;

/// <summary>What a node reported when it was last ticked.</summary>
public enum RunStatus
{
    /// <summary>The node finished and achieved what it set out to do.</summary>
    Success,

    /// <summary>The node finished and did not.</summary>
    Failure,

    /// <summary>The node is part way through and wants to be ticked again.</summary>
    Running,
}

/// <summary>
/// A node in a behaviour tree.
/// </summary>
/// <remarks>
/// <para>
/// The bot decides what to do by ticking a tree from the root several times a second. Each
/// tick walks down from the root, so a higher-priority branch always gets the chance to take
/// over: a character that starts dying mid-way through walking to a vendor stops walking,
/// because the tree is re-evaluated rather than resumed blindly.
/// </para>
/// <para>
/// <b>Running is the whole point.</b> A node that returns <see cref="RunStatus.Running"/> has
/// not finished, and the tick ends there. That is what lets an action that takes several
/// seconds — walking somewhere, casting something, waiting for a corpse to be lootable — be
/// expressed as one node without blocking the bot in a loop it cannot be interrupted out of.
/// </para>
/// <para>
/// Generic in the context type rather than using a dictionary blackboard for everything, so
/// that a routine referring to the wrong thing is a compile error rather than a null at three
/// in the morning. <see cref="Blackboard"/> is still there for the genuinely dynamic state
/// that bot bases and plugins need to share.
/// </para>
/// </remarks>
/// <typeparam name="TContext">Everything the tree needs to see; supplied on each tick.</typeparam>
public abstract class Node<TContext>
{
    /// <summary>A name for logs and the tree view. Defaults to the type name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The status this node reported on its last tick.</summary>
    public RunStatus LastStatus { get; protected set; } = RunStatus.Failure;

    /// <summary>Runs one step of this node.</summary>
    public RunStatus Tick(TContext context)
    {
        LastStatus = OnTick(context);
        return LastStatus;
    }

    /// <summary>The node's own behaviour.</summary>
    protected abstract RunStatus OnTick(TContext context);

    /// <summary>
    /// Abandons any partial progress.
    /// </summary>
    /// <remarks>
    /// Called when a higher-priority branch takes over, so that a half-finished sequence does
    /// not resume from the middle when control comes back to it much later and the world has
    /// moved on.
    /// </remarks>
    public virtual void Reset()
    {
        LastStatus = RunStatus.Failure;
    }

    /// <summary>Child nodes, for the tree view and for recursive resets.</summary>
    public virtual IReadOnlyList<Node<TContext>> Children => [];

    /// <summary>The display name of this node.</summary>
    public string DisplayName => string.IsNullOrEmpty(Name) ? GetType().Name : Name;

    public override string ToString() => DisplayName;
}
