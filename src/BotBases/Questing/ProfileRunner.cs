using WoWBuddy.Common.Logging;
using WoWBuddy.Profiles;

namespace WoWBuddy.BotBases.Questing;

/// <summary>Which step the bot should be working, and why nothing when nothing.</summary>
/// <param name="Step">The step to work, or null when there is none.</param>
/// <param name="Reason">Why there is none, for logs. Empty when there is a step.</param>
public readonly record struct StepChoice(ProfileStep? Step, string Reason)
{
    /// <summary>True when there is something to do.</summary>
    public bool HasStep => Step is not null;
}

/// <summary>
/// Walks a profile and says which step the character should be working.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a cursor. There is no "step 43 of 200" here: on every tick the runner asks,
/// from the top, which is the first step whose conditions hold and which is not already done.
/// That costs a walk over the list, and buys back everything that goes wrong with a cursor —
/// a quest abandoned by hand, a step completed by a passing player's tagging, a session
/// resumed a day later against a character that moved on. All of those simply resolve to a
/// different answer next tick instead of leaving the bot working a step the world has passed.
/// </para>
/// <para>
/// It is a pure function of the profile and the character's state, so the whole of a profile's
/// logic can be tested by describing a quest log.
/// </para>
/// </remarks>
public sealed class ProfileRunner
{
    private readonly Profile _profile;
    private readonly QuestObjectives _objectives;
    private readonly HashSet<int> _stalledRepeats = [];

    /// <summary>Builds a runner over a profile.</summary>
    /// <param name="profile">The plan to work through.</param>
    /// <param name="objectives">
    /// What each quest actually asks for, from the user's world data. Omit and a step is judged
    /// finished by the quest log alone, which is what happened before there was any export.
    /// </param>
    public ProfileRunner(Profile profile, QuestObjectives? objectives = null)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _objectives = objectives ?? QuestObjectives.None;
    }

    /// <summary>The profile being run.</summary>
    public Profile Profile => _profile;

    /// <summary>Picks the step to work.</summary>
    public StepChoice Choose(IBotState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        ProfileConditionContext conditions = new(state);

        StepChoice choice = Choose(_profile.Steps, state, conditions);

        return choice.HasStep
            ? choice
            : new StepChoice(null, "every step in the profile is done or waiting on a condition");
    }

    /// <summary>True when the profile has nothing left to do.</summary>
    public bool IsFinished(IBotState state) => !Choose(state).HasStep;

    private StepChoice Choose(
        IReadOnlyList<ProfileStep> steps,
        IBotState state,
        IProfileConditionContext conditions)
    {
        foreach (ProfileStep step in steps)
        {
            if (!step.ConditionsHold(conditions))
            {
                continue;
            }

            if (step.Kind == StepKind.Repeat)
            {
                if (_stalledRepeats.Contains(step.LineNumber))
                {
                    continue;
                }

                StepChoice inner = Choose(step.Inner, state, conditions);

                if (inner.HasStep)
                {
                    return inner;
                }

                // The condition still holds but every step inside is done, so going round
                // again would do nothing at all. Say so once and move past it, rather than
                // spinning silently for the rest of the session.
                _stalledRepeats.Add(step.LineNumber);

                Log.For<ProfileRunner>().Warning(
                    "The repeat at line {Line} still applies but everything inside it is done, "
                    + "so the profile is moving past it", step.LineNumber);

                continue;
            }

            if (IsDone(step, state, _objectives))
            {
                continue;
            }

            return new StepChoice(step, string.Empty);
        }

        return new StepChoice(null, string.Empty);
    }

    /// <summary>True when a step no longer needs working.</summary>
    /// <remarks>
    /// Every answer comes from the game rather than from anything the runner remembers, which
    /// is what lets a profile be picked up mid-way on a character that has already done some
    /// of it.
    /// </remarks>
    public static bool IsDone(ProfileStep step, IBotState state, QuestObjectives? objectives = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        IQuestLog quests = state.Quests;

        switch (step.Kind)
        {
            case StepKind.PickUp:
                return quests.IsInLog(step.QuestId) || quests.IsCompleted(step.QuestId);

            case StepKind.TurnIn:
                return quests.IsCompleted(step.QuestId);

            case StepKind.Objective:
                return IsObjectiveDone(step, quests, state, objectives ?? QuestObjectives.None);

            case StepKind.RunTo:
                return state.Position.Distance(step.Position) <= ArrivalRange;

            case StepKind.Grind:
            case StepKind.CustomBehavior:
                // Both run for as long as their conditions hold. A grind with no condition
                // never finishes, which the profile loader warns about when it loads.
                return false;

            default:
                return true;
        }
    }

    /// <summary>How close counts as having arrived somewhere.</summary>
    public const float ArrivalRange = 5f;

    private static bool IsObjectiveDone(
        ProfileStep step,
        IQuestLog quests,
        IBotState state,
        QuestObjectives objectives)
    {
        if (quests.IsCompleted(step.QuestId))
        {
            return true;
        }

        if (quests.Find(step.QuestId) is not { } entry)
        {
            // Not in the log and not handed in: the pick-up step above is what needs to run,
            // and treating this as done is what lets the runner get to it. If there is no
            // pick-up step, the profile is wrong and the loader has already said so.
            return true;
        }

        if (entry.IsComplete)
        {
            return true;
        }

        if (step.ObjectiveIndex >= 1)
        {
            return entry.Objective(step.ObjectiveIndex) is { IsDone: true };
        }

        // A collect objective can also be satisfied by already carrying the items: the quest
        // log only counts them once they are in the bags, and both answers agree, but this one
        // is right a tick sooner and does not depend on the log's wording.
        //
        // The item and the count come from the profile when it gives them, and from the user's
        // world data when it does not — which is the common case for an imported profile.
        if (step.Objective == ObjectiveKind.Collect
            && objectives.CollectionFor(step) is { Entry: > 0, Count: > 0 } collection)
        {
            return state.ItemCount(collection.Entry) >= collection.Count;
        }

        return false;
    }
}
