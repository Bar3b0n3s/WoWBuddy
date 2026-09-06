using WoWBuddy.Behavior;
using WoWBuddy.Common.Geometry;
using WoWBuddy.BotBases.Group;
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
    /// <param name="errandHandler">
    /// Carries an errand out: walks to the vendor, sells, repairs, posts the mail. Omit and
    /// errands are never started, because beginning one nothing can finish would stop the bot.
    /// </param>
    /// <param name="travel">
    /// When to get on a mount. Omit and the character walks everywhere, which is slower and
    /// never wrong.
    /// </param>
    /// <param name="talents">
    /// The order to spend talent points in. Omit and points are left unspent, which is the
    /// right answer: spending them in the wrong order costs gold to undo.
    /// </param>
    public static BehaviorTree<IBotState> Build(
        Node<IBotState> botBase,
        ErrandPlanner? errands = null,
        Node<IBotState>? errandHandler = null,
        TalentBuild? talents = null,
        TravelSettings? travel = null)
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
                HandleGroupSupport(),

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
                HandleSkinning(),
                HandleTalents(talents),
                HandleMounting(travel),

                HandleRest(),
                HandleErrands(errands, errandHandler),

                new If<IBotState>(_ => true, botBase) { Name = "Bot base" },

                new Do<IBotState>(_ => RunStatus.Running) { Name = "Idle" })
            {
                Name = "Root",
            });
    }


    /// <summary>
    /// Letting the routine act while the group fights and the character has not been drawn in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A healer stood behind the tank is not in combat and has nothing targeted, so
    /// <see cref="HandleCombat"/> passes it over entirely — and the tank dies while the bot
    /// considers what to do next. This branch exists for exactly that gap.
    /// </para>
    /// <para>
    /// <b>It reports failure even when the routine acted.</b> That is deliberate rather than a
    /// mistake: returning success or running here would swallow the tick, and a damage
    /// character would then never reach the bot base that picks it a target to assist with.
    /// Failing lets the routine take its chance to heal and the rest of the tree carry on.
    /// </para>
    /// </remarks>
    public static Node<IBotState> HandleGroupSupport() =>
        new If<IBotState>(
            s => !s.IsInCombat && s.Target is not { IsAlive: true } && s.Party.AnyoneInCombat(),
            new Do<IBotState>(s =>
            {
                s.Routine.Combat(s.Combat);
                return RunStatus.Failure;
            })
            { Name = "Support the group" })
        { Name = "Group is fighting" };

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
    public static Node<IBotState> HandleErrands(
        ErrandPlanner? errands,
        Node<IBotState>? errandHandler = null)
    {
        // Errands need both halves: something to decide one is due, and something to carry it
        // out. With either missing there is nothing to run, and beginning an errand that
        // nothing can finish would leave the character standing still indefinitely.
        if (errands is null || errandHandler is null)
        {
            return new Check<IBotState>(_ => false) { Name = "Errands disabled" };
        }

        return new If<IBotState>(
            s => !s.IsInCombat && Due(s, errands) != Errand.None,
            new Do<IBotState>(s =>
            {
                if (s.CurrentErrand == Errand.None)
                {
                    Errand due = Due(s, errands);

                    // The bot may simply not know where to go, which is the normal state until
                    // a profile supplies vendor locations. Failing hands control back to the
                    // bot base rather than stalling.
                    if (!s.BeginErrand(due))
                    {
                        return RunStatus.Failure;
                    }
                }

                Errand ran = s.CurrentErrand;
                RunStatus status = errandHandler.Tick(s);

                if (status == RunStatus.Running)
                {
                    return RunStatus.Running;
                }

                // Done, or given up on. Either way the errand is over: an errand that stayed
                // current after its handler stopped working on it would hold the bot forever.
                s.EndErrand();

                if (ran == Errand.Train)
                {
                    // Recorded whether it worked or not, and that is the point. The planner
                    // decides a training trip is due from the character's level alone, so an
                    // errand that ends without this is decided again on the very next tick —
                    // and the bot walks to the trainer for the rest of the session instead of
                    // playing. A trainer that could not be reached is worth one attempt per
                    // few levels, not one per quarter second.
                    errands.NoteTrained(s.Level);
                }

                // Failure rather than success, so the bot base gets this tick instead of the
                // character standing still until the next one.
                return RunStatus.Failure;
            })
            { Name = "Run an errand" })
        { Name = "Handle errands" };
    }

    /// <summary>The errand in progress, or the one now due.</summary>
    private static Errand Due(IBotState state, ErrandPlanner errands) =>
        state.CurrentErrand != Errand.None
            ? state.CurrentErrand
            : errands.Next(state.Inventory, state.Level, state.HasSellableItems);

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
    /// Getting on a mount before a long walk, and off again before doing anything else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Above the bot base so the mount goes on before the journey starts, and below combat and
    /// looting so it never delays either. A mount cast is interrupted by damage and cancelled
    /// by moving, so trying at the wrong moment means starting casts that never finish and
    /// arriving later than if the character had walked.
    /// </para>
    /// <para>
    /// Dismounting is the other half and matters more than it looks: a mounted character cannot
    /// loot, gather, skin or attack, so a bot that forgets to get off simply stops working.
    /// </para>
    /// </remarks>
    public static Node<IBotState> HandleMounting(TravelSettings? travel)
    {
        if (travel is not { Enabled: true })
        {
            return new Check<IBotState>(_ => false) { Name = "Mounting off" };
        }

        return new PrioritySelector<IBotState>(
            // Off first. Everything the bot does apart from travelling needs the character on
            // its own feet, and being mounted at the wrong moment is a silent stop.
            new If<IBotState>(
                s => s.Travel.IsMounted
                     && (s.IsInCombat
                         || s.LootableCorpses.Count > 0
                         || s.SkinnableCorpses.Count > 0
                         || s.RemainingDistance < travel.WorthMountingFor),
                new Do<IBotState>(s => s.Travel.Dismount() ? RunStatus.Success : RunStatus.Failure)
                { Name = "Get off" }),

            new If<IBotState>(
                s => !s.Travel.IsMounted
                     && !s.IsInCombat
                     && s.Travel.CanMount
                     && s.RemainingDistance >= travel.WorthMountingFor,
                new Do<IBotState>(s => s.Travel.Mount() ? RunStatus.Running : RunStatus.Failure)
                { Name = "Get on" }))
        { Name = "Handle mounting" };
    }

    /// <summary>
    /// Spending a talent point when one is going spare.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Out of combat only, and one point per tick: the client refuses a talent whose
    /// prerequisites are not met, and taking them one at a time means the next tick sees the
    /// tree as it is now rather than as the build assumed it would be.
    /// </para>
    /// <para>
    /// <b>Nothing is spent without a build.</b> Points sitting unspent cost nothing; points
    /// spent in the wrong order cost gold to undo, and this project has no talent data with
    /// which to work out a right order.
    /// </para>
    /// </remarks>
    public static Node<IBotState> HandleTalents(TalentBuild? build)
    {
        if (build is not { IsUsable: true })
        {
            return new Check<IBotState>(_ => false) { Name = "No talent build" };
        }

        return new If<IBotState>(
            s => !s.IsInCombat && s.Talents.UnspentPoints > 0,
            new Do<IBotState>(s =>
            {
                // The build lists picks in order, and the character has already taken as many
                // as it has levels for. Which one is next follows from how many are left.
                int spent = build.Points - s.Talents.UnspentPoints;

                if (spent < 0 || spent >= build.Points)
                {
                    // More points than the build accounts for. Leaving them is right: the
                    // build has run out and nothing here knows what should come next.
                    return RunStatus.Failure;
                }

                TalentPick pick = build.Picks[spent];

                if (!s.Talents.Learn(pick))
                {
                    // Refused, almost always because a prerequisite further up the tree is not
                    // met yet — which means the build is wrong, and repeating it every tick
                    // would fill the log without ever succeeding.
                    Log.For<IBotState>().Warning(
                        "The client refused talent {Pick}. The build is probably in the wrong "
                        + "order; the remaining points have been left unspent.", pick);

                    return RunStatus.Failure;
                }

                Log.For<IBotState>().Information("Spent a talent point on {Pick}", pick);
                return RunStatus.Success;
            })
            { Name = "Spend a talent point" })
        { Name = "Handle talents" };
    }

    /// <summary>How close the character has to be to skin something.</summary>
    public const float SkinRange = 4f;

    /// <summary>
    /// Taking the skin off something already looted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Below looting rather than beside it, because a corpse only becomes skinnable once its
    /// loot has been taken: doing it the other way round means walking to the same corpse
    /// twice.
    /// </para>
    /// <para>
    /// Above resting, because a skin is on a timer and the character's health is not — the same
    /// reasoning that puts looting there. Below combat, because nothing is worth being eaten
    /// over.
    /// </para>
    /// </remarks>
    public static Node<IBotState> HandleSkinning() =>
        new If<IBotState>(
            s => !s.IsInCombat && s.SkinnableCorpses.Count > 0,
            new Do<IBotState>(s =>
            {
                CandidateTarget corpse = s.SkinnableCorpses[0];

                if (corpse.Distance > SkinRange)
                {
                    if (s.MovementFailed)
                    {
                        // Under the world, on a ledge, the other side of a fence. Leaving it
                        // is better than walking into the same rock until the corpse decays.
                        return RunStatus.Failure;
                    }

                    return s.MoveTo(corpse.Position) ? RunStatus.Running : RunStatus.Failure;
                }

                s.StopMoving();

                // Skinning is an interaction like any other: the client works out that the
                // character has a knife and the corpse has a skin.
                return s.Interact(corpse.Guid) ? RunStatus.Running : RunStatus.Failure;
            })
            { Name = "Skin a corpse" })
        { Name = "Handle skinning" };

    /// <summary>
    /// Recovering out of combat, and keeping buffs up while it happens.
    /// </summary>
    /// <remarks>
    /// Below combat because resting during a fight is not resting, and above the bot base
    /// because a character that pulls at half health dies at a predictable rate.
    /// </remarks>
    public static Node<IBotState> HandleRest() =>
        new If<IBotState>(
            // Not while anyone in the group is fighting. Sitting down to drink as the tank
            // pulls is the group-play equivalent of resting mid-fight, and the character's own
            // combat flag does not catch it: a healer stood behind the tank is not yet in
            // combat when the pull happens.
            s => !s.IsInCombat && !s.Party.AnyoneInCombat() && !s.Routine.IsReadyToFight(s.Combat),
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
