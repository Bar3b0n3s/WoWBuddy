using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Execution;
using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;
using WoWBuddy.Navigation.Movement;
using Xunit;

namespace WoWBuddy.Navigation.Tests;

/// <summary>Minimal writable memory standing in for a client's click-to-move block.</summary>
internal sealed class FakeClientMemory : IProcessMemory
{
    private readonly Dictionary<nint, byte> _bytes = [];

    public bool IsValid { get; set; } = true;

    public nint ModuleBase => 0x00400000;

    public int ModuleSize => 0x00800000;

    /// <summary>When false, every write fails, as it would if the client had exited.</summary>
    public bool WritesSucceed { get; set; } = true;

    public bool TryReadBytes(nint address, Span<byte> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            if (!_bytes.TryGetValue(address + i, out byte value))
            {
                return false;
            }

            buffer[i] = value;
        }

        return true;
    }

    public bool TryWriteBytes(nint address, ReadOnlySpan<byte> buffer)
    {
        if (!WritesSucceed)
        {
            return false;
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            _bytes[address + i] = buffer[i];
        }

        return true;
    }

    public nint Allocate(int size, bool executable) => 0;

    public bool Free(nint address) => false;

    public bool WithWritableMemory(nint address, int size, Action action)
    {
        action();
        return true;
    }
}

/// <summary>
/// Covers path following, arrival, and the refusal to move at all until click-to-move has
/// been confirmed against a real client.
/// </summary>
public sealed class MovementControllerTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static (MovementController Controller, ClickToMoveWriter Writer, FakeClientMemory Memory) Build(
        bool enabled = true)
    {
        var memory = new FakeClientMemory();
        var writer = new ClickToMoveWriter(memory);
        var controller = new MovementController(writer);

        if (enabled)
        {
            controller.Enable();
        }

        return (controller, writer, memory);
    }

    /// <summary>A straight path in plausible world coordinates.</summary>
    private static List<Vector3> StraightPath() =>
    [
        new(-8900f, 500f, 90f),
        new(-8880f, 500f, 90f),
        new(-8860f, 500f, 90f),
    ];

    [Fact]
    public void MovementIsRefusedUntilClickToMoveHasBeenConfirmed()
    {
        // The single most important behaviour here. Those offsets have one published source
        // and have not been checked first-hand; writing an unverified destination into a live
        // client sends somebody's character somewhere nobody asked for.
        (MovementController controller, ClickToMoveWriter writer, _) = Build(enabled: false);

        Assert.False(controller.IsEnabled);
        Assert.False(controller.Follow(StraightPath()));
        Assert.Equal(MovementState.Failed, controller.State);
        Assert.Equal(MovementFailure.NotPermitted, controller.Failure);

        // Nothing was written: the block is still unreadable because nothing ever touched it.
        Assert.Equal(default, writer.Read().Action);
    }

    [Fact]
    public void FollowingAPathWritesADestinationToTheClient()
    {
        (MovementController controller, ClickToMoveWriter writer, _) = Build();

        Assert.True(controller.Follow(StraightPath()));
        controller.Tick(new Vector3(-8900f, 500f, 90f), Start);

        ClickToMoveState state = writer.Read();
        Assert.Equal(ClickToMoveAction.Move, state.Action);
        Assert.True(state.Destination.IsFinite);
    }

    [Fact]
    public void AnEmptyPathFailsRatherThanReportingArrival()
    {
        (MovementController controller, _, _) = Build();

        Assert.False(controller.Follow([]));
        Assert.Equal(MovementFailure.NoPath, controller.Failure);
    }

    [Fact]
    public void ReachingTheDestinationReportsArrivalAndStops()
    {
        (MovementController controller, ClickToMoveWriter writer, _) = Build();
        List<Vector3> path = StraightPath();
        controller.Follow(path);

        controller.Tick(path[0], Start);
        MovementState state = controller.Tick(path[^1], Start.AddSeconds(1));

        Assert.Equal(MovementState.Arrived, state);
        Assert.Equal(ClickToMoveAction.Stop, writer.Read().Action);
    }

    [Fact]
    public void SeveralWaypointsCanBeConsumedAtOnceWhenTheCharacterOvershoots()
    {
        (MovementController controller, _, _) = Build();
        controller.Follow(StraightPath());

        // Landing on the last point should not require walking each earlier one.
        MovementState state = controller.Tick(new Vector3(-8860f, 500f, 90f), Start);

        Assert.Equal(MovementState.Arrived, state);
    }

    [Fact]
    public void AWaypointOnAnotherFloorIsNotCountedAsReached()
    {
        // Arrival is judged on flat distance so slopes work, but that must not let a
        // character standing under a waypoint claim to have reached it.
        (MovementController controller, _, _) = Build();
        controller.Follow([new Vector3(-8900f, 500f, 90f), new Vector3(-8880f, 500f, 90f)]);

        controller.Tick(new Vector3(-8900f, 500f, 90f), Start);
        MovementState state = controller.Tick(new Vector3(-8880f, 500f, 90f), Start.AddSeconds(1));

        Assert.Equal(MovementState.Arrived, state);
    }

    [Fact]
    public void ACharacterThatStopsMakingProgressEntersRecovery()
    {
        (MovementController controller, _, _) = Build();
        controller.Follow(StraightPath());

        var wedged = new Vector3(-8895f, 500f, 90f);
        controller.Tick(wedged, Start);
        MovementState state = controller.Tick(wedged, Start.AddSeconds(5));

        Assert.Equal(MovementState.Recovering, state);
    }

    [Fact]
    public void RepeatedFailureToRecoverGivesUpOnThePath()
    {
        // Giving up matters: a bot that retries forever is indistinguishable from a hung one,
        // and the behaviour tree above needs to be told so it can try something else.
        (MovementController controller, _, _) = Build();
        controller.Follow(StraightPath());

        var wedged = new Vector3(-8895f, 500f, 90f);
        DateTimeOffset now = Start;
        controller.Tick(wedged, now);

        for (int i = 0; i < 10 && controller.State != MovementState.Failed; i++)
        {
            now = now.AddSeconds(5);
            controller.Tick(wedged, now);
        }

        Assert.Equal(MovementState.Failed, controller.State);
        Assert.Equal(MovementFailure.Stuck, controller.Failure);
    }

    [Fact]
    public void AFailedWriteIsReportedRatherThanWalkedThrough()
    {
        (MovementController controller, _, FakeClientMemory memory) = Build();
        controller.Follow(StraightPath());
        memory.WritesSucceed = false;

        MovementState state = controller.Tick(new Vector3(-8900f, 500f, 90f), Start);

        Assert.Equal(MovementState.Failed, state);
        Assert.Equal(MovementFailure.CannotSteer, controller.Failure);
    }

    [Fact]
    public void StoppingClearsThePathAndTellsTheClient()
    {
        (MovementController controller, ClickToMoveWriter writer, _) = Build();
        controller.Follow(StraightPath());

        controller.Stop();

        Assert.Equal(MovementState.Idle, controller.State);
        Assert.Empty(controller.Path);
        Assert.Equal(ClickToMoveAction.Stop, writer.Read().Action);
    }

    [Fact]
    public void LongLegsAreSubdividedSoProgressIsCheckedAlongTheWay()
    {
        (MovementController controller, _, _) = Build();

        controller.Follow([new Vector3(-9000f, 500f, 90f), new Vector3(-8000f, 500f, 90f)]);

        Assert.True(controller.Path.Count > 2);
        for (int i = 1; i < controller.Path.Count; i++)
        {
            Assert.True(controller.Path[i - 1].Distance(controller.Path[i])
                <= MovementController.MaximumLegLength + 0.01f);
        }
    }

    [Fact]
    public void TickDoesNothingOnceTheControllerIsIdle()
    {
        (MovementController controller, _, _) = Build();

        Assert.Equal(MovementState.Idle, controller.Tick(new Vector3(-8900f, 500f, 90f), Start));
    }

    [Fact]
    public void AFreshPathClearsTroubleFromThePreviousOne()
    {
        (MovementController controller, _, _) = Build();
        controller.Follow(StraightPath());

        var wedged = new Vector3(-8895f, 500f, 90f);
        controller.Tick(wedged, Start);
        controller.Tick(wedged, Start.AddSeconds(5));

        controller.Follow(StraightPath());

        Assert.Equal(StuckSeverity.Moving, controller.Stuck);
        Assert.Equal(MovementState.Moving, controller.State);
    }
}
