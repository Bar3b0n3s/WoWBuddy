using WoWBuddy.BotBases;
using WoWBuddy.CombatRoutines;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Objects;
using WoWBuddy.Live;
using WoWBuddy.Navigation;

namespace WoWBuddy.Live.Tests;

/// <summary>The verified half of the bot's picture of the world, set by hand.</summary>
internal sealed class FakeCharacterView : ICharacterView
{
    public bool IsInWorld { get; set; } = true;

    public bool IsDead { get; set; }

    public bool IsGhost { get; set; }

    public bool IsInCombat { get; set; }

    public Vector3 Position { get; set; } = new(1500f, -2500f, 60f);

    public int MapId { get; set; }

    public double HealthPercent { get; set; } = 100d;

    public int Level { get; set; } = 20;

    public CandidateTarget? Target { get; set; }

    public IReadOnlyList<CandidateTarget> NearbyEnemies { get; set; } = [];

    public IReadOnlyList<CandidateTarget> LootableCorpses { get; set; } = [];

    public IReadOnlyList<CandidateTarget> SkinnableCorpses { get; set; } = [];

    public IReadOnlyList<VisibleObject> VisibleObjects { get; set; } = [];

    public Vector3? CorpsePosition { get; set; }

    public bool IsLooting { get; set; }

    /// <summary>What the bot asked the client to do, in order.</summary>
    public List<string> Actions { get; } = [];

    /// <summary>Where things are, by GUID.</summary>
    public Dictionary<ulong, Vector3> Positions { get; } = [];

    public Vector3? Locate(WoWGuid guid) =>
        Positions.TryGetValue(guid.Value, out Vector3 position) ? position : null;

    public bool SetTarget(WoWGuid guid)
    {
        Actions.Add($"SetTarget({guid})");
        return true;
    }

    public bool Interact(WoWGuid guid)
    {
        Actions.Add($"Interact({guid})");
        return true;
    }

    public bool Loot(WoWGuid guid)
    {
        Actions.Add($"Loot({guid})");
        return true;
    }

    public bool ReleaseCorpse()
    {
        Actions.Add("ReleaseCorpse");
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
}

/// <summary>Navigation that answers in straight lines.</summary>
internal sealed class FakeNavigation : INavigationService
{
    /// <summary>Whether a path can be found at all.</summary>
    public bool CanFindPaths { get; set; } = true;

    /// <summary>How many paths were asked for.</summary>
    public int Requests { get; private set; }

    public bool IsMapLoaded(int mapId) => CanFindPaths;

    public NavigationPath FindPath(int mapId, Vector3 start, Vector3 end)
    {
        Requests++;

        return CanFindPaths
            ? new NavigationPath([start, end], PathFailure.None)
            : NavigationPath.Failed(PathFailure.NoDataForMap);
    }

    public Vector3? FindNearestWalkable(int mapId, Vector3 position) =>
        CanFindPaths ? position : null;
}

/// <summary>
/// A client's memory, enough of it for the click-to-move block.
/// </summary>
/// <remarks>
/// Local rather than shared with the navigation tests: it is twenty lines, and a test project
/// reaching into another one for a fake this small is a coupling that costs more than it saves.
/// </remarks>
internal sealed class FakeClientMemory : IProcessMemory
{
    private readonly Dictionary<nint, byte> _bytes = [];

    public bool IsValid { get; set; } = true;

    public nint ModuleBase => 0x00400000;

    public int ModuleSize => 0x00800000;

    public bool TryReadBytes(nint address, Span<byte> buffer)
    {
        if (!IsValid)
        {
            return false;
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = _bytes.TryGetValue(address + i, out byte value) ? value : (byte)0;
        }

        return true;
    }

    public bool TryWriteBytes(nint address, ReadOnlySpan<byte> buffer)
    {
        if (!IsValid)
        {
            return false;
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            _bytes[address + i] = buffer[i];
        }

        return true;
    }

    public nint Allocate(int size, bool executable) => 0x10000000;

    public bool Free(nint address) => true;

    public bool WithWritableMemory(nint address, int size, Action action)
    {
        action();
        return true;
    }
}

/// <summary>
/// A combat context that does nothing.
/// </summary>
/// <remarks>
/// <see cref="LiveBotState"/> passes the context through to the routine and never looks inside
/// it, so these tests need one to exist and nothing more. The real thing is exercised in the
/// combat routine tests.
/// </remarks>
internal sealed class StubCombat : ICombatContext
{
    public UnitSnapshot Me { get; } = new(
        new WoWGuid(0x1), 100d, 100d, 100, 20, IsAlive: true, 0f, WoWGuid.Zero);

    public UnitSnapshot? Target => null;

    public UnitSnapshot? Pet => null;

    public int EnemiesInMelee => 0;

    public bool IsCasting => false;

    public bool IsSpellReady(string spellName) => false;

    public bool HasAura(UnitSnapshot unit, string auraName) => false;

    public bool Cast(string spellName, bool onSelf = false) => false;
}
