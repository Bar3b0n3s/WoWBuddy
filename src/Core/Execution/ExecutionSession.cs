using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Attach;
using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Objects;

namespace WoWBuddy.Core.Execution;

/// <summary>
/// Everything the bot needs to act on the client, once it has proved it can.
/// </summary>
/// <remarks>
/// <para>
/// Obtaining one of these means code has been written into the client and the render loop is
/// running it. That is a much larger commitment than phase 1's read-only attach, so it is a
/// separate, explicit step rather than something that happens automatically: a user who only
/// wants to look at the object manager never has anything injected into their game.
/// </para>
/// <para>
/// The same rule as phase 1 applies, one level up. Attach refuses unless the offset table
/// checks out against the client; execution refuses unless the injected machinery is proved
/// to work against the client, by round-tripping a value the bot did not supply.
/// </para>
/// </remarks>
public sealed class ExecutionSession : IDisposable
{
    private readonly GameThreadExecutor _executor;
    private bool _disposed;

    private ExecutionSession(
        GameThreadExecutor executor,
        LuaBridge lua,
        NativeFunctions native,
        SpellCaster spells,
        ClickToMoveWriter clickToMove,
        VerificationReport report)
    {
        _executor = executor;
        Lua = lua;
        Native = native;
        Spells = spells;
        ClickToMove = clickToMove;
        Verification = report;
    }

    /// <summary>Lua execution and value reads.</summary>
    public LuaBridge Lua { get; }

    /// <summary>Native calls into the client.</summary>
    public NativeFunctions Native { get; }

    /// <summary>
    /// Spell casting. Only usable once the client has confirmed the bot's scripts are trusted;
    /// see <see cref="SpellCaster.CanCast"/>.
    /// </summary>
    public SpellCaster Spells { get; }

    /// <summary>The click-to-move block. Read-only until phase 3.</summary>
    public ClickToMoveWriter ClickToMove { get; }

    /// <summary>What was checked when execution was installed.</summary>
    public VerificationReport Verification { get; }

    /// <summary>The underlying executor, for diagnostics.</summary>
    public GameThreadExecutor Executor => _executor;

    /// <summary>True while the injected machinery is installed and working.</summary>
    public bool IsUsable => !_disposed && _executor.IsInstalled && !_executor.IsBroken;

    /// <summary>
    /// Installs the game-thread hook and proves it works.
    /// </summary>
    /// <param name="memory">Read/write access to the client.</param>
    /// <param name="modules">Module table, used to confirm the hook target really is Direct3D.</param>
    /// <param name="objects">The object manager, for the cross-checks.</param>
    /// <returns>A usable session, or a failure carrying the report.</returns>
    public static ExecutionInstallResult Install(
        IProcessMemory memory,
        IModuleResolver modules,
        ObjectManager objects)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(objects);

        var report = new VerificationReport();
        var executor = new GameThreadExecutor(memory, modules);

        if (!executor.TryInstall(out string failure))
        {
            report.Fail("Install hook", failure);
            executor.Dispose();
            return ExecutionInstallResult.Failed(report);
        }

        report.Pass("Install hook",
            $"EndScene redirected via vtable slot 0x{executor.Hook.VTableSlotAddress:X8}; " +
            $"stub at 0x{executor.StubAddress:X8}, data at 0x{executor.DataAddress:X8}.");

        try
        {
            var lua = new LuaBridge(executor, memory);
            var native = new NativeFunctions(executor, memory, objects);

            if (!VerifyGameThreadIsRunning(report, executor))
            {
                executor.Dispose();
                return ExecutionInstallResult.Failed(report);
            }

            if (!VerifyActivePlayer(report, native))
            {
                executor.Dispose();
                return ExecutionInstallResult.Failed(report);
            }

            if (!VerifyLua(report, lua))
            {
                executor.Dispose();
                return ExecutionInstallResult.Failed(report);
            }

            var spells = new SpellCaster(lua);

            // Casting is checked but not required. A user who only wants to read the world,
            // or whose client turns out to refuse protected calls, still gets a working
            // session; they simply cannot fight with it.
            if (spells.SelfTest(out string castingDetail))
            {
                report.Pass("Spell casting", castingDetail);
            }
            else
            {
                report.Warn("Spell casting", castingDetail);
            }

            var session = new ExecutionSession(
                executor, lua, native, spells, new ClickToMoveWriter(memory), report);
            Log.For<ExecutionSession>().Information("{Report}", report.ToString());
            return ExecutionInstallResult.Succeeded(session);
        }
        catch
        {
            executor.Dispose();
            throw;
        }
    }

    private static bool VerifyGameThreadIsRunning(VerificationReport report, GameThreadExecutor executor)
    {
        // A call that does nothing at all. Its only job is to come back, which proves the
        // render loop is reaching the stub and the handshake completes.
        ExecutionResult result = executor.Execute(
            RemoteCall.To(executor.ThunkAddress, returns: ReturnKind.None));

        if (!result.Success)
        {
            report.Fail("Game thread",
                $"The injected stub never ran a call: {result.Error} " +
                "The client may be minimised, on a loading screen, or not rendering.");
            return false;
        }

        report.Pass("Game thread",
            $"The render loop executed an injected call ({executor.CompletedCallCount} completed).");
        return true;
    }

    private static bool VerifyActivePlayer(VerificationReport report, NativeFunctions native)
    {
        if (!native.VerifyActivePlayerObject(out string detail))
        {
            report.Fail("Native call", detail);
            return false;
        }

        report.Pass("Native call", detail);
        return true;
    }

    private static bool VerifyLua(VerificationReport report, LuaBridge lua)
    {
        if (!lua.SelfTest(out string detail))
        {
            report.Fail("Lua round trip", detail);
            return false;
        }

        report.Pass("Lua round trip", detail);
        return true;
    }

    /// <summary>
    /// Removes the hook and frees the injected memory.
    /// </summary>
    /// <remarks>
    /// Must run before the bot exits. The client holds a pointer into memory this process
    /// owns, and leaving it there past our lifetime would crash the game on the next frame.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _executor.Dispose();
    }
}

/// <summary>The outcome of installing game-thread execution.</summary>
public sealed class ExecutionInstallResult
{
    private ExecutionInstallResult(ExecutionSession? session, VerificationReport report)
    {
        Session = session;
        Report = report;
    }

    /// <summary>The session, or null when installation failed.</summary>
    public ExecutionSession? Session { get; }

    /// <summary>What was checked and what was found.</summary>
    public VerificationReport Report { get; }

    /// <summary>True when execution is available.</summary>
    public bool Success => Session is not null;

    /// <summary>Why installation failed, or an empty string.</summary>
    public string FailureReason =>
        Report.FirstFailure is { } failure ? $"{failure.Name}: {failure.Detail}" : string.Empty;

    internal static ExecutionInstallResult Succeeded(ExecutionSession session) =>
        new(session, session.Verification);

    internal static ExecutionInstallResult Failed(VerificationReport report) => new(null, report);
}
