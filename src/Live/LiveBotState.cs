using WoWBuddy.Behavior;
using WoWBuddy.BotBases;
using WoWBuddy.BotBases.Battlegrounds;
using WoWBuddy.BotBases.Group;
using WoWBuddy.BotBases.Questing;
using WoWBuddy.BotBases.Support;
using WoWBuddy.CombatRoutines;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;
using WoWBuddy.Common.Scheduling;
using WoWBuddy.Core.Objects;
using WoWBuddy.Navigation;
using WoWBuddy.Navigation.Movement;
using WoWBuddy.Profiles;

namespace WoWBuddy.Live;

/// <summary>
/// The bot's picture of a running client: what the behaviour tree has been written against all
/// along.
/// </summary>
/// <remarks>
/// <para>
/// This is the join everything else was waiting for. Six bot bases, thirty combat routines, the
/// tree, profiles, group play and battlegrounds are all written against <see cref="IBotState"/>;
/// until now the only implementation was a fake in the test suite.
/// </para>
/// <para>
/// It is composition and nothing else. The memory-derived half comes through
/// <see cref="ICharacterView"/>, whose facts rest on offsets verified against the attached
/// client. The scripted half — quests, party, battleground queue, bags — comes through the four
/// Lua adapters, each of which switches itself off with a sentence when the client cannot do
/// what it needs. Movement comes through the controller, which will not run until it has been
/// explicitly enabled.
/// </para>
/// <para>
/// <b>Nothing here invents a fact.</b> Where a source is unavailable the property reports the
/// safe answer — no quests, no group, empty bags, no battleground — rather than something that
/// would read as usable. That is what lets a bot base run against a partially capable client
/// and simply do less, instead of doing something wrong.
/// </para>
/// </remarks>
public sealed class LiveBotState : IBotState, IProfileConditionContext
{
    private readonly ICharacterView _view;
    private readonly MovementController _movement;
    private readonly INavigationService _navigation;
    private readonly LuaQuestLog _quests;
    private readonly LuaPartyState _party;
    private readonly LuaBattlegrounds _battlegrounds;
    private readonly LuaInventory _inventory;
    private readonly LuaVendor _vendor;
    private readonly SessionScheduler _session;

    private readonly Func<DateTimeOffset> _clock;

    private Errand _errand = Errand.None;

    /// <summary>Composes a live state from the pieces that read a client.</summary>
    public LiveBotState(
        ICharacterView view,
        MovementController movement,
        INavigationService navigation,
        LuaQuestLog quests,
        LuaPartyState party,
        LuaBattlegrounds battlegrounds,
        LuaInventory inventory,
        LuaVendor vendor,
        SessionScheduler session,
        ICombatRoutine routine,
        ICombatContext combat,
        Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        Now = _clock();

        _view = view ?? throw new ArgumentNullException(nameof(view));
        _movement = movement ?? throw new ArgumentNullException(nameof(movement));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _quests = quests ?? throw new ArgumentNullException(nameof(quests));
        _party = party ?? throw new ArgumentNullException(nameof(party));
        _battlegrounds = battlegrounds ?? throw new ArgumentNullException(nameof(battlegrounds));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _vendor = vendor ?? throw new ArgumentNullException(nameof(vendor));
        _session = session ?? throw new ArgumentNullException(nameof(session));

        Routine = routine ?? throw new ArgumentNullException(nameof(routine));
        Combat = combat ?? throw new ArgumentNullException(nameof(combat));
    }

    // ---- what the routine sees -------------------------------------------------------------

    /// <inheritdoc />
    /// <remarks>
    /// Read once per tick by the runner rather than sampled per property, so that every
    /// decision in one tick agrees about what time it is. A tree that asked twice and got two
    /// answers could decide a break had started halfway through its own reasoning.
    /// </remarks>
    public DateTimeOffset Now { get; private set; }

    /// <inheritdoc />
    public Blackboard Blackboard { get; } = new();

    /// <inheritdoc />
    public ICombatRoutine Routine { get; }

    /// <inheritdoc />
    public ICombatContext Combat { get; }

    // ---- the character, from memory ---------------------------------------------------------

    /// <inheritdoc />
    public bool IsInWorld => _view.IsInWorld;

    /// <inheritdoc />
    public bool IsDead => _view.IsDead;

    /// <inheritdoc />
    public bool IsGhost => _view.IsGhost;

    /// <inheritdoc />
    public bool IsInCombat => _view.IsInCombat;

    /// <inheritdoc />
    public Vector3 Position => _view.Position;

    /// <inheritdoc />
    public int MapId => _view.MapId;

    /// <inheritdoc />
    public double HealthPercent => _view.HealthPercent;

    /// <inheritdoc />
    public int Level => _view.Level;

    /// <inheritdoc />
    public CandidateTarget? Target => _view.Target;

    /// <inheritdoc />
    public IReadOnlyList<CandidateTarget> NearbyEnemies => _view.NearbyEnemies;

    /// <inheritdoc />
    public IReadOnlyList<CandidateTarget> LootableCorpses => _view.LootableCorpses;

    /// <inheritdoc />
    public IReadOnlyList<VisibleObject> VisibleObjects => _view.VisibleObjects;

    /// <inheritdoc />
    public Vector3? CorpsePosition => _view.CorpsePosition;

    /// <inheritdoc />
    public bool IsLooting => _view.IsLooting;

    // ---- acting on it -----------------------------------------------------------------------

    /// <inheritdoc />
    public bool SetTarget(WoWGuid guid) => _view.SetTarget(guid);

    /// <inheritdoc />
    public bool Interact(WoWGuid guid) => _view.Interact(guid);

    /// <inheritdoc />
    public bool Loot(WoWGuid guid) => _view.Loot(guid);

    /// <inheritdoc />
    public bool ReleaseCorpse() => _view.ReleaseCorpse();

    /// <inheritdoc />
    public bool RetrieveCorpse() => _view.RetrieveCorpse();

    /// <inheritdoc />
    public bool StartResting() => _view.StartResting();

    // ---- moving -----------------------------------------------------------------------------

    /// <inheritdoc />
    public bool IsMoving => _movement.State == MovementState.Moving;

    /// <inheritdoc />
    public bool MovementFailed => _movement.State == MovementState.Failed;

    /// <inheritdoc />
    /// <remarks>
    /// Asking for the same destination twice is common — a bot base calls this on every tick
    /// while it walks — so a request that matches the path already being followed is left alone
    /// rather than restarting it. Recomputing would throw away the progress made and make the
    /// character stutter on the spot.
    /// </remarks>
    public bool MoveTo(Vector3 destination)
    {
        if (!WorldBounds.IsPlausible(destination))
        {
            // Refusing rather than walking: a destination outside the world came from a bad
            // read or a bad profile, and acting on it is how a character ends up under the map.
            Log.For<LiveBotState>().Warning(
                "Refusing to walk to {Destination}, which is not somewhere in the world",
                destination);
            return false;
        }

        if (_movement.State == MovementState.Moving
            && _movement.Destination is { } current
            && current.Distance(destination) < MovementController.DestinationTolerance)
        {
            return true;
        }

        NavigationPath path = _navigation.FindPath(MapId, Position, destination);

        if (!path.Success)
        {
            Log.For<LiveBotState>().Debug(
                "No path from {From} to {To} on map {Map}: {Reason}",
                Position, destination, MapId, path.Failure);
            return false;
        }

        return _movement.Follow(path.Points);
    }

    /// <inheritdoc />
    public void StopMoving() => _movement.Stop();

    // ---- housekeeping -----------------------------------------------------------------------

    /// <inheritdoc />
    public InventoryState Inventory => _inventory.State;

    /// <inheritdoc />
    public SessionState Session => _session.State;

    /// <inheritdoc />
    public Errand CurrentErrand => _errand;

    /// <inheritdoc />
    public bool BeginErrand(Errand errand)
    {
        if (_errand == errand)
        {
            return true;
        }

        Log.For<LiveBotState>().Information("Errand: {Errand}", errand);
        _errand = errand;
        return true;
    }

    /// <inheritdoc />
    public void EndErrand()
    {
        if (_errand == Errand.None)
        {
            return;
        }

        Log.For<LiveBotState>().Information("Errand finished: {Errand}", _errand);
        _errand = Errand.None;
    }

    /// <inheritdoc />
    public bool HasSellableItems => _inventory.HasSellableItems();

    /// <inheritdoc />
    public int ItemCount(uint itemId) => _inventory.Count(itemId);

    /// <inheritdoc />
    public bool UseItem(uint itemId) => _inventory.Use(itemId);

    // ---- the scripted half --------------------------------------------------------------------

    /// <inheritdoc />
    public IQuestLog Quests => _quests;

    /// <inheritdoc />
    public IPartyState Party => _party;

    /// <inheritdoc />
    public IBattlegroundActions Battlegrounds => _battlegrounds;

    /// <inheritdoc />
    public IVendorActions Vendor => _vendor;

    // ---- what a profile condition asks about ----------------------------------------------------

    /// <inheritdoc />
    /// <remarks>
    /// The same object answers both interfaces on purpose: a profile condition and a bot base
    /// asking the same question during one tick must get the same answer, and they will only do
    /// that if they are asking the same thing.
    /// </remarks>
    long IProfileConditionContext.Money => _inventory.State.Copper;

    /// <inheritdoc />
    int IProfileConditionContext.BagsFullPercent
    {
        get
        {
            InventoryState bags = _inventory.State;

            // Unknown bags read as empty rather than full. A bot that thinks its bags are full
            // stops doing anything useful, which is a worse failure than one that keeps looting.
            return bags.TotalSlots == 0 ? 0 : (int)Math.Round(bags.UsedFraction * 100d);
        }
    }

    /// <inheritdoc />
    bool IProfileConditionContext.IsQuestCompleted(uint questId) => _quests.IsCompleted(questId);

    /// <inheritdoc />
    bool IProfileConditionContext.IsQuestInLog(uint questId) => _quests.Find(questId) is not null;

    /// <inheritdoc />
    bool IProfileConditionContext.IsQuestReadyToTurnIn(uint questId) =>
        _quests.Find(questId) is { IsComplete: true };

    /// <inheritdoc />
    int IProfileConditionContext.ItemCount(uint itemId) => _inventory.Count(itemId);

    // ---- driving --------------------------------------------------------------------------------

    /// <summary>
    /// Moves the character along whatever path it is following.
    /// </summary>
    /// <remarks>
    /// Called once per tick, before the tree runs, so that the tree sees the position the
    /// character actually reached rather than the one it had when the last decision was made.
    /// </remarks>
    public MovementState Advance(DateTimeOffset now)
    {
        Now = now;
        return _movement.Tick(Position, now);
    }

    /// <summary>Updates the session clock, and reports whether the bot should still be playing.</summary>
    public SessionState UpdateSession(DateTimeOffset now) => _session.Update(now, Level);

    /// <summary>Throws away every cached reading, for when the world has changed underneath.</summary>
    /// <remarks>
    /// Zoning, a loading screen or a resurrection invalidate everything at once, and a reading
    /// taken before one of those is not stale so much as about a different situation.
    /// </remarks>
    public void InvalidateCaches()
    {
        _quests.Invalidate();
        _party.Invalidate();
        _battlegrounds.Invalidate();
        _inventory.Invalidate();
        _vendor.Invalidate();
    }
}
