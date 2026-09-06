namespace WoWBuddy.Behavior.Tests;

/// <summary>A context with a clock the tests control and a log of what ran.</summary>
public sealed class TestContext
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public List<string> Log { get; } = [];

    public HashSet<string> Flags { get; } = [];

    public void Advance(TimeSpan by) => Now += by;

    public bool Flag(string name) => Flags.Contains(name);
}

/// <summary>A node that records that it ran and reports whatever it was told to.</summary>
public sealed class Recorder(string name, params RunStatus[] statuses) : Node<TestContext>
{
    private int _tick;

    /// <summary>How many times this node has been ticked.</summary>
    public int Ticks { get; private set; }

    /// <summary>How many times this node has been reset.</summary>
    public int Resets { get; private set; }

    protected override RunStatus OnTick(TestContext context)
    {
        Ticks++;
        context.Log.Add(name);

        RunStatus status = statuses.Length == 0
            ? RunStatus.Success
            : statuses[Math.Min(_tick, statuses.Length - 1)];

        _tick++;
        return status;
    }

    public override void Reset()
    {
        base.Reset();
        Resets++;
        _tick = 0;
    }
}
