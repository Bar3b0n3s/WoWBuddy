using System.Diagnostics;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Client;
using WoWBuddy.Core.Execution;
using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;

namespace WoWBuddy.Core.Attach;

/// <summary>
/// An attached, verified game client.
/// </summary>
/// <remarks>
/// <para>
/// This is the object the rest of the bot is handed. Getting one means the offset table has
/// already been checked against this specific client and the unit position layout has been
/// resolved; there is no way to obtain one without passing verification, which is the point.
/// </para>
/// <para>
/// Phase 1 is read-only. Nothing here writes to the client or injects anything.
/// </para>
/// </remarks>
public sealed class GameClient : IDisposable
{
    private readonly ProcessMemoryReader _reader;
    private bool _disposed;

    private ExecutionSession? _execution;

    private GameClient(
        ProcessMemoryReader reader,
        ClientBuild build,
        WoWGuid localPlayerGuid,
        Offsets335a.PositionLayout positionLayout,
        uint? gameObjectPositionOffset,
        VerificationReport verification,
        IModuleResolver modules)
    {
        _reader = reader;
        GameObjectPositionOffset = gameObjectPositionOffset;
        Modules = modules;
        Build = build;
        LocalPlayerGuid = localPlayerGuid;
        PositionLayout = positionLayout;
        Verification = verification;
        Objects = new ObjectManager(reader);
        Names = new PlayerNameCache(reader);
    }

    /// <summary>The client's version information.</summary>
    public ClientBuild Build { get; }

    /// <summary>GUID of the character that was logged in at attach time.</summary>
    public WoWGuid LocalPlayerGuid { get; }

    /// <summary>The unit position layout resolved for this client.</summary>
    public Offsets335a.PositionLayout PositionLayout { get; }

    /// <summary>
    /// Where game objects keep their position, worked out at attach, or null when it could
    /// not be established.
    /// </summary>
    /// <remarks>
    /// Null is survivable. Reading the world, fighting and moving all work without it; only
    /// gathering and anything else that must walk to a world object is blocked, and those
    /// say so rather than acting on a fabricated coordinate.
    /// </remarks>
    public uint? GameObjectPositionOffset { get; }

    /// <summary>The verification report produced at attach.</summary>
    public VerificationReport Verification { get; }

    /// <summary>The object manager for this client.</summary>
    public ObjectManager Objects { get; }

    /// <summary>Player name lookups for this client.</summary>
    public PlayerNameCache Names { get; }

    /// <summary>The raw memory reader, for the dev tools.</summary>
    public IMemoryReader Memory => _reader;

    /// <summary>Module table for the attached client, snapshotted at attach.</summary>
    public IModuleResolver Modules { get; }

    /// <summary>
    /// Game-thread execution, once <see cref="EnableExecution"/> has succeeded. Null until then.
    /// </summary>
    /// <remarks>
    /// Deliberately absent by default. Attaching is read-only and cannot destabilise the
    /// client; enabling execution writes code into it. Nothing should get that capability
    /// without having asked for it.
    /// </remarks>
    public ExecutionSession? Execution => _execution;

    /// <summary>
    /// Installs game-thread execution and proves it works against this client.
    /// </summary>
    /// <remarks>
    /// Idempotent while a working session exists. A session that has broken is discarded and
    /// reinstalled, because a broken executor never recovers on its own.
    /// </remarks>
    public ExecutionInstallResult EnableExecution()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_execution is { IsUsable: true } existing)
        {
            return ExecutionInstallResult.Succeeded(existing);
        }

        _execution?.Dispose();
        _execution = null;

        ExecutionInstallResult result = ExecutionSession.Install(_reader, Modules, Objects);
        _execution = result.Session;
        return result;
    }

    /// <summary>Process id of the attached client.</summary>
    public int ProcessId => _reader.ProcessId;

    /// <summary>
    /// True while the client is still running and still has the same character logged in.
    /// </summary>
    /// <remarks>
    /// The character check matters as much as the liveness check. If the user logs out and
    /// back in on a different character, every GUID the bot has remembered is meaningless,
    /// and continuing would have it act on stale identities.
    /// </remarks>
    public bool IsAttached =>
        !_disposed
        && _reader.IsValid
        && OffsetVerifier.StillAttachedTo(Objects, LocalPlayerGuid);

    /// <summary>
    /// Attaches to <paramref name="process"/>, verifying the offset table against it.
    /// </summary>
    /// <returns>
    /// A successful result carrying the client, or a failed one carrying the report that
    /// explains what did not check out.
    /// </returns>
    public static AttachResult Attach(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        ClientBuild build = ClientBuildDetector.Detect(process);
        Log.For<GameClient>().Information("Attaching to pid {Pid}: {Build}", process.Id, build);

        ProcessMemoryReader reader;
        try
        {
            reader = ProcessMemoryReader.Open(process);
        }
        catch (MemoryAccessException ex)
        {
            var report = new VerificationReport();
            report.Fail("Open process", ex.Message);
            return AttachResult.Failed(report);
        }

        try
        {
            VerificationReport report = OffsetVerifier.Verify(reader, build, out PositionResolution resolution);
            Log.For<GameClient>().Information("{Report}", report.ToString());

            if (!report.Passed)
            {
                reader.Dispose();
                return AttachResult.Failed(report);
            }

            var objectManager = new ObjectManager(reader);
            WoWGuid localPlayerGuid = objectManager.GetLocalPlayerGuid();

            // Game objects are resolved after units, because judging whether an object is
            // near the player needs the player's own position to be trustworthy first.
            GameObjectPositionResolution gameObjects = GameObjectPositionResolver.Resolve(
                reader,
                objectManager.EnumerateObjects(WoWObjectType.GameObject),
                ReadLocalPlayerPosition(reader, objectManager, resolution.Layout));

            if (gameObjects.Success)
            {
                report.Pass("Game object positions", gameObjects.Detail);
            }
            else
            {
                report.Warn("Game object positions", gameObjects.Detail);
            }

            var client = new GameClient(
                reader, build, localPlayerGuid, resolution.Layout, gameObjects.Offset, report,
                new ProcessModuleResolver(process));
            Log.For<GameClient>().Information(
                "Attached to pid {Pid} as player {Guid}", process.Id, localPlayerGuid);
            return AttachResult.Succeeded(client);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    /// <summary>Reads the local player's position through the resolved unit layout.</summary>
    private static Common.Geometry.Vector3 ReadLocalPlayerPosition(
        IMemoryReader reader, ObjectManager objects, Offsets335a.PositionLayout layout)
    {
        GameObjectRef player = objects.FindLocalPlayer();

        return player.IsValid
               && reader.TryReadVector3(player.Address + (nint)layout.PositionBlock, out var position)
            ? position
            : Common.Geometry.Vector3.Zero;
    }

    /// <summary>
    /// Attaches to the client with the given process id.
    /// </summary>
    /// <remarks>
    /// A process id that is not running is reported through the returned report like any
    /// other attach failure, so callers have one failure path rather than two.
    /// </remarks>
    public static AttachResult Attach(int processId)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            var report = new VerificationReport();
            report.Fail("Find client", $"No process with id {processId} is running.");
            return AttachResult.Failed(report);
        }

        using (process)
        {
            return Attach(process);
        }
    }

    /// <summary>
    /// Attaches to the single running 3.3.5a client.
    /// </summary>
    /// <remarks>
    /// Refuses to guess when several clients are running. Picking one arbitrarily would mean
    /// botting whichever character the user did not intend.
    /// </remarks>
    public static AttachResult AttachToSingleClient()
    {
        IReadOnlyList<WowClientCandidate> candidates = WowClientLocator.FindSupported();

        if (candidates.Count == 0)
        {
            var report = new VerificationReport();
            report.Fail("Find client",
                $"No running WoW {Offsets335a.SupportedVersion} build {Offsets335a.SupportedBuild} client was found. " +
                "Start the game and log a character into the world first.");
            return AttachResult.Failed(report);
        }

        if (candidates.Count > 1)
        {
            var report = new VerificationReport();
            report.Fail("Find client",
                $"{candidates.Count} clients are running: " +
                string.Join("; ", candidates.Select(c => c.Describe())) +
                ". Choose one explicitly rather than letting the bot guess.");
            return AttachResult.Failed(report);
        }

        return Attach(candidates[0].Process);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Order matters: the hook must come out of the client before the process handle that
        // owns the injected memory goes away.
        _execution?.Dispose();
        _execution = null;
        _reader.Dispose();
    }
}

/// <summary>The outcome of an attach attempt.</summary>
public sealed class AttachResult
{
    private AttachResult(GameClient? client, VerificationReport report)
    {
        Client = client;
        Report = report;
    }

    /// <summary>The attached client, or null when attach failed.</summary>
    public GameClient? Client { get; }

    /// <summary>What was checked and what was found.</summary>
    public VerificationReport Report { get; }

    /// <summary>True when a client was attached.</summary>
    public bool Success => Client is not null;

    /// <summary>The reason attach failed, or an empty string on success.</summary>
    public string FailureReason =>
        Report.FirstFailure is { } failure ? $"{failure.Name}: {failure.Detail}" : string.Empty;

    internal static AttachResult Succeeded(GameClient client) => new(client, client.Verification);

    internal static AttachResult Failed(VerificationReport report) => new(null, report);
}
