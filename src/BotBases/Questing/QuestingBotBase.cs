using WoWBuddy.Behavior;
using WoWBuddy.BotBases.Support;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Objects;
using WoWBuddy.Profiles;

namespace WoWBuddy.BotBases.Questing;

/// <summary>
/// Works through a profile: takes quests, does what they ask, hands them in.
/// </summary>
/// <remarks>
/// <para>
/// The base that most needs the rest of the bot to already work. Every tick it asks the
/// <see cref="ProfileRunner"/> which step applies, then runs the small subtree for that kind of
/// step. Nothing is remembered between ticks beyond which quest giver was tried and found
/// empty, which means anything that changes the world — a quest abandoned by hand, an objective
/// finished by a passing player, the session resumed a day later — resolves to a different
/// answer next tick rather than leaving the bot working a step that no longer exists.
/// </para>
/// <para>
/// <b>Running out of quests is normal, not an error.</b> A profile covering levels 1 to 10 stops
/// having anything to do at 10, and a character standing still is worse than a character
/// levelling slowly, so the base falls back to grinding when the profile is finished. Passing no
/// grind settings turns that off, and the base simply idles.
/// </para>
/// </remarks>
public sealed class QuestingBotBase
{
    /// <summary>How close the character must be to interact with something.</summary>
    /// <remarks>
    /// The client's own interaction range is a little under five yards for most things. Four
    /// leaves room for the character drifting as it stops.
    /// </remarks>
    public const float InteractRange = 4f;

    /// <summary>How far to look for the creature or object a step names.</summary>
    public const float SearchRange = 100f;

    private readonly ProfileRunner _runner;
    private readonly Node<IBotState>? _fallback;
    private readonly IReadOnlyDictionary<string, Node<IBotState>> _behaviors;
    private readonly QuestObjectives _objectives;
    private readonly HashSet<string> _reportedMissingBehaviors = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<uint> _describedQuests = [];

    /// <summary>Builds a questing base around a profile.</summary>
    /// <param name="profile">The plan to work through.</param>
    /// <param name="fallback">
    /// What to do when the profile is finished. Usually a grind base built from the profile's
    /// own avoid list; null to stand still instead.
    /// </param>
    /// <param name="behaviors">
    /// Named behaviours a profile's <c>CustomBehavior</c> steps can run. A step naming one that
    /// is not here is reported once and skipped, so an imported profile that mentions behaviours
    /// this bot does not have still runs the rest of its steps.
    /// </param>
    /// <param name="objectives">
    /// What each quest actually asks for, from the user's world data. Omit and objective steps
    /// work only from what the profile names, which is what they did before there was an export
    /// to read: an objective the profile did not describe falls back to killing whatever is
    /// nearby.
    /// </param>
    public QuestingBotBase(
        Profile profile,
        Node<IBotState>? fallback = null,
        IReadOnlyDictionary<string, Node<IBotState>>? behaviors = null,
        QuestObjectives? objectives = null)
    {
        _objectives = objectives ?? QuestObjectives.None;
        _runner = new ProfileRunner(profile, _objectives);
        _fallback = fallback;
        _behaviors = behaviors ?? new Dictionary<string, Node<IBotState>>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The profile being worked.</summary>
    public Profile Profile => _runner.Profile;

    /// <summary>Which step the bot would work right now.</summary>
    public StepChoice Current(IBotState state) => _runner.Choose(state);

    /// <summary>Builds the subtree the root tree runs when nothing has gone wrong.</summary>
    /// <remarks>
    /// One node rather than a selector, so that a step which fails this tick cannot fall
    /// through to the fallback. Otherwise a moment's bad luck — a quest giver briefly out of
    /// sight, a path that failed once — would drop the character into grinding in the middle
    /// of a quest chain. The fallback is for a profile that is genuinely finished, and nothing
    /// else.
    /// </remarks>
    public Node<IBotState> Build() =>
        new Do<IBotState>(state =>
        {
            StepChoice choice = _runner.Choose(state);

            if (choice.Step is { } step)
            {
                return Work(state, step);
            }

            if (_fallback is not null)
            {
                // Debug rather than warning: finishing a profile is what success looks like,
                // not a problem to flag every tick.
                Log.For<QuestingBotBase>().Debug("The profile is finished; grinding instead");
                return _fallback.Tick(state);
            }

            state.StopMoving();
            return RunStatus.Running;
        })
        { Name = "Questing" };

    private RunStatus Work(IBotState state, ProfileStep step) =>
        step.Kind switch
        {
            StepKind.PickUp => PickUp(state, step),
            StepKind.TurnIn => TurnIn(state, step),
            StepKind.Objective => WorkObjective(state, step),
            StepKind.RunTo => Travel(state, step.Position),
            StepKind.Grind => Grind(state, step),
            StepKind.CustomBehavior => RunBehavior(state, step),
            _ => RunStatus.Failure,
        };

    // ---- quest givers ---------------------------------------------------------------------

    private RunStatus PickUp(IBotState state, ProfileStep step)
    {
        if (state.Quests.IsFull())
        {
            // Nothing here can fix a full log, and hammering the giver would not help. Say so
            // and let the tick end; a turn-in later in the profile will make room.
            Log.For<QuestingBotBase>().Warning(
                "The quest log is full, so quest {QuestId} cannot be taken", step.QuestId);
            return RunStatus.Failure;
        }

        if (ApproachGiver(state, step) is { } travelling)
        {
            return travelling;
        }

        state.StopMoving();

        switch (state.Quests.Accept(step.QuestId))
        {
            case QuestGiverResult.Done:
                Log.For<QuestingBotBase>().Information(
                    "Took quest {QuestId} ({QuestName})", step.QuestId, step.QuestName);
                return RunStatus.Success;

            case QuestGiverResult.NotOffered:
                // The giver is talking and does not have it. On 3.3.5a the usual reason is
                // that this character did the quest long before the bot arrived, and no Lua
                // call can say so — see IQuestLog.IsCompleted. Recording it here is what stops
                // the bot walking back to this giver every tick for the rest of the session.
                Log.For<QuestingBotBase>().Information(
                    "Quest {QuestId} was not on offer, so it is being treated as already done",
                    step.QuestId);

                state.Quests.MarkCompleted(step.QuestId);
                return RunStatus.Failure;

            default:
                return RunStatus.Running;
        }
    }

    private RunStatus TurnIn(IBotState state, ProfileStep step)
    {
        if (!state.Quests.IsInLog(step.QuestId))
        {
            // Not in the log and not recorded as done: nothing here can hand it in. Recording
            // it moves the profile on rather than parking the character at an empty NPC.
            Log.For<QuestingBotBase>().Warning(
                "Quest {QuestId} is not in the log, so it cannot be handed in", step.QuestId);

            state.Quests.MarkCompleted(step.QuestId);
            return RunStatus.Failure;
        }

        if (!state.Quests.IsReadyToTurnIn(step.QuestId))
        {
            // Its objectives are not done yet. Failing lets a later step run.
            return RunStatus.Failure;
        }

        if (ApproachGiver(state, step) is { } travelling)
        {
            return travelling;
        }

        state.StopMoving();

        switch (state.Quests.TurnIn(step.QuestId, step.RewardIndex))
        {
            case QuestGiverResult.Done:
                Log.For<QuestingBotBase>().Information(
                    "Handed in quest {QuestId} ({QuestName})", step.QuestId, step.QuestName);

                state.Quests.MarkCompleted(step.QuestId);
                return RunStatus.Success;

            case QuestGiverResult.NotOffered:
                Log.For<QuestingBotBase>().Warning(
                    "Quest {QuestId} cannot be handed in here. Check the profile's turn-in "
                    + "position and creature.", step.QuestId);
                return RunStatus.Failure;

            default:
                return RunStatus.Running;
        }
    }

    /// <summary>
    /// Gets the character to the step's quest giver and opens its window.
    /// </summary>
    /// <returns>
    /// What the caller should return while the character is still getting there, or null once
    /// it is in place and talking.
    /// </returns>
    private RunStatus? ApproachGiver(IBotState state, ProfileStep step)
    {
        if (Nearest(state, step.Entry) is { } giver)
        {
            if (giver.Distance > InteractRange)
            {
                return Travel(state, giver.Position);
            }

            state.StopMoving();

            // Interacting opens the quest window. Doing it every tick is harmless — the
            // client ignores it once the window is up — and it recovers on its own if the
            // window is closed by something else.
            state.Interact(giver.Guid);
            return null;
        }

        // Not in sight, so the profile's position is the best guess available.
        if (step.Position.IsZero)
        {
            Log.For<QuestingBotBase>().Warning(
                "Quest giver {Entry} for quest {QuestId} is not in sight and the profile gives "
                + "no position for it", step.Entry, step.QuestId);

            return RunStatus.Failure;
        }

        return Travel(state, step.Position);
    }

    // ---- objectives -----------------------------------------------------------------------

    private RunStatus WorkObjective(IBotState state, ProfileStep step) => step.Objective switch
    {
        ObjectiveKind.Kill or ObjectiveKind.Collect => Fight(state, step),
        ObjectiveKind.Interact or ObjectiveKind.Gossip => Touch(state, step),
        ObjectiveKind.UseItem => Use(state, step),
        _ => WorkTheArea(state, step),
    };

    /// <summary>
    /// Kills what the objective names, wherever the profile says they are.
    /// </summary>
    /// <remarks>
    /// What counts as "what the objective names" is the profile's entry when it gives one, the
    /// quest's own creature list when the user has a world data export, and anything at all
    /// when neither says. The last of those is the case worth avoiding: a step that kills
    /// whatever walks past finishes by luck, and only if the right thing happens to walk past.
    /// </remarks>
    private RunStatus Fight(IBotState state, ProfileStep step)
    {
        if (state.Target is { IsAlive: true })
        {
            return RunStatus.Running;
        }

        IReadOnlySet<uint> wanted = Wanted(step);

        CandidateTarget? candidate = state.NearbyEnemies
            .Where(enemy => enemy.IsAlive)
            .Where(enemy => wanted.Count == 0 || wanted.Contains(enemy.Entry))
            .Where(enemy => !Profile.AvoidMobs.Contains(enemy.Entry))
            .Where(enemy => !enemy.IsInCombat || enemy.IsTargetingMe)
            .Where(enemy => !Profile.IsBlacklisted(state.MapId, enemy.Position))
            .OrderBy(enemy => enemy.Distance)
            .Select(enemy => (CandidateTarget?)enemy)
            .FirstOrDefault();

        if (candidate is { } target)
        {
            return state.SetTarget(target.Guid) ? RunStatus.Running : RunStatus.Failure;
        }

        return WorkTheArea(state, step);
    }

    /// <summary>
    /// The creature and object entries a step should be working towards.
    /// </summary>
    /// <remarks>
    /// Empty means "anything", which is what an objective with no entry and no world data has
    /// always meant. Said once per quest in the log, because knowing the bot has narrowed a
    /// step down is worth a line and repeating it every tick is not.
    /// </remarks>
    private IReadOnlySet<uint> Wanted(ProfileStep step)
    {
        IReadOnlySet<uint> wanted = _objectives.KillsFor(step);

        if (step.Entry == 0 && wanted.Count > 0 && _describedQuests.Add(step.QuestId))
        {
            Log.For<QuestingBotBase>().Information(
                "Quest {QuestId} ({QuestName}) wants {Objectives}; the profile did not say, so "
                + "the entries come from world data",
                step.QuestId, step.QuestName, _objectives.Describe(step));
        }

        return wanted;
    }

    /// <summary>Walks to what the objective names and interacts with it.</summary>
    private RunStatus Touch(IBotState state, ProfileStep step)
    {
        if (Nearest(state, Wanted(step)) is { } thing)
        {
            if (thing.Distance > InteractRange)
            {
                return Travel(state, thing.Position);
            }

            state.StopMoving();
            return state.Interact(thing.Guid) ? RunStatus.Running : RunStatus.Failure;
        }

        return WorkTheArea(state, step);
    }

    /// <summary>Uses the item the objective names, once in place.</summary>
    private RunStatus Use(IBotState state, ProfileStep step)
    {
        if (step.ItemId == 0)
        {
            Log.For<QuestingBotBase>().Warning(
                "Quest {QuestId} has a UseItem objective with no item. The profile has to name "
                + "one: the item a quest hands out to be used is not in the objectives the "
                + "database exports.", step.QuestId);

            return RunStatus.Failure;
        }

        if (state.ItemCount(step.ItemId) == 0)
        {
            Log.For<QuestingBotBase>().Warning(
                "Quest {QuestId} wants item {ItemId} used, but the character is not carrying it",
                step.QuestId, step.ItemId);

            return RunStatus.Failure;
        }

        // Where the objective names a creature, the item is used on it, so it has to be
        // targeted and in range first.
        if (step.Entry != 0)
        {
            if (Nearest(state, Wanted(step)) is not { } victim)
            {
                return WorkTheArea(state, step);
            }

            if (victim.Distance > InteractRange)
            {
                return Travel(state, victim.Position);
            }

            state.StopMoving();
            state.SetTarget(victim.Guid);
            return state.UseItem(step.ItemId) ? RunStatus.Running : RunStatus.Failure;
        }

        if (!IsInPlace(state, step))
        {
            return TravelToStep(state, step);
        }

        state.StopMoving();
        return state.UseItem(step.ItemId) ? RunStatus.Running : RunStatus.Failure;
    }

    /// <summary>
    /// Moves around the objective's area and lets the quest log decide when it is done.
    /// </summary>
    /// <remarks>
    /// What an objective the profile could not describe falls back to, and what the specific
    /// behaviours fall back to when what they are looking for is not in sight. Doing something
    /// vaguely right in the right place beats standing still, and the quest log is the thing
    /// that actually decides when the step is finished.
    /// </remarks>
    private RunStatus WorkTheArea(IBotState state, ProfileStep step)
    {
        if (!IsInPlace(state, step))
        {
            return TravelToStep(state, step);
        }

        // Already there with nothing in sight: move on to the next hotspot so the character
        // covers the area rather than standing on one spot waiting for a respawn.
        if (step.Spots.Count > 1)
        {
            return Travel(state, NextSpot(state, step));
        }

        return RunStatus.Running;
    }

    // ---- the rest -------------------------------------------------------------------------

    private RunStatus Grind(IBotState state, ProfileStep step)
    {
        GrindSettings settings = new()
        {
            Hotspots = step.Spots.Count > 0 ? step.Spots : [step.Position],
            SearchRadius = step.Radius > 0f ? step.Radius : 60f,
            HotspotRadius = step.Radius > 0f ? step.Radius : 80f,
            AvoidEntries = Profile.AvoidMobs,
            Blackspots = [.. Profile.Blackspots
                .Where(spot => spot.MapId == state.MapId)
                .Select(spot => (spot.Position, spot.Radius))],
        };

        if (state.Target is { IsAlive: true })
        {
            return RunStatus.Running;
        }

        if (new GrindBotBase(settings).SelectTarget(state) is { } candidate)
        {
            return state.SetTarget(candidate.Guid) ? RunStatus.Running : RunStatus.Failure;
        }

        return WorkTheArea(state, step);
    }

    private RunStatus RunBehavior(IBotState state, ProfileStep step)
    {
        if (_behaviors.TryGetValue(step.BehaviorName, out Node<IBotState>? behavior))
        {
            return behavior.Tick(state);
        }

        if (_reportedMissingBehaviors.Add(step.BehaviorName))
        {
            Log.For<QuestingBotBase>().Warning(
                "The profile asks for behaviour '{Name}' at line {Line}, which this bot does not "
                + "have. That step will be skipped; whatever it was for will not happen.",
                step.BehaviorName, step.LineNumber);
        }

        // Failing rather than succeeding: the step is not done, but the branch gives way so
        // the root tree can do something useful with the tick.
        return RunStatus.Failure;
    }

    // ---- moving about ---------------------------------------------------------------------

    private static RunStatus Travel(IBotState state, Vector3 destination)
    {
        if (state.MovementFailed)
        {
            Log.For<QuestingBotBase>().Warning("Could not reach {Destination}", destination);
            return RunStatus.Failure;
        }

        return state.MoveTo(destination) ? RunStatus.Running : RunStatus.Failure;
    }

    private static RunStatus TravelToStep(IBotState state, ProfileStep step)
    {
        Vector3 destination = step.Spots.Count > 0 ? NextSpot(state, step) : step.Position;

        return destination.IsZero ? RunStatus.Failure : Travel(state, destination);
    }

    /// <summary>True when the character is close enough to work the step where it stands.</summary>
    private static bool IsInPlace(IBotState state, ProfileStep step)
    {
        float radius = step.Radius > 0f ? step.Radius : ProfileRunner.ArrivalRange;

        foreach (Vector3 spot in step.AllPositions())
        {
            if (state.Position.Distance(spot) <= radius)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The hotspot to head for next.
    /// </summary>
    /// <remarks>
    /// The nearest one the character is not already standing at. That keeps the character
    /// moving around the area instead of oscillating between the two closest points, without
    /// needing an index to remember between ticks.
    /// </remarks>
    private static Vector3 NextSpot(IBotState state, ProfileStep step)
    {
        float radius = step.Radius > 0f ? step.Radius : ProfileRunner.ArrivalRange;

        Vector3 best = step.Position;
        float bestDistance = float.MaxValue;

        foreach (Vector3 spot in step.Spots)
        {
            float distance = state.Position.Distance(spot);

            if (distance <= radius)
            {
                continue;
            }

            if (distance < bestDistance)
            {
                best = spot;
                bestDistance = distance;
            }
        }

        // Standing inside every hotspot: stay where the step points.
        return bestDistance == float.MaxValue ? step.Spots[0] : best;
    }

    private static VisibleObject? Nearest(IBotState state, uint entry) =>
        entry == 0 ? null : Nearest(state, new HashSet<uint> { entry });

    private static VisibleObject? Nearest(IBotState state, IReadOnlySet<uint> entries)
    {
        // Empty means the bot does not know what it is looking for, which is not the same as
        // looking for anything: interacting with the nearest object of any kind would open a
        // mailbox, or a forge, or a quest giver two zones' worth of chain quests deep.
        if (entries.Count == 0)
        {
            return null;
        }

        return state.VisibleObjects
            .Where(visible => entries.Contains(visible.Entry) && visible.HasPosition)
            .Where(visible => visible.Distance <= SearchRange)
            .OrderBy(visible => visible.Distance)
            .Select(visible => (VisibleObject?)visible)
            .FirstOrDefault();
    }
}
