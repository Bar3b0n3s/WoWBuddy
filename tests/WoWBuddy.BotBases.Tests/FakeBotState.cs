using WoWBuddy.Behavior;
using WoWBuddy.BotBases.Battlegrounds;
using WoWBuddy.BotBases.Group;
using WoWBuddy.BotBases.Questing;
using WoWBuddy.BotBases;
using WoWBuddy.BotBases.Support;
using WoWBuddy.CombatRoutines;
using WoWBuddy.Common.Scheduling;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Objects;
using WoWBuddy.GameApi.Enums;

namespace WoWBuddy.BotBases.Tests;

/// <summary>A combat context that records what the routine was asked to do.</summary>
public sealed class FakeCombatContext : ICombatContext
{
    public List<string> CastLog { get; } = [];

    public UnitSnapshot Me { get; set; } =
        new(new WoWGuid(1), 100d, 100d, 100, 80, true, 0f, WoWGuid.Zero);

    public UnitSnapshot? Target { get; set; }

    public UnitSnapshot? Pet { get; set; }

    public int EnemiesInMelee { get; set; }

    public bool IsCasting { get; set; }

    public bool IsSpellReady(string spellName) => true;

    public bool HasAura(UnitSnapshot unit, string auraName) => false;

    public bool Cast(string spellName, bool onSelf = false)
    {
        CastLog.Add(spellName);
        return true;
    }
}

/// <summary>A routine that records which of its phases the tree called.</summary>
public sealed class RecordingRoutine : ICombatRoutine
{
    public List<string> Calls { get; } = [];

    public string Name => "Recording";

    public WoWClass Class => WoWClass.Warrior;

    public float PullRange { get; set; } = 5f;

    /// <summary>What <see cref="IsReadyToFight"/> reports.</summary>
    public bool Ready { get; set; } = true;

    /// <summary>How many more times <see cref="Rest"/> should say it is still needed.</summary>
    public int RestTicksRemaining { get; set; }

    public bool Buff(ICombatContext context)
    {
        Calls.Add("Buff");
        return true;
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

        if (RestTicksRemaining <= 0)
        {
            return false;
        }

        RestTicksRemaining--;
        return true;
    }

    public bool PetControl(ICombatContext context)
    {
        Calls.Add("PetControl");
        return false;
    }

    public bool IsReadyToFight(ICombatContext context) => Ready;
}

/// <summary>A situation the behaviour tree can be asked to reason about.</summary>
public sealed class FakeBotState : IBotState
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public Blackboard Blackboard { get; } = new();

    public RecordingRoutine RecordingRoutine { get; } = new();

    public ICombatRoutine Routine => RecordingRoutine;

    public FakeCombatContext CombatContext { get; } = new();

    public ICombatContext Combat => CombatContext;

    public bool IsInWorld { get; set; } = true;

    public bool IsDead { get; set; }

    public bool IsGhost { get; set; }

    public bool IsInCombat { get; set; }

    public Vector3 Position { get; set; } = new(-8900f, 500f, 90f);

    public int MapId { get; set; }

    public double HealthPercent { get; set; } = 100d;

    public CandidateTarget? Target { get; set; }

    public IReadOnlyList<CandidateTarget> NearbyEnemies { get; set; } = [];

    public Vector3? CorpsePosition { get; set; }

    public bool IsMoving { get; set; }

    public bool MovementFailed { get; set; }

    /// <summary>What the tree asked the bot to do, in order.</summary>
    public List<string> Actions { get; } = [];

    /// <summary>Destinations the tree asked to move to.</summary>
    public List<Vector3> MoveRequests { get; } = [];

    /// <summary>When false, MoveTo reports that no route was found.</summary>
    public bool CanMove { get; set; } = true;

    public bool SetTarget(WoWGuid guid)
    {
        Actions.Add($"SetTarget({guid})");
        CandidateTarget match = NearbyEnemies.FirstOrDefault(e => e.Guid == guid);
        Target = match.Guid == guid ? match : null;
        return true;
    }

    public bool MoveTo(Vector3 destination)
    {
        Actions.Add("MoveTo");
        MoveRequests.Add(destination);
        IsMoving = CanMove;
        return CanMove;
    }

    public void StopMoving()
    {
        Actions.Add("StopMoving");
        IsMoving = false;
    }

    public bool ReleaseCorpse()
    {
        Actions.Add("ReleaseCorpse");
        IsGhost = true;
        return true;
    }

    public bool RetrieveCorpse()
    {
        Actions.Add("RetrieveCorpse");
        return true;
    }

    public bool StartResting()
    {
        Actions.Add("StartResting");
        return true;
    }

    public int Level { get; set; } = 70;

    public InventoryState Inventory { get; set; } = new(16, 16, 100d, 100_000);

    public SessionState Session { get; set; } = SessionState.Running;

    public IReadOnlyList<CandidateTarget> LootableCorpses { get; set; } = [];

    public bool IsLooting { get; set; }

    public Errand CurrentErrand { get; set; } = Errand.None;

    /// <summary>When false, BeginErrand reports the bot does not know where to go.</summary>
    public bool KnowsWhereErrandsAre { get; set; } = true;

    public bool Loot(WoWGuid guid)
    {
        Actions.Add($"Loot({guid})");
        IsLooting = true;
        return true;
    }

    /// <summary>Whether the bags hold anything a vendor would pay for.</summary>
    public bool HasSellableItems { get; set; } = true;

    /// <summary>Business with a vendor or a mailbox.</summary>
    public FakeVendor VendorState { get; } = new();

    public IVendorActions Vendor => VendorState;

    public void EndErrand()
    {
        Actions.Add($"EndErrand({CurrentErrand})");
        CurrentErrand = Errand.None;
    }

    public bool BeginErrand(Errand errand)
    {
        Actions.Add($"BeginErrand({errand})");

        if (!KnowsWhereErrandsAre)
        {
            return false;
        }

        CurrentErrand = errand;
        return true;
    }

    public IReadOnlyList<VisibleObject> VisibleObjects { get; set; } = [];

    public bool Interact(WoWGuid guid)
    {
        Actions.Add($"Interact({guid})");
        return true;
    }

    // ---- phase 7 ----------------------------------------------------------------------

    public FakeQuestLog QuestLog { get; } = new();

    public IQuestLog Quests => QuestLog;

    /// <summary>What the character is carrying, by item id.</summary>
    public Dictionary<uint, int> Items { get; } = [];

    /// <summary>Items whose use fails, for testing what happens when it does.</summary>
    public HashSet<uint> UnusableItems { get; } = [];

    public int ItemCount(uint itemId) => Items.TryGetValue(itemId, out int count) ? count : 0;

    public bool UseItem(uint itemId)
    {
        Actions.Add($"UseItem({itemId})");
        return ItemCount(itemId) > 0 && !UnusableItems.Contains(itemId);
    }

    /// <summary>The character's group.</summary>
    public FakeParty PartyState { get; } = new();

    public IPartyState Party => PartyState;

    /// <summary>The battleground queue.</summary>
    public FakeBattlegrounds BattlegroundState { get; } = new();

    public IBattlegroundActions Battlegrounds => BattlegroundState;

    /// <summary>Places a creature or object the client can see.</summary>
    public VisibleObject AddVisible(ulong guid, uint entry, float distance = 10f, bool hasPosition = true)
    {
        var visible = new VisibleObject(
            new WoWGuid(guid | ((ulong)WoWGuidType.Creature << 48)),
            entry,
            new Vector3(Position.X + distance, Position.Y, Position.Z),
            hasPosition,
            distance);

        VisibleObjects = [.. VisibleObjects, visible];
        return visible;
    }

    /// <summary>Places a lootable corpse nearby.</summary>
    public CandidateTarget AddCorpse(ulong guid, float distance = 3f)
    {
        var corpse = new CandidateTarget(
            new WoWGuid(guid | ((ulong)WoWGuidType.Creature << 48)),
            new Vector3(Position.X + distance, Position.Y, Position.Z),
            distance, 70, 0d, IsAlive: false, IsInCombat: false, IsTargetingMe: false, 299);

        LootableCorpses = [.. LootableCorpses, corpse];
        return corpse;
    }

    /// <summary>Places a candidate enemy nearby.</summary>
    public CandidateTarget AddEnemy(
        ulong guid,
        float distance = 20f,
        int level = 70,
        bool alive = true,
        bool inCombat = false,
        bool targetingMe = false,
        uint entry = 299,
        double healthPercent = 100d)
    {
        var candidate = new CandidateTarget(
            new WoWGuid(guid | ((ulong)WoWGuidType.Creature << 48)),
            new Vector3(Position.X + distance, Position.Y, Position.Z),
            distance, level, healthPercent, alive, inCombat, targetingMe, entry);

        NearbyEnemies = [.. NearbyEnemies, candidate];
        return candidate;
    }
}
