using System;
using System.IO;
using System.Threading;
using WoWBuddy.Behavior;
using WoWBuddy.BotBases;
using WoWBuddy.BotBases.Support;
using WoWBuddy.Core.Attach;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Execution;
using WoWBuddy.GameApi;
using WoWBuddy.GameApi.Capabilities;
using WoWBuddy.Common.Scheduling;
using WoWBuddy.Live;
using WoWBuddy.Navigation;
using WoWBuddy.Navigation.Movement;
using WoWBuddy.CombatRoutines;
using WoWBuddy.Presentation;
using WoWBuddy.Profiles;

namespace WoWBuddy.UI;

/// <summary>
/// Attaching to a client, and what the window can do with one.
/// </summary>
/// <remarks>
/// <para>
/// Attaching, verifying the offset table and reading the character are real and work today.
/// Starting is not, and this class says so rather than appearing to run.
/// </para>
/// <para>
/// <b>What is missing is the live <see cref="IBotState"/>.</b> Everything above this line —
/// six bot bases, thirty combat routines, the behaviour tree, profiles, group play — is written
/// against that interface and tested against a fake implementation of it. Nothing yet implements
/// it against a running client: doing so means reading the quest log, the party, the bags and
/// the battleground queue out of a real 3.3.5a client, and most of that is Lua this project has
/// not verified. It is the next piece of work, and it is deliberately not faked here. A start
/// button that ticked a tree fed on invented values would look like progress and be worth less
/// than nothing.
/// </para>
/// </remarks>
public sealed class BotController : IBotController
{
    private GameClient? _client;
    private World? _world;
    private Node<IBotState>? _tree;
    private CapabilityReport? _capabilities;
    private BotRunner? _runner;
    private Timer? _timer;
    private MovementController? _movement;
    private SpellCaster? _caster;
    private int _ticking;

    /// <inheritdoc />
    public bool IsAttached => _client is not null;

    /// <inheritdoc />
    public bool IsRunning => _runner is { IsRunning: true };

    /// <inheritdoc />
    public bool CanExecute => _client?.Execution is { IsUsable: true };

    /// <inheritdoc />
    public string ClientCapabilities { get; private set; } = string.Empty;

    /// <inheritdoc />
    public string CharacterSummary
    {
        get
        {
            if (_world?.Me is not { } me)
            {
                return string.Empty;
            }

            return $"{me.Name} — level {me.Level} {me.Class}, {me.HealthPercent:F0}% health";
        }
    }

    /// <inheritdoc />
    public AttachOutcome Attach(ClientOption client)
    {
        Detach();

        // Attaching by id rather than by handing over a Process: the core layer already deals
        // with a process that has since exited, and there is no reason to do it twice.
        AttachResult result = GameClient.Attach(client.ProcessId);

        if (!result.Success)
        {
            return new AttachOutcome(
                false,
                result.Report.ToString(),
                "Attach failed. See the verification report.");
        }

        _client = result.Client;
        _world = new World(_client!);

        return new AttachOutcome(true, result.Report.ToString(), "Attached.");
    }

    /// <summary>
    /// Installs the execution hook, proves the Lua round trip, and asks what the client can do.
    /// </summary>
    /// <remarks>
    /// Three steps in one because the second and third are only meaningful after the first, and
    /// each refuses rather than continuing: a hook that did not install means no Lua, a Lua
    /// round trip that did not prove itself means every capability answer would be a guess, and
    /// a capability that is missing means a feature is switched off rather than left to fail
    /// four hours in.
    /// </remarks>
    public string EnableExecution()
    {
        if (_client is null)
        {
            return "Attach to a client first.";
        }

        ExecutionInstallResult install = _client.EnableExecution();

        if (!install.Success)
        {
            ClientCapabilities = install.Report.ToString();
            return $"Could not install the execution hook. {install.FailureReason}";
        }

        LuaBridge lua = install.Session!.Lua;

        if (!lua.SelfTest(out string detail))
        {
            ClientCapabilities = $"The Lua round trip could not be proved against this client: {detail}";
            return "Execution installed, but Lua could not be proved. The bot will not act on the game.";
        }

        // Casting has a gate of its own, and it is the reason this project casts through the
        // client's scripting rather than a native address it could not verify: the test asks
        // the client whether the bot's scripts run in a secure context, and casting stays off
        // if the answer is no.
        _caster = new SpellCaster(lua);

        if (!_caster.SelfTest(out string castDetail))
        {
            Log.For<BotController>().Warning(
                "Casting is unavailable on this client: {Reason}", castDetail);
        }

        CapabilityReport report = CapabilityProbes.Probe(lua);

        _capabilities = report;
        ClientCapabilities = report.Describe()
            + Environment.NewLine
            + (_caster.CanCast
                ? "Casting: available."
                : $"Casting: unavailable. {_caster.UnavailableReason}");

        return report.NothingUnexpected
            ? "Execution enabled."
            : "Execution enabled, but this client is missing calls the bot expected. Read the "
                + "capabilities report before running anything.";
    }

    /// <inheritdoc />
    public void Detach()
    {
        Stop();

        ClientCapabilities = string.Empty;
        _caster = null;
        _world = null;
        _client?.Dispose();
        _client = null;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Composes the whole bot and starts ticking it. Every refusal before that point is a
    /// sentence the user can act on rather than a silent failure: the tree is built here, so a
    /// profile that does not give its bot base what it needs is caught before anything runs.
    /// </remarks>
    public string Start(string botBase, string routine, string? profilePath)
    {
        if (_client is null || _world is null)
        {
            return "Attach to a client first.";
        }

        if (_client.Execution is not { IsUsable: true } execution || _capabilities is null)
        {
            return "Enable execution first.";
        }

        Profile? profile = null;

        if (!string.IsNullOrEmpty(profilePath))
        {
            ProfileLoadResult loaded = ProfileLoader.LoadFile(profilePath);

            if (!loaded.Success)
            {
                return "That profile cannot be used. See the profile report.";
            }

            profile = loaded.Profile;
        }

        ICombatRoutine? chosen = new RoutineCatalogue().ByName(routine);

        if (chosen is null)
        {
            return $"There is no combat routine called '{routine}'.";
        }

        BotBaseBuild built = BotBaseFactory.Create(botBase, profile);

        if (!built.Success)
        {
            return built.Message;
        }

        _tree = built.Tree;

        LiveBotState state = Compose(execution, chosen);

        // Errands only run when the profile says where the vendors are. Without that the
        // branch stays off, which is the right answer: the planner would keep deciding a trip
        // was due and nothing would ever be able to make it.
        ErrandPlanner? planner = null;
        Node<IBotState>? errandHandler = null;

        if (profile is { Vendors.Count: > 0 })
        {
            planner = new ErrandPlanner(new ErrandSettings());

            errandHandler = new ErrandHandler(new ErrandHandlerSettings
            {
                Vendors = profile.Vendors,
            })
            .Build();
        }

        // Movement is the last gate. Nothing writes to the client's click-to-move block until
        // this line, and a user who never gets here has had nothing injected that moves them.
        _movement!.Enable();

        _runner = new BotRunner(state, RootTree.Build(_tree!, planner, errandHandler).Root);
        _runner.Start();

        // A tick every quarter second. Faster buys nothing — the client's own update rate is
        // the floor on how quickly anything the bot reads can change — and costs a round trip
        // onto the game thread each time.
        _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(250));

        return built.Message;
    }

    /// <summary>Builds the live picture of the client from the pieces that read it.</summary>
    private LiveBotState Compose(ExecutionSession execution, ICombatRoutine routine)
    {
        LuaBridge lua = execution.Lua;

        WorldCharacterView view = new(
            _world!,
            new NativeFunctions(execution.Executor, _client!.Memory, _client.Objects),
            lua);

        _movement = new MovementController(execution.ClickToMove);

        // Navigation data comes from the user's own extraction, and a missing folder is not an
        // error here: the bot simply cannot walk, MoveTo says so, and the bases that need it
        // report it rather than the whole thing refusing to start.
        INavigationService navigation = new DetourNavigationService(
            Path.Combine(AppContext.BaseDirectory, "mmaps"));

        return new LiveBotState(
            view,
            _movement,
            navigation,
            new LuaQuestLog(lua, _capabilities!),
            new LuaPartyState(lua, _capabilities!, () => view.Position, view.Locate),
            new LuaBattlegrounds(lua, _capabilities!),
            new LuaInventory(lua, _capabilities!),
            new LuaVendor(lua, _capabilities!),
            new SessionScheduler(new SessionSchedule()),
            routine,
            new LiveCombatContext(lua, view, _caster));
    }

    /// <summary>
    /// Runs one tick, never overlapping with itself.
    /// </summary>
    /// <remarks>
    /// The timer fires on a thread pool thread, and a tick that runs long — a slow round trip
    /// onto the game thread — would otherwise have the next one start on top of it. Two ticks
    /// at once would read a half-updated picture and act on it twice.
    /// </remarks>
    private void Tick()
    {
        if (Interlocked.Exchange(ref _ticking, 1) == 1)
        {
            return;
        }

        try
        {
            if (_runner?.Tick(DateTimeOffset.UtcNow) is TickOutcome.Stopped or TickOutcome.Faulted)
            {
                StopTimer();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _ticking, 0);
        }
    }

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <inheritdoc />
    public void Stop()
    {
        StopTimer();

        _runner?.Stop();
        _runner = null;
        _tree = null;
        _movement = null;
    }
}
