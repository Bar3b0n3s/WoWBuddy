using WoWBuddy.Core.Execution;
using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Offsets;
using WoWBuddy.Core.Tests.Fakes;
using Xunit;

namespace WoWBuddy.Core.Tests;

/// <summary>
/// Drives the executor against a client whose render loop is simulated by actually
/// interpreting the injected stub.
/// </summary>
/// <remarks>
/// The "game thread" here runs the same machine code the bot would inject, against the same
/// command block, so the handshake, the timeout behaviour and the result path are exercised
/// as a whole rather than as pieces.
/// </remarks>
public sealed class GameThreadExecutorTests : IDisposable
{
    private const nint D3DBase = 0x6D000000;
    private const nint EndSceneAddress = D3DBase + 0x12340;

    private readonly SimulatedClient _client = new();
    private readonly FakeModuleResolver _modules =
        new FakeModuleResolver().Add("d3d9.dll", D3DBase, 0x100000);

    private CancellationTokenSource? _renderLoop;
    private Task? _renderTask;

    public GameThreadExecutorTests()
    {
        nint deviceOuter = _client.Allocate(0x8000);
        nint device = _client.Allocate(0x100);
        nint vtable = _client.Allocate(0x200);

        _client.WritePointer(
            Offsets335a.Rebase(Offsets335a.Execution.D3DDevicePointer1, _client.ModuleBase), deviceOuter);
        _client.WritePointer(deviceOuter + (nint)Offsets335a.Execution.D3DDevicePointer2, device);
        _client.WritePointer(device, vtable);
        _client.WritePointer(vtable + (nint)Offsets335a.Execution.D3DEndSceneVTableOffset, EndSceneAddress);
    }

    /// <summary>Starts a background thread that runs the injected stub once per "frame".</summary>
    private void StartRenderLoop(Func<nint, IReadOnlyList<uint>, uint, CallOutcome> handler, nint stubAddress)
    {
        _renderLoop = new CancellationTokenSource();
        CancellationToken token = _renderLoop.Token;

        _renderTask = Task.Run(
            () =>
            {
                var interpreter = new X86Interpreter(_client, handler);
                while (!token.IsCancellationRequested)
                {
                    interpreter.Run(stubAddress);
                    Thread.Sleep(1);
                }
            },
            token);
    }

    [Fact]
    public void InstallAllocatesTheBlocksAndHooksTheVtable()
    {
        using var executor = new GameThreadExecutor(_client, _modules);

        Assert.True(executor.TryInstall(out string failure), failure);
        Assert.True(executor.IsInstalled);
        Assert.True(executor.Hook.IsInstalled);
        Assert.NotEqual(0, executor.DataAddress);

        // The stub needs the original function's address before the first frame runs it.
        Assert.Equal(
            (uint)EndSceneAddress,
            _client.ReadOrDefault<uint>(executor.DataAddress + CommandBlock.OriginalFunctionOffset));
    }

    [Fact]
    public void InstallRefusesWhenTheHookTargetIsNotDirect3D()
    {
        using var executor = new GameThreadExecutor(_client, new FakeModuleResolver());

        Assert.False(executor.TryInstall(out string failure));
        Assert.False(executor.IsInstalled);
        Assert.Contains("refusing to hook", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ACallRunsOnTheRenderLoopAndReturnsItsValue()
    {
        using var executor = new GameThreadExecutor(_client, _modules);
        Assert.True(executor.TryInstall(out string failure), failure);

        StartRenderLoop((function, _, _) =>
            function == 0x00900000 ? new CallOutcome(Eax: 0xCAFEBABE) : new CallOutcome(),
            executor.StubAddress);

        ExecutionResult result = executor.Execute(
            RemoteCall.To(0x00900000), TimeSpan.FromSeconds(10));

        Assert.True(result.Success, result.Error);
        Assert.Equal(0xCAFEBABEu, result.Low);
    }

    [Fact]
    public void ArgumentsReachTheCalledFunctionInOrder()
    {
        using var executor = new GameThreadExecutor(_client, _modules);
        Assert.True(executor.TryInstall(out _));

        IReadOnlyList<uint>? seen = null;
        StartRenderLoop((function, args, _) =>
        {
            if (function == 0x00900000)
            {
                seen = args;
            }

            return new CallOutcome();
        }, executor.StubAddress);

        executor.Execute(
            new RemoteCall(0x00900000, [1u, 2u, 3u], CallingConvention.Cdecl, ReturnKind.None),
            TimeSpan.FromSeconds(10));

        Assert.Equal(new uint[] { 1, 2, 3 }, seen);
    }

    [Fact]
    public void SeveralCallsRunInSequence()
    {
        using var executor = new GameThreadExecutor(_client, _modules);
        Assert.True(executor.TryInstall(out _));

        uint next = 100;
        StartRenderLoop((_, _, _) => new CallOutcome(Eax: next++), executor.StubAddress);

        for (uint expected = 100; expected < 105; expected++)
        {
            ExecutionResult result = executor.Execute(
                RemoteCall.To(0x00900000), TimeSpan.FromSeconds(10));

            Assert.True(result.Success, result.Error);
            Assert.Equal(expected, result.Low);
        }

        Assert.Equal(5u, executor.CompletedCallCount);
    }

    [Fact]
    public void ACallThatIsNeverPickedUpTimesOutAndDisablesTheExecutor()
    {
        // No render loop: the client is frozen, minimised, or the hook was never reached.
        using var executor = new GameThreadExecutor(_client, _modules);
        Assert.True(executor.TryInstall(out _));

        ExecutionResult result = executor.Execute(
            RemoteCall.To(0x00900000), TimeSpan.FromMilliseconds(150));

        Assert.False(result.Success);
        Assert.True(executor.IsBroken);
        Assert.Contains("never picked the call up", executor.BrokenReason, StringComparison.Ordinal);
    }

    [Fact]
    public void ABrokenExecutorRefusesFurtherWork()
    {
        // Latching matters: a game thread that wakes up later would run whatever thunk is in
        // the page at that moment, which must not be a call the bot has since replaced.
        using var executor = new GameThreadExecutor(_client, _modules);
        executor.TryInstall(out _);
        executor.Execute(RemoteCall.To(0x00900000), TimeSpan.FromMilliseconds(100));

        Assert.True(executor.IsBroken);

        ExecutionResult second = executor.Execute(RemoteCall.To(0x00900000), TimeSpan.FromSeconds(5));

        Assert.False(second.Success);
        Assert.Contains("not usable", second.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ADisplacedHookIsDetectedRatherThanCalledInto()
    {
        using var executor = new GameThreadExecutor(_client, _modules);
        executor.TryInstall(out _);

        _client.WritePointer(executor.Hook.VTableSlotAddress, 0x07000000);

        ExecutionResult result = executor.Execute(RemoteCall.To(0x00900000), TimeSpan.FromSeconds(1));

        Assert.False(result.Success);
        Assert.Contains("displaced", executor.BrokenReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DisposeRestoresTheVtableAndFreesTheInjectedMemory()
    {
        var executor = new GameThreadExecutor(_client, _modules);
        Assert.True(executor.TryInstall(out _));

        nint slot = executor.Hook.VTableSlotAddress;
        nint data = executor.DataAddress;

        executor.Dispose();

        Assert.Equal(EndSceneAddress, _client.ReadPointerOrZero(slot));
        Assert.DoesNotContain(data, _client.Allocations.Keys);
    }

    [Fact]
    public void ScratchHoldsStringsForTheClientToRead()
    {
        using var executor = new GameThreadExecutor(_client, _modules);
        executor.TryInstall(out _);

        nint address = executor.WriteScratchString("UnitLevel(\"player\")");

        Assert.NotEqual(0, address);
        Assert.True(_client.TryReadCString(address, out string read));
        Assert.Equal("UnitLevel(\"player\")", read);
    }

    [Fact]
    public void ScratchRefusesToOverrunItsArea()
    {
        using var executor = new GameThreadExecutor(_client, _modules);
        executor.TryInstall(out _);

        Assert.NotEqual(0, executor.WriteScratch(new byte[CommandBlock.ScratchSize - 4]));
        Assert.Equal(0, executor.WriteScratch(new byte[64]));

        executor.ResetScratch();

        Assert.NotEqual(0, executor.WriteScratch(new byte[64]));
    }

    [Fact]
    public void ExecutingBeforeInstallingIsRefused()
    {
        using var executor = new GameThreadExecutor(_client, _modules);

        ExecutionResult result = executor.Execute(RemoteCall.To(0x00900000));

        Assert.False(result.Success);
        Assert.Contains("not installed", result.Error, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _renderLoop?.Cancel();
        try
        {
            _renderTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // The loop is torn down with the test; a cancellation here is not a failure.
        }

        _renderLoop?.Dispose();
    }
}
