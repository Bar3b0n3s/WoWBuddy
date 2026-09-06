using WoWBuddy.Behavior;
using WoWBuddy.BotBases.Support;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Objects;

namespace WoWBuddy.BotBases.Battlegrounds;

/// <summary>How the bot plays battlegrounds.</summary>
public sealed record BattlegroundSettings
{
    /// <summary>Which battleground to queue for, as the client names it.</summary>
    public string Queue { get; init; } = string.Empty;

    /// <summary>Where to go once inside.</summary>
    public BattlegroundPlan Plan { get; init; } = new();

    /// <summary>How far to look for someone to fight.</summary>
    public float SearchRange { get; init; } = 40f;

    /// <summary>Leave when the match ends rather than waiting to be thrown out.</summary>
    public bool LeaveWhenFinished { get; init; } = true;

    /// <summary>Re-queue after a match finishes.</summary>
    public bool QueueAgain { get; init; } = true;
}

/// <summary>
/// Queues for a battleground, plays it, and queues again.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope, stated plainly: this fights and moves and does not get thrown out for standing
/// still. It does not play Warsong Gulch well.</b> It has no idea which team holds what, it
/// does not call incoming, it will not decide that the flag carrier needs help, and it cannot
/// tell a defended base from an empty one. Everything it knows about a battleground is a list
/// of places worth being, in order, supplied by the user.
/// </para>
/// <para>
/// That is a deliberate stopping point rather than an unfinished one. Real battleground play is
/// per-battleground logic driven by the score and objective state, and pretending to have it
/// would produce a bot that looks competent for thirty seconds. What is here is honest: a
/// character that turns up, goes to the right places, clicks the flags, fights what it meets,
/// and stops chasing runners across the map.
/// </para>
/// <para>
/// The short leash is the most important thing in it. The classic battleground bot failure is
/// chasing one runner to the other end of the map while the objective it was standing on
/// changes hands behind it.
/// </para>
/// </remarks>
public sealed class BattlegroundBotBase
{
    /// <summary>How close counts as standing on a post.</summary>
    public const float InteractRange = 4f;

    private readonly BattlegroundSettings _settings;
    private int _postIndex;
    private bool _warnedAboutNoPlan;
    private bool _warnedAboutNoQueue;

    public BattlegroundBotBase(BattlegroundSettings? settings = null)
    {
        _settings = settings ?? new BattlegroundSettings();
    }

    /// <summary>How the bot is playing.</summary>
    public BattlegroundSettings Settings => _settings;

    /// <summary>The post the bot is heading for, or null when the plan is empty.</summary>
    public BattlegroundPost? CurrentPost =>
        _settings.Plan.Posts.Count > 0 ? _settings.Plan.Posts[_postIndex] : null;

    /// <summary>Moves on to the next post.</summary>
    public void AdvancePost()
    {
        if (_settings.Plan.Posts.Count == 0)
        {
            return;
        }

        _postIndex = (_postIndex + 1) % _settings.Plan.Posts.Count;
    }

    /// <summary>
    /// Picks someone to fight near the current post.
    /// </summary>
    /// <remarks>
    /// Whoever is already attacking the character first — being hit and not hitting back is how
    /// a bot dies with its target at full health — then the weakest thing within the leash.
    /// Preferring the weakest is right in a battleground in a way it is not while grinding:
    /// kills are shared, so finishing someone else's work is worth more than starting fresh.
    /// </remarks>
    public CandidateTarget? SelectTarget(IBotState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        Vector3 anchor = CurrentPost?.Position ?? state.Position;
        float leash = _settings.Plan.ChaseRange;

        IEnumerable<CandidateTarget> candidates = state.NearbyEnemies
            .Where(enemy => enemy.IsAlive)
            .Where(enemy => enemy.Distance <= _settings.SearchRange)
            .Where(enemy => !_settings.Plan.AvoidEntries.Contains(enemy.Entry))
            .Where(enemy => !_settings.Plan.IsBlacklisted(state.MapId, enemy.Position));

        CandidateTarget[] reachable = [.. candidates.Where(enemy => enemy.Position.Distance(anchor) <= leash)];

        return reachable
            .Where(enemy => enemy.IsTargetingMe)
            .OrderBy(enemy => enemy.Distance)
            .Concat(reachable.OrderBy(enemy => enemy.HealthPercent).ThenBy(enemy => enemy.Distance))
            .Select(enemy => (CandidateTarget?)enemy)
            .FirstOrDefault();
    }

    /// <summary>Builds the subtree the root tree runs when nothing has gone wrong.</summary>
    public Node<IBotState> Build() =>
        new PrioritySelector<IBotState>(
            Engagement.Build(),
            new Do<IBotState>(Act) { Name = "Play the battleground" })
        { Name = "Battleground" };

    private RunStatus Act(IBotState state)
    {
        IBattlegroundActions battlegrounds = state.Battlegrounds;

        if (!battlegrounds.IsInside)
        {
            return Wait(state, battlegrounds);
        }

        if (battlegrounds.IsFinished)
        {
            return Finish(state, battlegrounds);
        }

        if (!battlegrounds.HasStarted)
        {
            // Behind the gates. Walking into them achieves nothing, but standing perfectly
            // still for two minutes is one of the few things a battleground actually watches
            // for, so head for the first post and let the gates stop the character.
            return Travel(state);
        }

        if (SelectTarget(state) is { } target)
        {
            return state.SetTarget(target.Guid) ? RunStatus.Running : RunStatus.Failure;
        }

        return Travel(state);
    }

    private RunStatus Wait(IBotState state, IBattlegroundActions battlegrounds)
    {
        switch (battlegrounds.Status)
        {
            case BattlegroundStatus.Confirmed:
                return battlegrounds.AcceptInvitation() ? RunStatus.Success : RunStatus.Failure;

            case BattlegroundStatus.Queued:
                // Nothing to do but wait. The root tree's other branches still run, so the
                // character can eat, repair and so on while it queues.
                return RunStatus.Failure;

            default:
                if (_settings.Queue.Length == 0)
                {
                    if (!_warnedAboutNoQueue)
                    {
                        _warnedAboutNoQueue = true;
                        Log.For<BattlegroundBotBase>().Warning(
                            "No battleground was named to queue for, so the bot has nothing to "
                            + "do outside one.");
                    }

                    return RunStatus.Failure;
                }

                if (battlegrounds.Queue(_settings.Queue))
                {
                    Log.For<BattlegroundBotBase>().Information(
                        "Queued for {Battleground}", _settings.Queue);
                    return RunStatus.Success;
                }

                return RunStatus.Failure;
        }
    }

    private RunStatus Finish(IBotState state, IBattlegroundActions battlegrounds)
    {
        if (!_settings.LeaveWhenFinished)
        {
            state.StopMoving();
            return RunStatus.Running;
        }

        if (battlegrounds.Leave())
        {
            Log.For<BattlegroundBotBase>().Information(
                "{Battleground} is over; leaving", battlegrounds.Name);

            // Start the next one from the top of the plan rather than wherever this one ended.
            _postIndex = 0;
            return RunStatus.Success;
        }

        return RunStatus.Failure;
    }

    /// <summary>Goes to the current post, clicking whatever is on it.</summary>
    private RunStatus Travel(IBotState state)
    {
        if (CurrentPost is not { } post)
        {
            if (!_warnedAboutNoPlan)
            {
                _warnedAboutNoPlan = true;
                Log.For<BattlegroundBotBase>().Warning(
                    "This battleground plan names no places to go. Battleground layouts are game "
                    + "data this project does not ship, so the plan has to come from a profile.");
            }

            return RunStatus.Failure;
        }

        if (state.Position.Distance(post.Position) > post.Radius)
        {
            if (state.MovementFailed)
            {
                Log.For<BattlegroundBotBase>().Warning(
                    "Could not reach {Post}; trying the next one", post);
                AdvancePost();
                return RunStatus.Failure;
            }

            return state.MoveTo(post.Position) ? RunStatus.Running : RunStatus.Failure;
        }

        // Standing on the post. Flags and banners are the one piece of battleground behaviour
        // that generalises: capturing a base, taking a flag and returning one are all "walk to
        // a thing and click it".
        if (post.HasObjective && Nearest(state, post.Entry) is { } objective)
        {
            if (objective.Distance > InteractRange)
            {
                return state.MoveTo(objective.Position) ? RunStatus.Running : RunStatus.Failure;
            }

            state.StopMoving();
            return state.Interact(objective.Guid) ? RunStatus.Running : RunStatus.Failure;
        }

        // Nothing here to click and nobody to fight: move on rather than stand still, which is
        // both more useful and the thing that keeps the character from being kicked.
        AdvancePost();

        return CurrentPost is { } next && state.MoveTo(next.Position)
            ? RunStatus.Running
            : RunStatus.Failure;
    }

    private static VisibleObject? Nearest(IBotState state, uint entry) =>
        state.VisibleObjects
            .Where(visible => visible.Entry == entry && visible.HasPosition)
            .OrderBy(visible => visible.Distance)
            .Select(visible => (VisibleObject?)visible)
            .FirstOrDefault();
}
