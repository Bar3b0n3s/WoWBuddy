using WoWBuddy.BotBases.Support;
using WoWBuddy.CombatRoutines;
using WoWBuddy.CombatRoutines.Routines;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Scheduling;
using WoWBuddy.Core.Execution;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Tests;
using WoWBuddy.GameApi.Capabilities;
using WoWBuddy.Live;
using WoWBuddy.Navigation.Movement;
using WoWBuddy.Profiles;
using Xunit;

namespace WoWBuddy.Live.Tests;

public sealed class LiveBotStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private const string Field = "\u001F";
    private const string Row = "\u001E";

    private sealed record Parts(
        LiveBotState State,
        FakeCharacterView View,
        FakeLua Lua,
        FakeNavigation Navigation,
        MovementController Movement);

    private static Parts Build(FakeLua? lua = null, bool movementEnabled = true)
    {
        lua ??= FakeLua.Typical335a();

        CapabilityReport capabilities = CapabilityProbes.Probe(lua);

        FakeCharacterView view = new();
        FakeNavigation navigation = new();

        MovementController movement = new(new ClickToMoveWriter(new FakeClientMemory()));

        if (movementEnabled)
        {
            movement.Enable();
        }

        LiveBotState state = new(
            view,
            movement,
            navigation,
            new LuaQuestLog(lua, capabilities, () => Now),
            new LuaPartyState(lua, capabilities, () => view.Position, view.Locate, () => Now),
            new LuaBattlegrounds(lua, capabilities, () => Now),
            new LuaInventory(lua, capabilities, () => Now),
            new LuaVendor(lua, capabilities, () => Now),
            new SessionScheduler(new SessionSchedule()),
            new FuryWarrior(),
            new StubCombat(),
            () => Now);

        return new Parts(state, view, lua, navigation, movement);
    }

    [Fact]
    public void TheVerifiedHalfIsPassedStraightThrough()
    {
        Parts parts = Build();

        parts.View.Position = new Vector3(100f, 200f, 50f);
        parts.View.HealthPercent = 42d;
        parts.View.Level = 61;
        parts.View.IsInCombat = true;

        Assert.Equal(new Vector3(100f, 200f, 50f), parts.State.Position);
        Assert.Equal(42d, parts.State.HealthPercent);
        Assert.Equal(61, parts.State.Level);
        Assert.True(parts.State.IsInCombat);
    }

    [Fact]
    public void VerbsReachTheClient()
    {
        Parts parts = Build();

        parts.State.SetTarget(new WoWGuid(0x11));
        parts.State.Interact(new WoWGuid(0x22));
        parts.State.Loot(new WoWGuid(0x33));
        parts.State.ReleaseCorpse();
        parts.State.StartResting();

        Assert.Equal(5, parts.View.Actions.Count);
        Assert.Contains("ReleaseCorpse", parts.View.Actions);
        Assert.Contains("StartResting", parts.View.Actions);
    }

    [Fact]
    public void WalkingSomewhereAsksForAPathAndFollowsIt()
    {
        Parts parts = Build();

        Assert.True(parts.State.MoveTo(new Vector3(1600f, -2500f, 60f)));
        Assert.Equal(1, parts.Navigation.Requests);
        Assert.True(parts.State.IsMoving);
    }

    [Fact]
    public void AskingForTheSameDestinationAgainDoesNotRestartThePath()
    {
        // A bot base calls MoveTo on every tick while it walks. Recomputing would throw away
        // the progress made and make the character stutter on the spot.
        Parts parts = Build();
        Vector3 destination = new(1600f, -2500f, 60f);

        parts.State.MoveTo(destination);
        parts.State.MoveTo(destination);
        parts.State.MoveTo(destination);

        Assert.Equal(1, parts.Navigation.Requests);
    }

    [Fact]
    public void ANewDestinationDoesRestartThePath()
    {
        Parts parts = Build();

        parts.State.MoveTo(new Vector3(1600f, -2500f, 60f));
        parts.State.MoveTo(new Vector3(1700f, -2400f, 60f));

        Assert.Equal(2, parts.Navigation.Requests);
    }

    [Fact]
    public void ADestinationOutsideTheWorldIsRefusedRatherThanWalkedTo()
    {
        // It came from a bad read or a bad profile, and acting on it is how a character ends up
        // under the map.
        Parts parts = Build();

        Assert.False(parts.State.MoveTo(new Vector3(500000f, 0f, 0f)));
        Assert.Equal(0, parts.Navigation.Requests);
    }

    [Fact]
    public void NoPathIsAFailureRatherThanAWalkInAStraightLine()
    {
        Parts parts = Build();
        parts.Navigation.CanFindPaths = false;

        Assert.False(parts.State.MoveTo(new Vector3(1600f, -2500f, 60f)));
        Assert.False(parts.State.IsMoving);
    }

    [Fact]
    public void MovementThatWasNeverEnabledDoesNotMove()
    {
        // The controller is a gate of its own: nothing writes to the client's click-to-move
        // block until someone explicitly turned movement on.
        Parts parts = Build(movementEnabled: false);

        Assert.False(parts.State.MoveTo(new Vector3(1600f, -2500f, 60f)));
    }

    [Fact]
    public void TheScriptedHalfIsWiredUp()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["GetItemCount(2589)"] = "6";

        Parts parts = Build(lua);

        Assert.Equal(6, parts.State.ItemCount(2589));
        Assert.NotNull(parts.State.Quests);
        Assert.NotNull(parts.State.Party);
        Assert.NotNull(parts.State.Battlegrounds);
    }

    [Fact]
    public void AClientThatCannotAnswerReportsTheSafeThingRatherThanSomethingUsable()
    {
        // A bot base running against a partially capable client should do less, not something
        // wrong.
        FakeLua lua = FakeLua.Typical335a();
        lua.Functions.Remove("GetQuestLink");
        lua.Functions.Remove("UnitAffectingCombat");
        lua.Functions.Remove("GetMoney");

        Parts parts = Build(lua);

        Assert.Empty(parts.State.Quests.Entries);
        Assert.Empty(parts.State.Party.Members);
        Assert.Equal(0, parts.State.Inventory.TotalSlots);
    }

    [Fact]
    public void AProfileConditionAndABotBaseGetTheSameAnswer()
    {
        // They will only agree if they are asking the same object, which is why one class
        // answers both interfaces.
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["GetItemCount(2589)"] = "4";

        Parts parts = Build(lua);
        IProfileConditionContext condition = parts.State;

        Assert.Equal(parts.State.ItemCount(2589), condition.ItemCount(2589));
    }

    [Fact]
    public void UnknownBagsReadAsEmptyRatherThanFull()
    {
        // A bot that thinks its bags are full stops doing anything useful, which is a worse
        // failure than one that keeps looting.
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = null;

        IProfileConditionContext condition = Build(lua).State;

        Assert.Equal(0, condition.BagsFullPercent);
    }

    [Fact]
    public void BagsThatCanBeReadAreReportedAsAPercentage()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Join(Field, "16", "80", "100.0", "500");

        IProfileConditionContext condition = Build(lua).State;

        Assert.Equal(80, condition.BagsFullPercent);
        Assert.Equal(500L, condition.Money);
    }

    [Fact]
    public void AQuestReadyToHandInIsDistinguishedFromOneMerelyInTheLog()
    {
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Join(Row,
            string.Join(Field, "9001", "Done", "1"),
            string.Join(Field, "9002", "Working", "0"));

        IProfileConditionContext condition = Build(lua).State;

        Assert.True(condition.IsQuestInLog(9001));
        Assert.True(condition.IsQuestReadyToTurnIn(9001));

        Assert.True(condition.IsQuestInLog(9002));
        Assert.False(condition.IsQuestReadyToTurnIn(9002));

        Assert.False(condition.IsQuestInLog(9003));
    }

    [Fact]
    public void TheTimeIsReadOncePerTickRatherThanSampledPerProperty()
    {
        // A tree that asked twice and got two answers could decide a break had started halfway
        // through its own reasoning.
        Parts parts = Build();

        parts.State.Advance(Now);

        Assert.Equal(Now, parts.State.Now);
        Assert.Equal(parts.State.Now, parts.State.Now);
    }

    [Fact]
    public void AnErrandIsRememberedUntilItChanges()
    {
        Parts parts = Build();

        Assert.Equal(Errand.None, parts.State.CurrentErrand);

        parts.State.BeginErrand(Errand.Repair);

        Assert.Equal(Errand.Repair, parts.State.CurrentErrand);
    }

    [Fact]
    public void ZoningThrowsAwayEveryCachedReadingAtOnce()
    {
        // A reading taken before a loading screen is not stale so much as about a different
        // situation.
        FakeLua lua = FakeLua.Typical335a();
        lua.Answers["__wowbuddy_result"] = string.Empty;

        Parts parts = Build(lua);

        _ = parts.State.Quests.Entries;
        _ = parts.State.Party.Members;
        _ = parts.State.Inventory;

        parts.State.InvalidateCaches();

        int before = lua.Asked.Count;

        _ = parts.State.Quests.Entries;
        _ = parts.State.Party.Members;
        _ = parts.State.Inventory;

        Assert.True(lua.Asked.Count > before);
    }

    [Fact]
    public void TheSessionClockDrivesWhetherToKeepPlaying()
    {
        Parts parts = Build();

        Assert.Equal(SessionState.Running, parts.State.UpdateSession(Now));
        Assert.Equal(SessionState.Running, parts.State.Session);
    }
}
