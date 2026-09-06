using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Memory;

namespace WoWBuddy.Core.Execution;

/// <summary>
/// Runs calls on the game client's own thread.
/// </summary>
/// <remarks>
/// <para>
/// This is the piece the rest of the bot is built on. Reading the client's memory is enough
/// to know what is happening; doing anything about it means calling the client's own
/// functions, and those must run on the thread that owns the game state. So the bot leaves a
/// compiled call in the client's memory, the injected stub picks it up on the next rendered
/// frame, and the answer comes back through the same block.
/// </para>
/// <para>
/// <b>On the protected-function problem.</b> WoW refuses to let addon Lua call things like
/// <c>CastSpellByName</c> or <c>TargetUnit</c> when the call originated from untrusted code.
/// That protection lives in the Lua layer: it tracks taint across the script VM. The C
/// functions those Lua bindings wrap have no such notion, and neither does the render loop.
/// Calling them from here is therefore not a workaround for taint so much as a route that
/// never enters the taint system at all. It is also why the bot does not try to execute
/// protected Lua and never will: Lua is for reading state, native calls are for acting.
/// </para>
/// <para>
/// <b>Failure is latching.</b> If a call is not picked up within its deadline, the executor
/// marks itself broken and refuses further work. It cannot know whether the game thread is
/// merely stalled, on a loading screen, or gone; and if it later wakes up and runs a thunk
/// the bot has since overwritten, the client executes something nobody intended. Stopping is
/// the only safe response, and a stopped bot leaves a character standing.
/// </para>
/// </remarks>
public sealed class GameThreadExecutor : IDisposable
{
    /// <summary>How long a call may take before the executor gives up on the game thread.</summary>
    /// <remarks>
    /// A frame is normally under 50ms. This is generous enough to cover a stutter, a zoning
    /// hitch or a busy loading screen, and short enough that a user notices something is
    /// wrong rather than watching the bot hang.
    /// </remarks>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long to wait after unhooking before freeing the code page.
    /// </summary>
    /// <remarks>
    /// The game thread may be inside the stub at the moment the vtable slot is restored.
    /// Freeing the page it is executing would crash the client, so the executor waits out a
    /// generous number of frames first. This is a race that cannot be closed completely from
    /// outside the process, only made vanishingly unlikely.
    /// </remarks>
    public static readonly TimeSpan UnhookSettleTime = TimeSpan.FromMilliseconds(250);

    private readonly IProcessMemory _memory;
    private readonly EndSceneHook _hook;
    private readonly object _gate = new();

    private nint _dataAddress;
    private nint _codeAddress;
    private int _scratchUsed;
    private bool _disposed;

    public GameThreadExecutor(IProcessMemory memory, IModuleResolver modules)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _hook = new EndSceneHook(memory, modules);
    }

    /// <summary>Convenience constructor that resolves modules from a live process.</summary>
    public GameThreadExecutor(IProcessMemory memory, Process process)
        : this(memory, new ProcessModuleResolver(process))
    {
    }

    /// <summary>True once the stub is installed and the executor is usable.</summary>
    public bool IsInstalled { get; private set; }

    /// <summary>
    /// True when a call was not picked up in time. The executor refuses all further work.
    /// </summary>
    public bool IsBroken { get; private set; }

    /// <summary>Why the executor latched broken, or an empty string.</summary>
    public string BrokenReason { get; private set; } = string.Empty;

    /// <summary>Base of the data block in the client.</summary>
    public nint DataAddress => _dataAddress;

    /// <summary>Address of the resident stub in the client.</summary>
    public nint StubAddress => _codeAddress + CommandBlock.StubOffset;

    /// <summary>Address of the per-call thunk in the client.</summary>
    public nint ThunkAddress => _codeAddress + CommandBlock.ThunkOffset;

    /// <summary>The underlying hook, for diagnostics.</summary>
    public EndSceneHook Hook => _hook;

    /// <summary>
    /// Allocates the command block, writes the stub, and installs the hook.
    /// </summary>
    /// <param name="failure">Why installation failed, when it did.</param>
    public bool TryInstall(out string failure)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (IsInstalled)
            {
                failure = string.Empty;
                return true;
            }

            if (!_hook.TryResolve(out failure))
            {
                return false;
            }

            _dataAddress = _memory.Allocate(CommandBlock.DataSize, executable: false);
            if (_dataAddress == 0)
            {
                failure = "Could not allocate the command block in the client.";
                return false;
            }

            _codeAddress = _memory.Allocate(CommandBlock.CodeSize, executable: true);
            if (_codeAddress == 0)
            {
                _memory.Free(_dataAddress);
                _dataAddress = 0;
                failure = "Could not allocate the code page in the client.";
                return false;
            }

            // Zero the data block, then record where the real EndScene lives so the stub can
            // jump to it. This must be in place before the hook goes in: the very next frame
            // will read it.
            if (!_memory.TryWriteBytes(_dataAddress, new byte[CommandBlock.DataSize])
                || !WriteUInt32(_dataAddress + CommandBlock.OriginalFunctionOffset, (uint)_hook.OriginalEndScene))
            {
                FreeAllocations();
                failure = "Could not initialise the command block.";
                return false;
            }

            byte[] stub = StubCompiler.Compile(_dataAddress, StubAddress, ThunkAddress);
            if (!_memory.TryWriteBytes(StubAddress, stub))
            {
                FreeAllocations();
                failure = "Could not write the stub into the client.";
                return false;
            }

            // A thunk that does nothing, so that a frame landing between the hook going in
            // and the first real call cannot execute an uninitialised page.
            byte[] noop = new X86Assembler(ThunkAddress).Return().ToArray();
            if (!_memory.TryWriteBytes(ThunkAddress, noop))
            {
                FreeAllocations();
                failure = "Could not write the initial thunk into the client.";
                return false;
            }

            if (!_hook.Install(StubAddress))
            {
                FreeAllocations();
                failure = "Could not install the EndScene hook.";
                return false;
            }

            IsInstalled = true;
            failure = string.Empty;

            Log.For<GameThreadExecutor>().Information(
                "Game-thread execution installed. Data 0x{Data:X8}, code 0x{Code:X8}.",
                _dataAddress, _codeAddress);

            return true;
        }
    }

    /// <summary>
    /// Runs <paramref name="call"/> on the game thread and waits for it to finish.
    /// </summary>
    /// <param name="call">The call to make.</param>
    /// <param name="timeout">How long to wait. Defaults to <see cref="DefaultTimeout"/>.</param>
    /// <returns>What the call returned, or a failed result.</returns>
    public ExecutionResult Execute(RemoteCall call, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(call);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (IsBroken)
            {
                return ExecutionResult.Failed($"The game-thread executor is not usable: {BrokenReason}");
            }

            if (!IsInstalled)
            {
                return ExecutionResult.Failed("The game-thread executor is not installed.");
            }

            if (!_hook.IsInstalled)
            {
                Break("The EndScene hook is no longer in the vtable; something displaced it.");
                return ExecutionResult.Failed(BrokenReason);
            }

            byte[] thunk;
            try
            {
                thunk = ThunkCompiler.Compile(call, _dataAddress, ThunkAddress);
            }
            catch (ArgumentException ex)
            {
                return ExecutionResult.Failed(ex.Message);
            }

            // Ordering is the whole safety argument. The thunk and a cleared state must be in
            // the client before the request word says there is work, or the game thread can
            // run a half-written thunk.
            if (!WriteUInt32(_dataAddress + CommandBlock.StateOffset, (uint)CommandState.Idle)
                || !_memory.TryWriteBytes(ThunkAddress, thunk))
            {
                return ExecutionResult.Failed("Could not write the call into the client.");
            }

            uint sequenceBefore = ReadUInt32(_dataAddress + CommandBlock.SequenceOffset);

            if (!WriteUInt32(_dataAddress + CommandBlock.StateOffset, (uint)CommandState.Requested))
            {
                return ExecutionResult.Failed("Could not submit the call.");
            }

            return WaitForCompletion(sequenceBefore, timeout ?? DefaultTimeout);
        }
    }

    private ExecutionResult WaitForCompletion(uint sequenceBefore, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            var state = (CommandState)ReadUInt32(_dataAddress + CommandBlock.StateOffset);

            if (state == CommandState.Complete)
            {
                uint low = ReadUInt32(_dataAddress + CommandBlock.ReturnLowOffset);
                uint high = ReadUInt32(_dataAddress + CommandBlock.ReturnHighOffset);
                float single = ReadSingle(_dataAddress + CommandBlock.ReturnFloatOffset);

                WriteUInt32(_dataAddress + CommandBlock.StateOffset, (uint)CommandState.Idle);
                return ExecutionResult.Succeeded(low, high, single);
            }

            if (!_memory.IsValid)
            {
                Break("The client exited while a call was in flight.");
                return ExecutionResult.Failed(BrokenReason);
            }

            Thread.Sleep(1);
        }

        uint sequenceAfter = ReadUInt32(_dataAddress + CommandBlock.SequenceOffset);
        string detail = sequenceAfter == sequenceBefore
            ? "The game thread never picked the call up: the hook is not being reached, or the client is not rendering."
            : "The game thread is running but the call did not complete in time.";

        Break($"A call timed out after {timeout.TotalSeconds:F1}s. {detail}");
        return ExecutionResult.Failed(BrokenReason);
    }

    /// <summary>
    /// Copies bytes into the client's scratch area and returns their address.
    /// </summary>
    /// <remarks>
    /// Strings passed to client functions have to live in the client. The scratch area is a
    /// simple bump allocator reset by <see cref="ResetScratch"/>; call that at the start of
    /// each logical operation rather than trying to free individual pieces.
    /// </remarks>
    public nint WriteScratch(ReadOnlySpan<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsInstalled)
        {
            return 0;
        }

        // Keep entries 4-byte aligned so anything read back as a dword is aligned too.
        int aligned = (bytes.Length + 3) & ~3;

        lock (_gate)
        {
            if (_scratchUsed + aligned > CommandBlock.ScratchSize)
            {
                Log.For<GameThreadExecutor>().Warning(
                    "Scratch area exhausted ({Used}/{Total} bytes)", _scratchUsed, CommandBlock.ScratchSize);
                return 0;
            }

            nint address = _dataAddress + CommandBlock.ScratchOffset + _scratchUsed;
            if (!_memory.TryWriteBytes(address, bytes))
            {
                return 0;
            }

            _scratchUsed += aligned;
            return address;
        }
    }

    /// <summary>Writes a null-terminated string into the client's scratch area.</summary>
    public nint WriteScratchString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        byte[] bytes = Encoding.UTF8.GetBytes(value);
        byte[] terminated = new byte[bytes.Length + 1];
        bytes.CopyTo(terminated, 0);
        return WriteScratch(terminated);
    }

    /// <summary>Releases the scratch area for reuse.</summary>
    public void ResetScratch()
    {
        lock (_gate)
        {
            _scratchUsed = 0;
        }
    }

    /// <summary>Number of calls the game thread has completed since installation.</summary>
    public uint CompletedCallCount =>
        IsInstalled ? ReadUInt32(_dataAddress + CommandBlock.SequenceOffset) : 0;

    private void Break(string reason)
    {
        IsBroken = true;
        BrokenReason = reason;
        Log.For<GameThreadExecutor>().Error("Game-thread execution disabled: {Reason}", reason);
    }

    private bool WriteUInt32(nint address, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        return _memory.TryWriteBytes(address, buffer);
    }

    private uint ReadUInt32(nint address) => _memory.ReadOrDefault<uint>(address);

    private float ReadSingle(nint address) => _memory.ReadOrDefault<float>(address);

    private void FreeAllocations()
    {
        if (_codeAddress != 0)
        {
            _memory.Free(_codeAddress);
            _codeAddress = 0;
        }

        if (_dataAddress != 0)
        {
            _memory.Free(_dataAddress);
            _dataAddress = 0;
        }
    }

    /// <summary>
    /// Removes the hook and frees the injected memory, in that order.
    /// </summary>
    /// <remarks>
    /// Order matters more than anything else in this class. The vtable must stop pointing at
    /// bot memory before that memory goes away, and the game thread needs a moment to leave
    /// the stub before the page under it is released.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_gate)
        {
            if (!IsInstalled)
            {
                FreeAllocations();
                return;
            }

            bool restored = _hook.Uninstall();
            IsInstalled = false;

            if (!restored)
            {
                // Freeing now would leave the client jumping into unmapped memory. Leaking a
                // page is the lesser harm by a wide margin.
                Log.For<GameThreadExecutor>().Fatal(
                    "Leaving the injected pages allocated because the hook could not be removed.");
                return;
            }

            if (_memory.IsValid)
            {
                Thread.Sleep(UnhookSettleTime);
            }

            FreeAllocations();
            Log.For<GameThreadExecutor>().Information("Game-thread execution removed.");
        }
    }
}

/// <summary>What a call on the game thread produced.</summary>
public readonly record struct ExecutionResult
{
    private ExecutionResult(bool success, uint low, uint high, float single, string error)
    {
        Success = success;
        Low = low;
        High = high;
        Single = single;
        Error = error;
    }

    /// <summary>True when the call ran.</summary>
    public bool Success { get; }

    /// <summary>The 32-bit return value.</summary>
    public uint Low { get; }

    /// <summary>The high half of a 64-bit return value.</summary>
    public uint High { get; }

    /// <summary>The floating-point return value.</summary>
    public float Single { get; }

    /// <summary>Why the call failed, or an empty string.</summary>
    public string Error { get; }

    /// <summary>The return value as a pointer into the client.</summary>
    public nint Pointer => (nint)Low;

    /// <summary>The return value as a 64-bit integer.</summary>
    public ulong Int64 => ((ulong)High << 32) | Low;

    internal static ExecutionResult Succeeded(uint low, uint high, float single) =>
        new(true, low, high, single, string.Empty);

    internal static ExecutionResult Failed(string error) => new(false, 0, 0, 0f, error);
}
