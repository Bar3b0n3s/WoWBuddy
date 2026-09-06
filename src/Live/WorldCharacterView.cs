using WoWBuddy.BotBases;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Execution;
using WoWBuddy.Core.Objects;
using WoWBuddy.GameApi;
using WoWBuddy.GameApi.Enums;
using WoWBuddy.GameApi.Objects;

namespace WoWBuddy.Live;

/// <summary>
/// Answers <see cref="ICharacterView"/> from a running client.
/// </summary>
/// <remarks>
/// <para>
/// The last piece, and deliberately the thinnest. Everything here is either a read through the
/// verified offset table or a call through the execution layer, and there is no judgement in it
/// beyond turning the game's objects into the plain values the bot bases were written against.
/// All the deciding happens above, in <see cref="LiveBotState"/> and the tree, where it can be
/// tested.
/// </para>
/// <para>
/// <b>Hostility is the one thing this cannot do properly.</b> That judgement is the only
/// guesswork in the file, so it lives apart in <see cref="HostilityRule"/> where it can be read
/// and tested on its own, and a user who has faction data can supply a better one.
/// </para>
/// </remarks>
public sealed class WorldCharacterView : ICharacterView
{
    /// <summary>How far around the character to look for things.</summary>
    /// <remarks>
    /// The client keeps objects in the manager well beyond what a player can see, so a limit is
    /// needed or every tick walks a list of hundreds. A hundred yards is comfortably past
    /// anything the bot acts on.
    /// </remarks>
    public const float SearchRange = 100f;

    private readonly World _world;
    private readonly NativeFunctions _natives;
    private readonly ILuaEvaluator _lua;
    private readonly Func<WoWUnit, bool> _isHostile;
    private readonly bool _canSkin;

    /// <summary>Builds a view over an attached client.</summary>
    /// <param name="world">The typed object model.</param>
    /// <param name="natives">The client's own functions, for targeting.</param>
    /// <param name="lua">The client's scripting, for the verbs that have no verified address.</param>
    /// <param name="isHostile">
    /// Decides whether a unit is worth attacking. Injected so that a user who has faction data
    /// can supply something better than the approximation described on this class.
    /// </param>
    /// <param name="canSkin">
    /// Whether the character has skinning. A setting rather than a reading: the bot would
    /// otherwise have to interpret the spellbook, and a character that walks to every corpse
    /// and fails to skin it is worse than one that never tries.
    /// </param>
    public WorldCharacterView(
        World world,
        NativeFunctions natives,
        ILuaEvaluator lua,
        Func<WoWUnit, bool>? isHostile = null,
        bool canSkin = false)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _natives = natives ?? throw new ArgumentNullException(nameof(natives));
        _lua = lua ?? throw new ArgumentNullException(nameof(lua));
        _isHostile = isHostile ?? CouldBeHostile;
        _canSkin = canSkin;
    }

    /// <inheritdoc />
    public bool IsInWorld => _world.IsInWorld && _world.Me is not null;

    /// <inheritdoc />
    public bool IsDead => _world.Me is { } me && me.Health == 0;

    /// <inheritdoc />
    /// <remarks>
    /// A ghost is alive with the ghost aura, which this project has no verified way to read, so
    /// the client is asked instead. Getting it wrong in the safe direction matters: a character
    /// wrongly believed to be a ghost would try to run to a corpse it does not have.
    /// </remarks>
    public bool IsGhost => _lua.EvaluateBool("UnitIsGhost(\"player\") and true or false");

    /// <inheritdoc />
    public bool IsInCombat => _world.Me is { IsInCombat: true };

    /// <summary>True when the character is on a mount.</summary>
    /// <remarks>
    /// From the unit flags, which are verified — a better answer than asking the scripting for
    /// something the object manager already knows.
    /// </remarks>
    public bool IsMounted => _world.Me is { IsMounted: true };

    /// <inheritdoc />
    public Vector3 Position => _world.Me?.Position ?? Vector3.Zero;

    /// <inheritdoc />
    public int MapId => _world.Me?.MapId ?? 0;

    /// <inheritdoc />
    public double HealthPercent => _world.Me?.HealthPercent ?? 0d;

    /// <inheritdoc />
    public int Level => _world.Me?.Level ?? 0;

    /// <inheritdoc />
    public CandidateTarget? Target =>
        _world.Target is { } target ? Describe(target) : null;

    /// <inheritdoc />
    public IReadOnlyList<CandidateTarget> NearbyEnemies =>
        [.. _world.UnitsNear(SearchRange)
            .Where(unit => unit.IsAlive)
            .Where(_isHostile)
            .Select(Describe)];

    /// <inheritdoc />
    /// <remarks>
    /// Only what the character is entitled to. A corpse tapped by somebody else holds loot that
    /// is not the bot's, and trying to take it produces an error message per attempt and looks
    /// exactly like a bot.
    /// </remarks>
    public IReadOnlyList<CandidateTarget> LootableCorpses =>
        [.. _world.UnitsNear(SearchRange)
            .Where(unit => unit.IsDead)
            .Where(unit => unit.DynamicFlags.HasFlag(UnitDynamicFlags.Lootable))
            .Where(unit => !unit.DynamicFlags.HasFlag(UnitDynamicFlags.Tapped)
                || unit.DynamicFlags.HasFlag(UnitDynamicFlags.TappedByMe))
            .Select(Describe)];

    /// <inheritdoc />
    /// <remarks>
    /// The skinnable flag is set on a corpse once its loot has been taken, so this list and
    /// <see cref="LootableCorpses"/> never overlap — which is what lets the tree work them in
    /// turn rather than walking to the same corpse twice.
    /// </remarks>
    public IReadOnlyList<CandidateTarget> SkinnableCorpses =>
        _canSkin
            ?
            [
                .. _world.UnitsNear(SearchRange)
                    .Where(unit => unit.IsDead)
                    .Where(unit => unit.Flags.HasFlag(UnitFlags.Skinnable))
                    .Select(Describe),
            ]
            : [];

    /// <inheritdoc />
    public IReadOnlyList<VisibleObject> VisibleObjects
    {
        get
        {
            Vector3 me = Position;

            return
            [
                .. _world.GameObjects
                    .Select(gameObject => new
                    {
                        Object = gameObject,
                        Distance = gameObject.HasKnownPosition
                            ? gameObject.Position.Distance(me)
                            : float.PositiveInfinity,
                    })
                    .Where(entry => entry.Distance <= SearchRange)
                    .OrderBy(entry => entry.Distance)
                    .Select(entry => new VisibleObject(
                        entry.Object.Guid,
                        entry.Object.Entry,
                        entry.Object.Position,
                        entry.Object.HasKnownPosition,
                        entry.Distance)),
            ];
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The character's own corpse, found by matching the owner GUID. Somebody else's corpse in
    /// the same graveyard is not the one to run to.
    /// </remarks>
    public Vector3? CorpsePosition
    {
        get
        {
            if (_world.Me is not { } me)
            {
                return null;
            }

            foreach (WoWCorpse corpse in _world.Corpses)
            {
                if (corpse.OwnerGuid == me.Guid && WorldBounds.IsPlausible(corpse.Position))
                {
                    return corpse.Position;
                }
            }

            return null;
        }
    }

    /// <inheritdoc />
    public bool IsLooting => (_lua.EvaluateInt("GetNumLootItems()") ?? 0) > 0;

    /// <inheritdoc />
    public Vector3? Locate(WoWGuid guid)
    {
        if (_world.FindByGuid(guid) is not { } found)
        {
            return null;
        }

        Vector3 position = found.Position;

        return WorldBounds.IsPlausible(position) ? position : null;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Through the client's own function rather than by writing the target GUID into memory.
    /// The function does the rest of what targeting entails — the UI, the events an addon
    /// would see — and a written GUID leaves the client believing something else.
    /// </remarks>
    public bool SetTarget(WoWGuid guid) => _natives.Target(guid);

    /// <inheritdoc />
    /// <remarks>
    /// Targets first, then interacts, because the scripting has no call that takes a GUID:
    /// <c>InteractUnit</c> works on a unit id, and "target" is the only one the bot can put
    /// anything into.
    /// </remarks>
    public bool Interact(WoWGuid guid)
    {
        if (!SetTarget(guid))
        {
            return false;
        }

        return _lua.Execute("InteractUnit(\"target\")");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Interacting with a corpse opens the loot window; <c>LootSlot</c> takes what is in it.
    /// Looting every slot in one script rather than one call per slot keeps a full corpse to a
    /// single round trip, and <c>ConfirmLootSlot</c> answers the confirmation a bind-on-pickup
    /// item raises, without which the window sticks and nothing else can be looted.
    /// </remarks>
    public bool Loot(WoWGuid guid)
    {
        if (!Interact(guid))
        {
            return false;
        }

        return _lua.Execute(
            "for slot = GetNumLootItems(), 1, -1 do "
            + "LootSlot(slot) ConfirmLootSlot(slot) end");
    }

    /// <inheritdoc />
    public bool ReleaseCorpse()
    {
        Log.For<WorldCharacterView>().Information("Releasing to the graveyard");
        return _lua.Execute("RepopMe()");
    }

    /// <inheritdoc />
    public bool RetrieveCorpse()
    {
        Log.For<WorldCharacterView>().Information("Taking the corpse back");
        return _lua.Execute("RetrieveCorpse()");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Sitting is what makes food and drink work, so it is worth doing explicitly rather than
    /// relying on the client to do it when an item is used.
    /// </remarks>
    public bool StartResting() => _lua.Execute("SitStandOrDescendStart()");

    /// <summary>Applies <see cref="HostilityRule"/> to a unit read from the client.</summary>
    private static bool CouldBeHostile(WoWUnit unit) =>
        HostilityRule.CouldBeHostile(unit is WoWPlayer, unit.NpcFlags, unit.Flags);

    private CandidateTarget Describe(WoWUnit unit)
    {
        Vector3 me = Position;
        WoWGuid myGuid = _world.Me?.Guid ?? WoWGuid.Zero;

        return new CandidateTarget(
            unit.Guid,
            unit.Position,
            unit.Position.Distance(me),
            unit.Level,
            unit.HealthPercent,
            unit.IsAlive,
            unit.IsInCombat,
            unit.TargetGuid == myGuid,
            unit.Guid.Entry);
    }
}
