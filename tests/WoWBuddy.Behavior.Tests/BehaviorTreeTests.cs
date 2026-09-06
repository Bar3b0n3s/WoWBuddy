using Xunit;

namespace WoWBuddy.Behavior.Tests;

public sealed class SequenceTests
{
    [Fact]
    public void RunsChildrenInOrderAndSucceedsWhenAllDo()
    {
        var context = new TestContext();
        var sequence = new Sequence<TestContext>(
            new Recorder("a"), new Recorder("b"), new Recorder("c"));

        Assert.Equal(RunStatus.Success, sequence.Tick(context));
        Assert.Equal(["a", "b", "c"], context.Log);
    }

    [Fact]
    public void StopsAtTheFirstFailure()
    {
        var context = new TestContext();
        var sequence = new Sequence<TestContext>(
            new Recorder("a"), new Recorder("b", RunStatus.Failure), new Recorder("c"));

        Assert.Equal(RunStatus.Failure, sequence.Tick(context));
        Assert.Equal(["a", "b"], context.Log);
    }

    [Fact]
    public void ResumesAtTheRunningChildRatherThanStartingOver()
    {
        // Without this a sequence could never get past a step that takes time: the first
        // child would run again every tick and the second would never finish.
        var context = new TestContext();
        var first = new Recorder("a");
        var second = new Recorder("b", RunStatus.Running, RunStatus.Running, RunStatus.Success);
        var sequence = new Sequence<TestContext>(first, second, new Recorder("c"));

        Assert.Equal(RunStatus.Running, sequence.Tick(context));
        Assert.Equal(RunStatus.Running, sequence.Tick(context));
        Assert.Equal(RunStatus.Success, sequence.Tick(context));

        Assert.Equal(1, first.Ticks);
        Assert.Equal(3, second.Ticks);
    }

    [Fact]
    public void StartsFromTheBeginningOnceItHasCompleted()
    {
        var context = new TestContext();
        var first = new Recorder("a");
        var sequence = new Sequence<TestContext>(first, new Recorder("b"));

        sequence.Tick(context);
        sequence.Tick(context);

        Assert.Equal(2, first.Ticks);
    }

    [Fact]
    public void AnEmptySequenceSucceeds() =>
        Assert.Equal(RunStatus.Success, new Sequence<TestContext>().Tick(new TestContext()));
}

public sealed class PrioritySelectorTests
{
    [Fact]
    public void TakesTheFirstChildThatDoesNotFail()
    {
        var context = new TestContext();
        var selector = new PrioritySelector<TestContext>(
            new Recorder("high", RunStatus.Failure),
            new Recorder("mid"),
            new Recorder("low"));

        Assert.Equal(RunStatus.Success, selector.Tick(context));
        Assert.Equal(["high", "mid"], context.Log);
    }

    [Fact]
    public void ReconsidersPriorityOnEveryTick()
    {
        // The behaviour the bot's root depends on: a branch that becomes relevant must be
        // able to take over from one that is part way through something slower.
        var context = new TestContext();
        var emergency = new Recorder("emergency", RunStatus.Failure, RunStatus.Success);
        var routine = new Recorder("routine", RunStatus.Running);
        var selector = new PrioritySelector<TestContext>(emergency, routine);

        Assert.Equal(RunStatus.Running, selector.Tick(context));
        Assert.Equal(RunStatus.Success, selector.Tick(context));

        Assert.Equal(["emergency", "routine", "emergency"], context.Log);
    }

    [Fact]
    public void ResetsAPreemptedBranchSoItDoesNotResumeStale()
    {
        // A vendor run interrupted by combat must start again afterwards, not carry on
        // selling to a vendor the character has walked away from.
        var context = new TestContext();
        var emergency = new Recorder("emergency", RunStatus.Failure, RunStatus.Success);
        var errand = new Recorder("errand", RunStatus.Running);
        var selector = new PrioritySelector<TestContext>(emergency, errand);

        selector.Tick(context);
        Assert.Equal(0, errand.Resets);

        selector.Tick(context);

        Assert.Equal(1, errand.Resets);
    }

    [Fact]
    public void ResetsTheRunningBranchWhenEverythingFails()
    {
        var context = new TestContext();
        var running = new Recorder("running", RunStatus.Running, RunStatus.Failure);
        var selector = new PrioritySelector<TestContext>(running);

        selector.Tick(context);
        Assert.Equal(RunStatus.Failure, selector.Tick(context));

        Assert.Equal(1, running.Resets);
    }

    [Fact]
    public void AnEmptySelectorFails() =>
        Assert.Equal(RunStatus.Failure, new PrioritySelector<TestContext>().Tick(new TestContext()));
}

public sealed class DecoratorTests
{
    [Fact]
    public void IfRunsItsChildOnlyWhenTheConditionHolds()
    {
        var context = new TestContext();
        var child = new Recorder("child");
        var node = new If<TestContext>(c => c.Flag("go"), child);

        Assert.Equal(RunStatus.Failure, node.Tick(context));
        Assert.Equal(0, child.Ticks);

        context.Flags.Add("go");

        Assert.Equal(RunStatus.Success, node.Tick(context));
        Assert.Equal(1, child.Ticks);
    }

    [Fact]
    public void IfAbandonsPartialProgressWhenItsReasonGoesAway()
    {
        // Halfway through walking to a corpse when the character gets resurrected: the walk
        // must not resume from where it stopped.
        var context = new TestContext { Flags = { "go" } };
        var child = new Recorder("child", RunStatus.Running);
        var node = new If<TestContext>(c => c.Flag("go"), child);

        node.Tick(context);
        context.Flags.Remove("go");
        node.Tick(context);

        Assert.Equal(1, child.Resets);
    }

    [Fact]
    public void InverterSwapsSuccessAndFailureButNotRunning()
    {
        var context = new TestContext();

        Assert.Equal(RunStatus.Failure, new Inverter<TestContext>(new Recorder("s")).Tick(context));
        Assert.Equal(RunStatus.Success,
            new Inverter<TestContext>(new Recorder("f", RunStatus.Failure)).Tick(context));
        Assert.Equal(RunStatus.Running,
            new Inverter<TestContext>(new Recorder("r", RunStatus.Running)).Tick(context));
    }

    [Fact]
    public void OptionalTurnsFailureIntoSuccess()
    {
        var context = new TestContext();
        var node = new Optional<TestContext>(new Recorder("x", RunStatus.Failure));

        Assert.Equal(RunStatus.Success, node.Tick(context));
    }

    [Fact]
    public void ThrottleStopsAnExpensiveBranchRunningEveryTick()
    {
        var context = new TestContext();
        var child = new Recorder("errand");
        var node = new Throttle<TestContext>(TimeSpan.FromMinutes(2), c => c.Now, child);

        Assert.Equal(RunStatus.Success, node.Tick(context));
        Assert.Equal(RunStatus.Failure, node.Tick(context));

        context.Advance(TimeSpan.FromMinutes(3));

        Assert.Equal(RunStatus.Success, node.Tick(context));
        Assert.Equal(2, child.Ticks);
    }

    [Fact]
    public void ThrottleDoesNotInterruptSomethingAlreadyRunning()
    {
        // Throttling governs how often an action may start, not whether it may finish.
        var context = new TestContext();
        var child = new Recorder("errand", RunStatus.Running, RunStatus.Running, RunStatus.Success);
        var node = new Throttle<TestContext>(TimeSpan.FromMinutes(5), c => c.Now, child);

        Assert.Equal(RunStatus.Running, node.Tick(context));
        Assert.Equal(RunStatus.Running, node.Tick(context));
        Assert.Equal(RunStatus.Success, node.Tick(context));
    }

    [Fact]
    public void ThrottleKeepsItsIntervalAcrossAReset()
    {
        // Being pre-empted is not a reason to let an expensive action run again immediately.
        var context = new TestContext();
        var node = new Throttle<TestContext>(TimeSpan.FromMinutes(2), c => c.Now, new Recorder("x"));

        node.Tick(context);
        node.Reset();

        Assert.Equal(RunStatus.Failure, node.Tick(context));
    }
}

public sealed class LeafTests
{
    [Fact]
    public void WaitUntilRunsUntilItsConditionHolds()
    {
        var context = new TestContext();
        var node = new WaitUntil<TestContext>(c => c.Flag("done"), TimeSpan.FromSeconds(10), c => c.Now);

        Assert.Equal(RunStatus.Running, node.Tick(context));

        context.Flags.Add("done");

        Assert.Equal(RunStatus.Success, node.Tick(context));
        Assert.False(node.TimedOut);
    }

    [Fact]
    public void WaitUntilGivesUpRatherThanWaitingForever()
    {
        // A wait without a timeout is how an unattended bot stands still for six hours.
        var context = new TestContext();
        var node = new WaitUntil<TestContext>(_ => false, TimeSpan.FromSeconds(5), c => c.Now);

        Assert.Equal(RunStatus.Running, node.Tick(context));

        context.Advance(TimeSpan.FromSeconds(6));

        Assert.Equal(RunStatus.Failure, node.Tick(context));
        Assert.True(node.TimedOut);
    }

    [Fact]
    public void WaitRunsForItsDuration()
    {
        var context = new TestContext();
        var node = new Wait<TestContext>(TimeSpan.FromSeconds(2), c => c.Now);

        Assert.Equal(RunStatus.Running, node.Tick(context));

        context.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(RunStatus.Success, node.Tick(context));
    }

    [Fact]
    public void WaitStartsAgainAfterCompleting()
    {
        var context = new TestContext();
        var node = new Wait<TestContext>(TimeSpan.FromSeconds(2), c => c.Now);

        node.Tick(context);
        context.Advance(TimeSpan.FromSeconds(2));
        node.Tick(context);

        Assert.Equal(RunStatus.Running, node.Tick(context));
    }

    [Fact]
    public void CheckReportsAConditionWithoutDoingAnything()
    {
        var context = new TestContext();
        var node = new Check<TestContext>(c => c.Flag("x"));

        Assert.Equal(RunStatus.Failure, node.Tick(context));
        context.Flags.Add("x");
        Assert.Equal(RunStatus.Success, node.Tick(context));
    }
}

public sealed class TreeTests
{
    [Fact]
    public void AThrowingNodeStopsTheTreeRatherThanBeingSkipped()
    {
        // Continuing past a node that threw means acting from an unknown state. Stopping
        // leaves the character standing, which is recoverable.
        var tree = new BehaviorTree<TestContext>(
            new Do<TestContext>(_ => throw new InvalidOperationException("routine blew up")));

        Assert.Equal(RunStatus.Failure, tree.Tick(new TestContext()));
        Assert.True(tree.IsFaulted);
        Assert.IsType<InvalidOperationException>(tree.Fault);
    }

    [Fact]
    public void AFaultedTreeStaysStoppedUntilItIsReset()
    {
        var tree = new BehaviorTree<TestContext>(
            new Do<TestContext>(_ => throw new InvalidOperationException()));

        tree.Tick(new TestContext());
        long ticksWhenFaulted = tree.TickCount;

        tree.Tick(new TestContext());
        Assert.Equal(ticksWhenFaulted, tree.TickCount);

        tree.Reset();
        Assert.False(tree.IsFaulted);
    }

    [Fact]
    public void DescribeRendersTheTreeAndEachNodeStatus()
    {
        var tree = new BehaviorTree<TestContext>(
            new PrioritySelector<TestContext>(
                new Recorder("combat", RunStatus.Failure) { Name = "Combat" },
                new Recorder("rest") { Name = "Rest" }));

        tree.Tick(new TestContext());
        string description = tree.Describe();

        Assert.Contains("Combat", description, StringComparison.Ordinal);
        Assert.Contains("Rest", description, StringComparison.Ordinal);
        Assert.Contains("Success", description, StringComparison.Ordinal);
    }
}
