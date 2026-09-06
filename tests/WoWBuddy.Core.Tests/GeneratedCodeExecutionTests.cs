using WoWBuddy.Core.Execution;
using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Tests.Fakes;
using Xunit;

namespace WoWBuddy.Core.Tests;

/// <summary>
/// Executes the machine code the bot would inject, and checks what it does.
/// </summary>
/// <remarks>
/// The golden tests in <see cref="X86AssemblerTests"/> prove the bytes disassemble correctly.
/// These prove the resulting program behaves correctly: arguments arrive in the right order,
/// the stack balances, results land where the bot reads them, and the stub's handshake with
/// the game thread works frame by frame.
/// </remarks>
public sealed class GeneratedCodeExecutionTests
{
    private const nint DataAddress = 0x02000000;
    private const nint CodeAddress = 0x02100000;
    private static readonly nint StubAddress = CodeAddress + CommandBlock.StubOffset;
    private static readonly nint ThunkAddress = CodeAddress + CommandBlock.ThunkOffset;
    private const nint OriginalEndScene = 0x05000000;

    private static SimulatedClient NewClient()
    {
        var client = new SimulatedClient();
        // Map the pages the executor would have allocated.
        for (nint i = 0; i < CommandBlock.DataSize; i++)
        {
            client.WriteByte(DataAddress + i, 0);
        }

        for (nint i = 0; i < CommandBlock.CodeSize; i++)
        {
            client.WriteByte(CodeAddress + i, 0);
        }

        client.WriteUInt32(DataAddress + CommandBlock.OriginalFunctionOffset, (uint)OriginalEndScene);
        return client;
    }

    private static void InstallStub(SimulatedClient client) =>
        client.TryWriteBytes(StubAddress, StubCompiler.Compile(DataAddress, StubAddress, ThunkAddress));

    private static void InstallThunk(SimulatedClient client, RemoteCall call) =>
        client.TryWriteBytes(ThunkAddress, ThunkCompiler.Compile(call, DataAddress, ThunkAddress));

    private static uint ReadState(SimulatedClient client) =>
        client.ReadOrDefault<uint>(DataAddress + CommandBlock.StateOffset);

    // --- The stub's frame loop ----------------------------------------------------------

    [Fact]
    public void StubDoesNothingAndFallsThroughWhenThereIsNoWork()
    {
        SimulatedClient client = NewClient();
        InstallStub(client);
        InstallThunk(client, RemoteCall.To(0x00900000));

        var interpreter = new X86Interpreter(client, (_, _, _) => new CallOutcome());
        nint next = interpreter.Run(StubAddress);

        // Every frame must reach the real EndScene, whether or not the bot wanted anything.
        Assert.Equal(OriginalEndScene, next);
        Assert.Empty(interpreter.CallLog);
        Assert.Equal((uint)CommandState.Idle, ReadState(client));
    }

    [Fact]
    public void StubRunsTheThunkAndReportsCompletionWhenWorkIsRequested()
    {
        SimulatedClient client = NewClient();
        InstallStub(client);
        InstallThunk(client, RemoteCall.To(0x00900000));
        client.WriteUInt32(DataAddress + CommandBlock.StateOffset, (uint)CommandState.Requested);

        var interpreter = new X86Interpreter(client, (_, _, _) => new CallOutcome(Eax: 0x1234));
        nint next = interpreter.Run(StubAddress);

        Assert.Equal(OriginalEndScene, next);
        Assert.Equal(new nint[] { 0x00900000 }, interpreter.CallLog);
        Assert.Equal((uint)CommandState.Complete, ReadState(client));
        Assert.Equal(1u, client.ReadOrDefault<uint>(DataAddress + CommandBlock.SequenceOffset));
        Assert.Equal(0x1234u, client.ReadOrDefault<uint>(DataAddress + CommandBlock.ReturnLowOffset));
    }

    [Fact]
    public void StubRunsTheThunkExactlyOncePerRequest()
    {
        SimulatedClient client = NewClient();
        InstallStub(client);
        InstallThunk(client, RemoteCall.To(0x00900000));
        client.WriteUInt32(DataAddress + CommandBlock.StateOffset, (uint)CommandState.Requested);

        var interpreter = new X86Interpreter(client, (_, _, _) => new CallOutcome());

        // Three frames, one request. A stub that re-ran completed work would fire a spell
        // three times instead of once.
        interpreter.Run(StubAddress);
        interpreter.Run(StubAddress);
        interpreter.Run(StubAddress);

        Assert.Single(interpreter.CallLog);
        Assert.Equal(1u, client.ReadOrDefault<uint>(DataAddress + CommandBlock.SequenceOffset));
    }

    [Fact]
    public void SequenceNumberAdvancesOncePerCompletedCall()
    {
        SimulatedClient client = NewClient();
        InstallStub(client);
        InstallThunk(client, RemoteCall.To(0x00900000));

        var interpreter = new X86Interpreter(client, (_, _, _) => new CallOutcome());

        for (int i = 1; i <= 3; i++)
        {
            client.WriteUInt32(DataAddress + CommandBlock.StateOffset, (uint)CommandState.Requested);
            interpreter.Run(StubAddress);
            Assert.Equal((uint)i, client.ReadOrDefault<uint>(DataAddress + CommandBlock.SequenceOffset));
        }
    }

    // --- Calling conventions -------------------------------------------------------------

    [Fact]
    public void CdeclArgumentsArriveLeftmostFirstAndTheCallerCleansUp()
    {
        SimulatedClient client = NewClient();
        var call = new RemoteCall(0x00819210, [11u, 22u, 33u], CallingConvention.Cdecl, ReturnKind.None);
        InstallThunk(client, call);

        IReadOnlyList<uint>? seen = null;
        var interpreter = new X86Interpreter(client, (_, args, _) =>
        {
            seen = args;
            return new CallOutcome();
        });

        interpreter.Run(ThunkAddress);

        Assert.Equal(new uint[] { 11, 22, 33 }, seen);
        Assert.Empty(interpreter.RemainingStack);
    }

    [Fact]
    public void StdCallLeavesStackCleanupToTheCallee()
    {
        SimulatedClient client = NewClient();
        var call = new RemoteCall(0x00819210, [7u, 8u], CallingConvention.StdCall, ReturnKind.None);
        InstallThunk(client, call);

        var interpreter = new X86Interpreter(client, (_, _, _) => new CallOutcome(CalleeCleansStack: true));
        interpreter.Run(ThunkAddress);

        // A cdecl cleanup emitted here as well would unbalance the stack by eight bytes.
        Assert.Empty(interpreter.RemainingStack);
    }

    [Fact]
    public void ThisCallPassesTheObjectInEcx()
    {
        SimulatedClient client = NewClient();
        var call = new RemoteCall(
            0x007225E0, [0x1000u], CallingConvention.ThisCall, ReturnKind.Int32, ThisPointer: 0xABCD0000);
        InstallThunk(client, call);

        uint seenEcx = 0;
        var interpreter = new X86Interpreter(client, (_, _, ecx) =>
        {
            seenEcx = ecx;
            return new CallOutcome(Eax: 1, CalleeCleansStack: true);
        });

        interpreter.Run(ThunkAddress);

        Assert.Equal(0xABCD0000u, seenEcx);
    }

    [Fact]
    public void PreludeResultBecomesTheThisPointerOfTheMainCall()
    {
        // This is the shape of the Lua-result read: fetch the active player object, then call
        // a member function on it. Getting the chaining wrong would call a method on garbage.
        const nint getPlayer = 0x004038F0;
        const nint getText = 0x007225E0;
        const uint playerObject = 0x0BADF00D;

        SimulatedClient client = NewClient();
        var call = new RemoteCall(
            getText,
            [0x2000u, unchecked((uint)-1)],
            CallingConvention.ThisCall,
            ReturnKind.Int32,
            Prelude: RemoteCall.To(getPlayer));
        InstallThunk(client, call);

        uint seenEcx = 0;
        IReadOnlyList<uint>? seenArgs = null;

        var interpreter = new X86Interpreter(client, (function, args, ecx) =>
        {
            if (function == getPlayer)
            {
                return new CallOutcome(Eax: playerObject);
            }

            seenEcx = ecx;
            seenArgs = args;
            return new CallOutcome(Eax: 0x3000, CalleeCleansStack: true);
        });

        interpreter.Run(ThunkAddress);

        Assert.Equal(new nint[] { getPlayer, getText }, interpreter.CallLog);
        Assert.Equal(playerObject, seenEcx);
        Assert.Equal(new uint[] { 0x2000, unchecked((uint)-1) }, seenArgs);
        Assert.Equal(0x3000u, client.ReadOrDefault<uint>(DataAddress + CommandBlock.ReturnLowOffset));
    }

    // --- Return values --------------------------------------------------------------------

    [Fact]
    public void SixtyFourBitReturnsStoreBothHalves()
    {
        SimulatedClient client = NewClient();
        var call = new RemoteCall(0x00900000, [], CallingConvention.Cdecl, ReturnKind.Int64);
        InstallThunk(client, call);

        var interpreter = new X86Interpreter(client, (_, _, _) => new CallOutcome(Eax: 0x89ABCDEF, Edx: 0x01234567));
        interpreter.Run(ThunkAddress);

        Assert.Equal(0x89ABCDEFu, client.ReadOrDefault<uint>(DataAddress + CommandBlock.ReturnLowOffset));
        Assert.Equal(0x01234567u, client.ReadOrDefault<uint>(DataAddress + CommandBlock.ReturnHighOffset));
    }

    [Fact]
    public void FloatReturnsArePoppedFromTheFpuStack()
    {
        SimulatedClient client = NewClient();
        var call = new RemoteCall(0x00900000, [], CallingConvention.Cdecl, ReturnKind.Float);
        InstallThunk(client, call);

        var interpreter = new X86Interpreter(client, (_, _, _) => new CallOutcome(FloatResult: 1.5f));
        interpreter.Run(ThunkAddress);

        Assert.Equal(1.5f, client.ReadOrDefault<float>(DataAddress + CommandBlock.ReturnFloatOffset));
    }

    [Fact]
    public void NoFloatStoreIsEmittedForCallsThatDoNotReturnOne()
    {
        // Popping a value that was never pushed corrupts the client's FPU state, and the
        // damage would surface far away from the cause.
        SimulatedClient client = NewClient();
        InstallThunk(client, new RemoteCall(0x00900000, [], CallingConvention.Cdecl, ReturnKind.Int32));

        var interpreter = new X86Interpreter(client, (_, _, _) => new CallOutcome(Eax: 5));

        // The interpreter throws if fstp runs with an empty FPU stack.
        interpreter.Run(ThunkAddress);

        Assert.Equal(5u, client.ReadOrDefault<uint>(DataAddress + CommandBlock.ReturnLowOffset));
    }

    [Fact]
    public void NoResultIsStoredWhenTheCallReturnsNothing()
    {
        SimulatedClient client = NewClient();
        client.WriteUInt32(DataAddress + CommandBlock.ReturnLowOffset, 0xFEEDFACE);
        InstallThunk(client, new RemoteCall(0x00900000, [], CallingConvention.Cdecl, ReturnKind.None));

        var interpreter = new X86Interpreter(client, (_, _, _) => new CallOutcome(Eax: 0x11111111));
        interpreter.Run(ThunkAddress);

        Assert.Equal(0xFEEDFACEu, client.ReadOrDefault<uint>(DataAddress + CommandBlock.ReturnLowOffset));
    }

    // --- Limits ----------------------------------------------------------------------------

    [Fact]
    public void AThunkTooLargeForItsPageIsRejected()
    {
        uint[] arguments = Enumerable.Range(0, 17).Select(i => (uint)i).ToArray();
        var call = new RemoteCall(0x00900000, arguments);

        Assert.Throws<ArgumentException>(() => ThunkCompiler.Compile(call, DataAddress, ThunkAddress));
    }

    [Fact]
    public void AThisCallWithoutAnObjectIsRejected()
    {
        var call = new RemoteCall(0x00900000, [], CallingConvention.ThisCall);

        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => ThunkCompiler.Compile(call, DataAddress, ThunkAddress));
        Assert.Contains("ThisPointer", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStubFitsInTheSpaceReservedBeforeTheThunk()
    {
        byte[] stub = StubCompiler.Compile(DataAddress, StubAddress, ThunkAddress);

        Assert.True(
            stub.Length <= CommandBlock.ThunkOffset,
            $"The stub is {stub.Length} bytes and must fit within {CommandBlock.ThunkOffset}.");
    }
}
