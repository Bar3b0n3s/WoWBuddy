using WoWBuddy.Behavior;
using WoWBuddy.Common.Geometry;
using WoWBuddy.BotBases.Support;
using WoWBuddy.Common.Logging;
using WoWBuddy.Common.Scheduling;

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

    /// <summary>How close the character has to be to open a corpse's loot.</summary>
    public const float LootRange = 4f;

    /// <summary>
    /// Builds the root tree around a bot base.
    /// </summary>
    /// <param name="botBase">The plan for what to do when nothing has gone wrong.</param>
    /// <param name="errands">
    /// Decides when to break off for a vendor, repair or trainer. Omit to disable errands.
    /// </param>
    public static BehaviorTree<IBotState> Build(Node<IBotState> botBase, ErrandPlanner? errands = null)
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

                // A break means stop playing, not stop existing. It sits below death and
                // combat deliberately: a character that spends a ten-minute break lying dead
                // resumes to a corpse run it could have done already, and one that stands
                // still while something eats it is not taking a break, it is dying. Below
                // this line is everything a person on a break would not be doing: looting,
                // eating, running errands, looking for the next fight.
                new If<IBotState>(
                    s => s.Session == SessionState.OnBreak,
                    new Do<IBotState>(s =>
                    {
                        s.StopMoving();
                        return RunStatus.Running;
                    })
                    { Name = "Take a break" })
                { Name = "On a break" },

                // Looting comes after combat and before resting: the corpse can be looted
                // while the character is still hurt, and waiting until after a two-minute
                // drink risks the corpse expiring.
                HandleLooting(),

                HandleRest(),
                HandleErrands(errands),

                new If<IBotState>(_ => true, botBase) { Name = "Bot base" },

                new Do<IBotState>(_ => RunStatus.Running) { Name = "Idle" })
            {
                Name = "Root",
            });
    }

    /// <summary>
    /// Emptying corpses the character has killed.
    /// </summary>
    /// <remarks>
    /// Below combat so that being attacked interrupts looting, and above resting because a
    /// corpse expires on a timer whereas the character's health does not.
    /// </remarks>
    public static Node<IBotState> HandleLooting() =>
        new If<IBotState>(
            s => !s.IsInCombat && (s.IsLooting || s.LootableCorpses.Count > 0),
            new PrioritySelector<IBotState>(
                new If<IBotState>(
                    s => s.IsLooting,
                    new Do<IBotState>(_ => RunStatus.Running) { Name = "Finish looting" }),

                new Do<IBotState>(s =>
                {
                    CandidateTarget corpse = s.LootableCorpses[0];

                    // Walk to it first: looting has a range and the corpse is wherever the
                    // fight ended, which is rarely where the character is standing.
                    if (corpse.Distance > LootRange)
                    {
                        return s.MoveTo(corpse.Position) ? RunStatus.Running : RunStatus.Failure;
                    }

                    s.StopMoving();
                    return s.Loot(corpse.Guid) ? RunStatus.Running : RunStatus.Failure;
                })
                { Name = "Loot the nearest corpse" })
            { Name = "Looting" })
        { Name = "Handle looting" };

    /// <summary>
    /// Breaking off to visit a vendor, a repair NPC, a mailbox or a trainer.
    /// </summary>
    /// <remarks>
    /// Below resting because an errand is a several-minute trip and a character that leaves
    /// at low health arrives dead. Above the bot base because carrying on grinding with full
    /// bags and broken gear accomplishes nothing.
    /// </remarks>
    public static Node<IBotState> HandleErrands(ErrandPlanner? errands)
    {
        if (errands is null)
        {
            return new Check<IBotState>(_ => false) { Name = "Errands disabled" };
        }

        return new If<IBotState>(
            s => !s.IsInCombat
                 && (s.CurrentErrand != Errand.None
                     || errands.Next(s.Inventory, s.Level, hasSellableItems: true) != Errand.None),
            new Do<IBotState>(s =>
            {
                if (s.CurrentErrand != Errand.None)
                {
                    return RunStatus.Running;
                }

                Errand due = errands.Next(s.Inventory, s.Level, hasSellableItems: true);

                // The bot may simply not know where to go, which is the normal state until a
                // profile supplies vendor locations. Failing hands control back to the bot
                // base rather than stalling.
                return s.BeginErrand(due) ? RunStatus.Running : RunStatus.Failure;
            })
            { Name = "Run an errand" })
        { Name = "Handle errands" };
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
