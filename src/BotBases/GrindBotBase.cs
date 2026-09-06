using WoWBuddy.Behavior;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;

namespace WoWBuddy.BotBases;

/// <summary>What the grind base is allowed to kill, and where.</summary>
/// <remarks>
/// Deliberately a plain settings object rather than something read from a profile file:
/// profiles arrive in phase 7, and until then the same values are what a profile will supply.
/// </remarks>
public sealed class GrindSettings
{
    /// <summary>Points the bot patrols between looking for things to kill.</summary>
    public IReadOnlyList<Vector3> Hotspots { get; init; } = [];

    /// <summary>Lowest level worth killing.</summary>
    public int MinimumLevel { get; init; } = 1;

    /// <summary>Highest level considered safe to pull.</summary>
    public int MaximumLevel { get; init; } = 80;

    /// <summary>How far from the character to look for targets.</summary>
    public float SearchRadius { get; init; } = 60f;

    /// <summary>How far from a hotspot the bot may wander before returning to it.</summary>
    public float HotspotRadius { get; init; } = 80f;

    /// <summary>Creature template ids never to attack.</summary>
    /// <remarks>
    /// The escape hatch for the things that kill unattended bots: elites on a patrol route,
    /// quest mobs that summon friends, anything that turned out to be a mistake at three in
    /// the morning.
    /// </remarks>
    public IReadOnlySet<uint> AvoidEntries { get; init; } = new HashSet<uint>();

    /// <summary>Places never to walk into, as a centre and a radius.</summary>
    public IReadOnlyList<(Vector3 Centre, float Radius)> Blackspots { get; init; } = [];

    /// <summary>True when a position is inside a blackspot.</summary>
    public bool IsBlackspotted(Vector3 position) =>
        Blackspots.Any(spot => position.Distance(spot.Centre) <= spot.Radius);

    /// <summary>True when a candidate is one this profile is willing to fight.</summary>
    public bool IsAcceptable(CandidateTarget target) =>
        target.IsAlive
        && target.Level >= MinimumLevel
        && target.Level <= MaximumLevel
        && !AvoidEntries.Contains(target.Entry)
        && !IsBlackspotted(target.Position);
}

/// <summary>
/// Kills things in an area, then kills more things.
/// </summary>
/// <remarks>
/// <para>
/// The simplest bot base and the one that proves the rest of the machinery works: it needs
/// targeting, movement, a combat routine, resting and death handling all cooperating, and
/// nothing else. If grinding runs unattended for an hour, the foundations are sound.
/// </para>
/// <para>
/// It contributes a subtree rather than a loop, so everything above it in the root tree can
/// pre-empt it at any point: a pull that is interrupted by the character dying simply never
/// resumes, because the tree is re-evaluated rather than continued.
/// </para>
/// </remarks>
public sealed class GrindBotBase
{
    private readonly GrindSettings _settings;
    private int _hotspotIndex;

    public GrindBotBase(GrindSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>The hotspot the bot is currently working.</summary>
    public Vector3? CurrentHotspot =>
        _settings.Hotspots.Count > 0 ? _settings.Hotspots[_hotspotIndex] : null;

    /// <summary>
    /// Picks the best thing to kill from what is nearby.
    /// </summary>
    /// <remarks>
    /// Nearest acceptable candidate that nothing else is already fighting. Preferring the
    /// nearest keeps the character inside its hotspot instead of being drawn across the zone;
    /// skipping mobs already in combat avoids stealing a fight from a real player, which is
    /// both rude and how a bot gets reported.
    /// </remarks>
    public CandidateTarget? SelectTarget(IBotState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.NearbyEnemies
            .Where(candidate => candidate.Distance <= _settings.SearchRadius)
            .Where(_settings.IsAcceptable)
            .Where(candidate => !candidate.IsInCombat || candidate.IsTargetingMe)
            .OrderBy(candidate => candidate.Distance)
            .Select(candidate => (CandidateTarget?)candidate)
            .FirstOrDefault();
    }

    /// <summary>Moves on to the next hotspot in the rotation.</summary>
    public void AdvanceHotspot()
    {
        if (_settings.Hotspots.Count == 0)
        {
            return;
        }

        _hotspotIndex = (_hotspotIndex + 1) % _settings.Hotspots.Count;
        Log.For<GrindBotBase>().Debug("Moving to hotspot {Index}", _hotspotIndex);
    }

    /// <summary>Builds the subtree the root tree runs when nothing has gone wrong.</summary>
    public Node<IBotState> Build() =>
        new PrioritySelector<IBotState>(
            // Something already selected and alive: close on it and start the fight. The
            // routine's own pull range decides where "close enough" is, which is what stops a
            // mage walking into melee for a fight it should open at thirty yards.
            new If<IBotState>(
                s => s.Target is { IsAlive: true },
                new PrioritySelector<IBotState>(
                    new If<IBotState>(
                        s => s.Target!.Value.Distance > s.Routine.PullRange,
                        new Do<IBotState>(s =>
                            s.MoveTo(s.Target!.Value.Position) ? RunStatus.Running : RunStatus.Failure)
                        { Name = "Approach" }),

                    new Do<IBotState>(s =>
                    {
                        s.StopMoving();
                        s.Routine.PetControl(s.Combat);
                        s.Routine.Pull(s.Combat);
                        return RunStatus.Running;
                    })
                    { Name = "Pull" })
                { Name = "Engage" })
            { Name = "Has a target" },

            // Nothing selected: pick something.
            new Do<IBotState>(state =>
            {
                if (SelectTarget(state) is not { } candidate)
                {
                    return RunStatus.Failure;
                }

                return state.SetTarget(candidate.Guid) ? RunStatus.Success : RunStatus.Failure;
            })
            { Name = "Select a target" },

            // Nothing worth killing here: go to the hotspot, or on to the next one.
            new If<IBotState>(
                _ => _settings.Hotspots.Count > 0,
                new Do<IBotState>(state =>
                {
                    Vector3 hotspot = CurrentHotspot!.Value;

                    // Standing at a hotspot with nothing to kill means it is exhausted for
                    // now; moving on beats waiting for a respawn.
                    if (state.Position.Distance(hotspot) <= _settings.HotspotRadius)
                    {
                        AdvanceHotspot();
                        hotspot = CurrentHotspot!.Value;
                    }

                    if (state.MovementFailed)
                    {
                        Log.For<GrindBotBase>().Warning(
                            "Could not reach hotspot {Hotspot}; trying the next one", hotspot);
                        AdvanceHotspot();
                        return RunStatus.Failure;
                    }

                    return state.MoveTo(hotspot) ? RunStatus.Running : RunStatus.Failure;
                })
                { Name = "Travel to hotspot" })
            { Name = "Has hotspots" })
        { Name = "Grind" };
}
