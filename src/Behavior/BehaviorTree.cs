using System.Text;
using WoWBuddy.Common.Logging;

namespace WoWBuddy.Behavior;

/// <summary>
/// A behaviour tree and the state of its most recent tick.
/// </summary>
/// <remarks>
/// <para>
/// Wraps a root node so that the bot has one thing to tick, one place where an exception
/// escaping a node is caught, and one place that records what the tree did for the live view
/// in the dev tools.
/// </para>
/// <para>
/// <b>An exception from a node stops the tree.</b> It does not skip the node and carry on: a
/// routine that threw was in the middle of deciding something, and continuing from an unknown
/// state is how a bot ends up doing something inexplicable. The bot stops and the character is
/// left standing, which is recoverable.
/// </para>
/// </remarks>
public sealed class BehaviorTree<TContext>
{
    private readonly Node<TContext> _root;

    public BehaviorTree(Node<TContext> root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
    }

    /// <summary>The root node.</summary>
    public Node<TContext> Root => _root;

    /// <summary>Number of ticks since the tree was created or reset.</summary>
    public long TickCount { get; private set; }

    /// <summary>The status of the most recent tick.</summary>
    public RunStatus LastStatus { get; private set; } = RunStatus.Failure;

    /// <summary>The exception that stopped the tree, if one did.</summary>
    public Exception? Fault { get; private set; }

    /// <summary>True when the tree has stopped because a node threw.</summary>
    public bool IsFaulted => Fault is not null;

    /// <summary>
    /// Ticks the tree once.
    /// </summary>
    /// <returns>
    /// What the root reported, or <see cref="RunStatus.Failure"/ > when the tree is faulted.
    /// </returns>
    public RunStatus Tick(TContext context)
    {
        if (IsFaulted)
        {
            return RunStatus.Failure;
        }

        TickCount++;

        try
        {
            LastStatus = _root.Tick(context);
            return LastStatus;
        }
        catch (Exception ex)
        {
            Fault = ex;
            LastStatus = RunStatus.Failure;

            Log.For(nameof(BehaviorTree<TContext>)).Fatal(
                ex,
                "A behaviour node threw on tick {Tick}. The tree has stopped and the character " +
                "has been left where it is.",
                TickCount);

            return RunStatus.Failure;
        }
    }

    /// <summary>Clears any partial progress and the faulted state.</summary>
    public void Reset()
    {
        _root.Reset();
        Fault = null;
        LastStatus = RunStatus.Failure;
    }

    /// <summary>
    /// Renders the tree and each node's last status, for the live view in the dev tools.
    /// </summary>
    public string Describe()
    {
        var builder = new StringBuilder();
        Describe(_root, builder, 0);
        return builder.ToString();
    }

    private static void Describe(Node<TContext> node, StringBuilder builder, int depth)
    {
        builder
            .Append(' ', depth * 2)
            .Append(node.DisplayName)
            .Append("  [")
            .Append(node.LastStatus)
            .AppendLine("]");

        foreach (Node<TContext> child in node.Children)
        {
            Describe(child, builder, depth + 1);
        }
    }
}
