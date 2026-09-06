using WoWBuddy.Behavior;
using WoWBuddy.BotBases;
using WoWBuddy.BotBases.Support;
using WoWBuddy.CombatRoutines;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Scheduling;
using WoWBuddy.Core.Execution;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Tests;
using WoWBuddy.GameApi.Capabilities;
using WoWBuddy.GameApi.Enums;
using WoWBuddy.Live;
using WoWBuddy.Navigation.Movement;
using WoWBuddy.Profiles;
using Xunit;

namespace WoWBuddy.Live.Tests;

/// <summary>A routine that records what it was asked to do and does nothing.</summary>
/// <remarks>
/// The point of these tests is what the tree decides, not what a rotation casts. A real routine
/// against a stub context casts nothing and says nothing, which makes it impossible to tell
/// "the tree never asked" from "the routine had nothing to do" — and that difference is the
/// whole subject here.
/// </remarks>
internal sealed class ScriptedRoutine : ICombatRoutine
{
    public List<string> Calls { get; } = [];

    public string Name => "Scripted";

    public WoWClass Class => WoWClass.Warrior;

    public float PullRange => 5f;

    /// <summary>Whether the character is fit to fight, which is what stops it resting.</summary>
    public bool Ready { get; set; } = true;

    public bool Buff(ICombatContext context)
    {
        Calls.Add("Buff");
        return false;
    }

    public bool Pull(ICombatContext context)
    {
        Calls.Add("Pull");
        return true;
    }

    public bool Combat(ICombatContext context)
    {
        Calls.Add("Combat");
        return true;
    }

    public bool Rest(ICombatContext context)
    {
        Calls.Add("Rest");
        return !Ready;
    }

    public bool PetControl(ICombatContext context) => false;

    public bool IsReadyToFight(ICombatContext context) => Ready;
}

/// <summary>
/// The whole bot, from the runner down to a real bot base, over a real live state.
/// </summary>
/// <remarks>
/// <para>
/// Every other test in this project checks one piece against a fake of its neighbours. That
/// leaves one thing untested: the seams. Every defect found in this project since the bot bases
/// were written has been at a seam — an errand nothing could finish, a setting the window
/// collected and the composition root never passed, a trainer errand waiting for a merchant's
/// window. None of those is visible from inside any single component, and all of them are
/// visible from here.
/// </para>
/// <para>
/// So this builds the real <see cref="BotRunner"/> over the real <see cref="RootTree"/> over a
/// real bot base, against a real <see cref="LiveBotState"/> whose Lua is answered by hand. The
/// only fakes are the client itself and the two things that talk to it directly: the object
/// manager's readings, and pathfinding.
/// </para>
/// </remarks>
public sealed class EndToEndTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly Vector3 Camp = new(1500f, -2500f, 60f);
    private static readonly Vector3 Town = new(1600f, -2400f, 60f);

    private const uint MerchantEntry = 5000;

    private sealed record Rig(
        BotRunner Runner,
        LiveBotState State,
        FakeCharacterView View,
        FakeLua Lua,
        ScriptedRoutine Routine,
        Counter BotBase);

    /// <summary>Counts how many ticks reached the bot base.</summary>
    internal sealed class Counter
    {
        public int Ticks { get; private set; }

        public Node<IBotState> Node => new Do<IBotState>(_ =>
        {
            Ticks++;
            return RunStatus.Running;
        })
        { Name = "Bot base" };
    }

    /// <summary>
    /// Builds the whole thing.
    /// </summary>
    /// <param name="botBase">
    /// What to run when nothing has gone wrong. A counter by default, so a test can say "the
    /// bot base never got a look in" — which is what every priority rule in the root tree
    /// actually means.
    /// </param>
    /// <param name="errands">Vendors for the errand handler, when a test needs one.</param>
    private static Rig Build(
        Node<IBotState>? botBase = null,
        IReadOnlyList<ProfileVendor>? errands = null,
        SessionSchedule? schedule = null)
    {
        FakeLua lua = FakeLua.Typical335a();
        CapabilityReport capabilities = CapabilityProbes.Probe(lua);

        FakeCharacterView view = new() { Position = Camp };
        ScriptedRoutine routine = new();

        MovementController movement = new(new ClickToMoveWriter(new FakeClientMemory()));
        movement.Enable();

        LiveBotState state = new(
            view,
            movement,
            new FakeNavigation(),
            new LuaQuestLog(lua, capabilities, () => Now),
            new LuaPartyState(lua, capabilities, () => view.Position, view.Locate, () => Now),
            new LuaBattlegrounds(lua, capabilities, () => Now),
            new LuaInventory(lua, capabilities, () => Now),
            new LuaVendor(lua, capabilities, () => Now),
            new LuaTrainer(lua, capabilities, () => Now),
            new LuaTalents(lua, capabilities),
            new LuaTravel(lua, capabilities, () => false, string.Empty),
            new SessionScheduler(schedule ?? new SessionSchedule()),
            routine,
            new StubCombat(),
            () => Now);

        Counter counter = new();

        ErrandPlanner? planner = null;
        Node<IBotState>? handler = null;

        if (errands is { Count: > 0 })
        {
            planner = new ErrandPlanner(new ErrandSettings { TrainingEnabled = false });

            handler = new ErrandHandler(
                new ErrandHandlerSettings { Vendors = errands },
                () => Now)
                .Build();
        }

        BotRunner runner = new(
            state,
            RootTree.Build(botBase ?? counter.Node, planner, handler).Root);

        runner.Start();

        return new Rig(runner, state, view, lua, routine, counter);
    }

    private static CandidateTarget Enemy(ulong guid, Vector3 where, bool alive = true) =>
        new(new WoWGuid(guid), where, where.Distance(Camp), Level: 20, HealthPercent: 100d,
            IsAlive: alive, IsInCombat: false, IsTargetingMe: false, Entry: 1234);

    [Fact]
    public void AGrindingCharacterPicksATargetAndFightsIt()
    {
        Rig rig = Build(new GrindBotBase(new GrindSettings { Hotspots = [Camp] }).Build());

        rig.View.NearbyEnemies = [Enemy(0x10, new Vector3(1510f, -2500f, 60f))];

        Assert.Equal(TickOutcome.Ran, rig.Runner.Tick(Now));
        Assert.Contains("SetTarget(0x0000000000000010)", rig.View.Actions);

        // Now it has one, the combat branch takes over and the routine is asked to fight.
        rig.View.Target = rig.View.NearbyEnemies[0];
        rig.Runner.Tick(Now);

        Assert.Contains("Combat", rig.Routine.Calls);
    }

    [Fact]
    public void DyingBeatsEverythingElseTheBotMightHaveDone()
    {
        Rig rig = Build();

        rig.View.IsDead = true;
        rig.View.NearbyEnemies = [Enemy(0x10, Camp)];

        rig.Runner.Tick(Now);

        Assert.Contains("ReleaseCorpse", rig.View.Actions);
        Assert.Equal(0, rig.BotBase.Ticks);
        Assert.DoesNotContain("Combat", rig.Routine.Calls);
    }

    [Fact]
    public void AGhostStandingOnItsCorpseTakesTheBodyBack()
    {
        Rig rig = Build();

        rig.View.IsDead = true;
        rig.View.IsGhost = true;
        rig.View.CorpsePosition = Camp;

        rig.Runner.Tick(Now);

        Assert.Contains("RetrieveCorpse", rig.View.Actions);
    }

    [Fact]
    public void ACharacterTooHurtToFightRestsBeforeTheBotBaseGetsALook()
    {
        Rig rig = Build();
        rig.Routine.Ready = false;

        rig.Runner.Tick(Now);

        Assert.Contains("Rest", rig.Routine.Calls);
        Assert.Equal(0, rig.BotBase.Ticks);
    }

    [Fact]
    public void NothingIsReadOrDecidedDuringALoadingScreen()
    {
        // Reading an object manager that belongs to nobody produces decisions about nobody.
        Rig rig = Build();
        rig.View.IsInWorld = false;

        Assert.Equal(TickOutcome.NotInWorld, rig.Runner.Tick(Now));
        Assert.Empty(rig.View.Actions);
        Assert.Equal(0, rig.BotBase.Ticks);
    }

    [Fact]
    public void AFullBagErrandRunsAndHandsControlBackTheSameTick()
    {
        // The defect this pins down took a night of a real session: the errand began, the branch
        // reported Running, and nothing cleared it. It is checked here rather than only against
        // a fake state because the clearing is split across three components — the root tree,
        // the handler, and LiveBotState's own errand verbs.
        Rig rig = Build(errands: [new ProfileVendor("Innkeeper", MerchantEntry, 0, Town)]);

        rig.View.Position = Town;
        rig.View.VisibleObjects =
        [
            new VisibleObject(new WoWGuid(0x20), MerchantEntry, Town, HasPosition: true, Distance: 1f),
        ];

        // Full bags, sound gear, and a merchant window already open, answered through the real
        // Lua readers rather than around them. Durability is left high on purpose: repair comes
        // before selling in the planner, and this test is about the selling path.
        FullBagsAtAnOpenMerchant(rig.Lua);

        rig.Runner.Tick(Now);

        // The junk actually left the bags, which is the only assertion here that cannot pass by
        // the errand never having started at all.
        // The junk actually left the bags, which is the only assertion here that cannot pass by
        // the errand never having started at all.
        Assert.Contains("execute: UseContainerItem(0, 1)", rig.Lua.Asked);

        Assert.Equal(Errand.None, rig.State.CurrentErrand);
        Assert.Equal(1, rig.BotBase.Ticks);
    }

    [Fact]
    public void ABreakStopsTheCharacterAndNotTheBot()
    {
        // A break means stop playing, not stop existing: the runner keeps ticking so that dying
        // or being attacked is still noticed.
        Rig rig = Build(schedule: new SessionSchedule
        {
            WorkInterval = TimeSpan.Zero,
            BreakLength = TimeSpan.FromMinutes(10),
            Jitter = 0d,
        });

        Assert.Equal(TickOutcome.Ran, rig.Runner.Tick(Now));
        Assert.Equal(SessionState.OnBreak, rig.State.Session);
        Assert.Equal(0, rig.BotBase.Ticks);
    }

    [Fact]
    public void StoppingLeavesTheCharacterWhereItStands()
    {
        Rig rig = Build();

        rig.Runner.Tick(Now);
        rig.Runner.Stop();

        Assert.False(rig.Runner.IsRunning);
        Assert.Equal(TickOutcome.Stopped, rig.Runner.Tick(Now));
    }

    /// <summary>
    /// Answers the readers the errand needs, each with the reading it asks for.
    /// </summary>
    /// <remarks>
    /// They share one Lua global for their results, exactly as they do in a real client, so the
    /// answer has to be chosen by which script just ran. Getting that wrong in the fake would
    /// hand the bag reader the inventory summary and hide a parsing bug behind a passing test.
    /// </remarks>
    private static void FullBagsAtAnOpenMerchant(FakeLua lua)
    {
        const string bags = "0\u001F1\u001FBroken Fang\u001F0\u001F10\u001F1\u001FMiscellaneous\u001FJunk\u001F\u001F1\u001F12";
        const string inventory = "0\u001F16\u001F100.0\u001F100000";

        lua.Answers["(MerchantFrame and MerchantFrame:IsVisible()) and true or false"] = "true";
        lua.Answers["(MailFrame and MailFrame:IsVisible()) and true or false"] = "false";
        lua.Answers["CanMerchantRepair() and true or false"] = "true";
        lua.Answers["(select(2, GetRepairAllCost())) and true or false"] = "true";

        // Answered with the bot's own script rather than a copy of it: a fixture holding its own
        // wording would go on passing after the real one changed, and this test would quietly
        // stop testing anything. It nearly did — the first version of it passed while the errand
        // never ran at all.
        lua.Answers[LuaInventory.SellableScript] = "true";
        lua.Answers["__wowbuddy_result"] = inventory;

        lua.OnExecute = script =>
            lua.Answers["__wowbuddy_result"] =
                script.Contains("GetContainerItemLink(bag, slot)", StringComparison.Ordinal)
                && script.Contains("GetItemInfo(link)", StringComparison.Ordinal)
                    ? bags
                    : inventory;
    }
}
