using WoWBuddy.Behavior;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;

namespace WoWBuddy.BotBases;

/// <summary>
/// The tree the bot ticks, whatever it is doing.
/// </summary>
/// <remarks>
/// <para>
/// One priority selector, and the order of its branches is the whole of the bot's judgement.
/// Being dead beats being stuck; being stuck beats fighting; fighting beats resting; resting
/// beats whatever the bot base wanted to do next. Every branch below the one that runs is
/// simply not considered this tick, and every branch above it gets the chance to take over on
/// the next.
/// </para>
/// <para>
/// The bot base is deliberately near the bottom. A questing or grinding plan is the least
/// urgent thing the bot does: it is what happens when nothing has gone wrong.
/// </para>
/// </remarks>
public static class RootTree
{
    /// <summary>How long to wait for the corpse to be reclaimable before releasing again.</summary>
    public static readonly TimeSpan CorpseRunTimeout = TimeSpan.FromMinutes(5);

    /// <summary>How close to the corpse the character has to be to reclaim it.</summary>
    public const float CorpseReclaimRange = 25f;

    /// <summary>
    /// Builds the root tree around a bot base.
    /// </summary>
    /// <param name="botBase">The plan for what to do when nothing has gone wrong.</param>
    public static BehaviorTree<IBotState> Build(Node<IBotState> botBase)
    {
        ArgumentNullException.ThrowIfNull(botBase);

        return new BehaviorTree<IBotState>(
            new PrioritySelector<IBotState>(
                // Nothing works while the world is not loaded, and reading the object manager
                // during a loading screen produces nonsense.
                new If<IBotState>(s => !s.IsInWorld, new Do<IBotState>(_ => RunStatus.Running)
                {
                    Name = "Wait for the world",
                }),

                HandleDeath(),
                HandleCombat(),
                HandleRest(),

                new If<IBotState>(_ => true, botBase) { Name = "Bot base" },

                new Do<IBotState>(_ => RunStatus.Running) { Name = "Idle" })
            {
                Name = "Root",
            });
    }

    /// <summary>
    /// Dying, releasing, walking back and reclaiming the body.
    /// </summary>
    /// <remarks>
    /// The highest priority because nothing else can work while it applies, and because an
    /// unattended bot that dies and does not recover is finished for the night.
    /// </remarks>
    public static Node<IBotState> HandleDeath() =>
        new If<IBotState>(
            s => s.IsDead,
            new PrioritySelector<IBotState>(
                // Dead but not yet a ghost: release.
                new If<IBotState>(
                    s => !s.IsGhost,
                    new Do<IBotState>(s =>
                    {
                        Log.For(nameof(RootTree)).Information("Died; releasing spirit");
                        return s.ReleaseCorpse() ? RunStatus.Running : RunStatus.Failure;
                    })
                    { Name = "Release" }),

                // A ghost standing on its corpse: take the body back.
                new If<IBotState>(
                    s => s.CorpsePosition is { } corpse
                         && s.Position.Distance(corpse) <= CorpseReclaimRange,
                    new Do<IBotState>(s => s.RetrieveCorpse() ? RunStatus.Running : RunStatus.Failure)
                    { Name = "Retrieve corpse" }),

                // A ghost somewhere else: walk to the corpse.
                new If<IBotState>(
                    s => s.CorpsePosition is not null,
                    new Do<IBotState>(s =>
                        s.MoveTo(s.CorpsePosition!.Value) ? RunStatus.Running : RunStatus.Failure)
                    { Name = "Corpse run" }),

                // A ghost with no known corpse. Reporting Running rather than Failure keeps
                // the branch in control: falling through to the bot base would have a ghost
                // trying to grind.
                new Do<IBotState>(_ => RunStatus.Running) { Name = "Wait for a corpse position" })
            { Name = "Death" })
        { Name = "Handle death" };

    /// <summary>
    /// Fighting: whatever is attacking first, then whatever the bot base picked.
    /// </summary>
    public static Node<IBotState> HandleCombat() =>
        new If<IBotState>(
            s => s.IsInCombat || s.Target is { IsAlive: true },
            new PrioritySelector<IBotState>(
                // Something is attacking that the character is not fighting back at. This is
                // above everything else in combat: ignoring an attacker to keep hitting a
                // different mob is how a bot dies with its target at 90 per cent.
                new If<IBotState>(
                    s => s.Target is not { IsAlive: true }
                         && s.NearbyEnemies.Any(e => e.IsTargetingMe && e.IsAlive),
                    new Do<IBotState>(s =>
                    {
                        CandidateTarget attacker = s.NearbyEnemies.First(e => e.IsTargetingMe && e.IsAlive);
                        return s.SetTarget(attacker.Guid) ? RunStatus.Success : RunStatus.Failure;
                    })
                    { Name = "Target whatever is attacking" }),

                new If<IBotState>(
                    s => s.Target is { IsAlive: true },
                    new Do<IBotState>(s =>
                    {
                        s.Routine.PetControl(s.Combat);
                        s.Routine.Combat(s.Combat);

                        // Running rather than Success: the fight is not over, and reporting
                        // success would let the bot base start something else mid-fight.
                        return RunStatus.Running;
                    })
                    { Name = "Fight" }),

                // In combat with nothing alive to hit. Letting this fail hands control to the
                // branches below, which is right: the fight is effectively over.
                new Do<IBotState>(_ => RunStatus.Failure) { Name = "Nothing to fight" })
            { Name = "Combat" })
        { Name = "Handle combat" };

    /// <summary>
    /// Recovering out of combat, and keeping buffs up while it happens.
    /// </summary>
    /// <remarks>
    /// Below combat because resting during a fight is not resting, and above the bot base
    /// because a character that pulls at half health dies at a predictable rate.
    /// </remarks>
    public static Node<IBotState> HandleRest() =>
        new If<IBotState>(
            s => !s.IsInCombat && !s.Routine.IsReadyToFight(s.Combat),
            new Sequence<IBotState>(
                new Do<IBotState>(s =>
                {
                    s.StopMoving();
                    return RunStatus.Success;
                })
                { Name = "Stand still" },

                new Optional<IBotState>(
                    new Do<IBotState>(s => s.Routine.Buff(s.Combat) ? RunStatus.Success : RunStatus.Failure)
                    { Name = "Buff" }),

                new Do<IBotState>(s =>
                {
                    bool stillResting = s.Routine.Rest(s.Combat);

                    if (stillResting)
                    {
                        s.StartResting();
                    }

                    return stillResting ? RunStatus.Running : RunStatus.Success;
                })
                { Name = "Recover" })
            { Name = "Rest" })
        { Name = "Handle rest" };
}
