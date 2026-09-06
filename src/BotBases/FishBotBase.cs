using WoWBuddy.Behavior;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Objects;

namespace WoWBuddy.BotBases;

/// <summary>What the bot needs to know to fish.</summary>
public sealed record FishSettings
{
    /// <summary>The fishing spell's name, as the client knows it.</summary>
    public string FishingSpell { get; init; } = "Fishing";

    /// <summary>A lure to apply to the fishing pole, or empty for none.</summary>
    public string LureName { get; init; } = string.Empty;

    /// <summary>How long a lure lasts before it is worth reapplying.</summary>
    public TimeSpan LureDuration { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>How long to wait for a bite before recasting.</summary>
    /// <remarks>
    /// A cast lasts about thirty seconds in this expansion. Waiting much longer means the
    /// bobber has already gone and the bot is watching nothing.
    /// </remarks>
    public TimeSpan BiteTimeout { get; init; } = TimeSpan.FromSeconds(25);

    /// <summary>Places to stand and fish, walked in order.</summary>
    public IReadOnlyList<Vector3> Spots { get; init; } = [];

    /// <summary>How long to fish one spot before moving to the next.</summary>
    public TimeSpan TimePerSpot { get; init; } = TimeSpan.FromMinutes(20);
}

/// <summary>What the fishing base is doing.</summary>
public enum FishingStage
{
    /// <summary>Not fishing.</summary>
    Idle,

    /// <summary>Walking to a spot.</summary>
    Travelling,

    /// <summary>Applying a lure to the pole.</summary>
    ApplyingLure,

    /// <summary>Casting the line.</summary>
    Casting,

    /// <summary>Watching the bobber.</summary>
    Waiting,

    /// <summary>A fish has bitten and the bobber is being clicked.</summary>
    Reeling,
}

/// <summary>
/// Fishes.
/// </summary>
/// <remarks>
/// <para>
/// The whole activity turns on one question: has a fish bitten? The bobber is a game object
/// belonging to the character, and the bite is a change in its state byte — the same
/// descriptor field the object layer already reads, which is one of the protocol-defined ones
/// rather than a guessed offset. That is why fishing is possible at all despite game object
/// positions still being unresolved: the bot never needs to know where the bobber is, only
/// that it has moved.
/// </para>
/// <para>
/// <b>The bite detection needs confirming against a client.</b> That a bobber's state byte
/// changes on a bite is a reasonable reading of how the client models it and has not been
/// watched happening. <c>docs/phase-6-manual-test.md</c> covers how to check it, and the base
/// falls back to recasting on a timer if no bite is ever detected, which fishes slowly rather
/// than not at all.
/// </para>
/// </remarks>
public sealed class FishBotBase
{
    private readonly FishSettings _settings;

    private DateTimeOffset _lureAppliedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _castAt = DateTimeOffset.MinValue;
    private DateTimeOffset _spotSince = DateTimeOffset.MinValue;
    private int _spotIndex;
    private byte? _bobberStateAtCast;

    public FishBotBase(FishSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>What the base is doing.</summary>
    public FishingStage Stage { get; private set; } = FishingStage.Idle;

    /// <summary>The spot being fished.</summary>
    public Vector3? CurrentSpot => _settings.Spots.Count > 0 ? _settings.Spots[_spotIndex] : null;

    /// <summary>Casts made this session.</summary>
    public int Casts { get; private set; }

    /// <summary>Bites noticed this session.</summary>
    public int Bites { get; private set; }

    /// <summary>True when the lure has worn off, or there never was one.</summary>
    public bool NeedsLure(DateTimeOffset now) =>
        !string.IsNullOrEmpty(_settings.LureName) && now - _lureAppliedAt >= _settings.LureDuration;

    /// <summary>Records that a lure has just been applied.</summary>
    public void NoteLureApplied(DateTimeOffset now)
    {
        _lureAppliedAt = now;
        Log.For<FishBotBase>().Debug("Lure applied; good until {Until:HH:mm}", now + _settings.LureDuration);
    }

    /// <summary>
    /// Records a cast and the bobber's state at that moment.
    /// </summary>
    /// <remarks>
    /// The state at rest is recorded rather than assumed, so a bite is detected as a change
    /// from whatever this client happens to use rather than as equality with a value this
    /// project has guessed.
    /// </remarks>
    public void NoteCast(DateTimeOffset now, byte? bobberState)
    {
        _castAt = now;
        _bobberStateAtCast = bobberState;
        Stage = FishingStage.Waiting;
        Casts++;
    }

    /// <summary>True when the bobber's state has changed since the cast.</summary>
    public bool HasBite(byte? bobberState)
    {
        if (_bobberStateAtCast is not { } atCast || bobberState is not { } now)
        {
            return false;
        }

        return now != atCast;
    }

    /// <summary>True when the cast has been out too long and should be abandoned.</summary>
    public bool CastHasExpired(DateTimeOffset now) => now - _castAt >= _settings.BiteTimeout;

    /// <summary>True when this spot has been fished long enough.</summary>
    public bool ShouldMoveOn(DateTimeOffset now) =>
        _settings.Spots.Count > 1 && now - _spotSince >= _settings.TimePerSpot;

    /// <summary>Moves to the next fishing spot.</summary>
    public void AdvanceSpot(DateTimeOffset now)
    {
        if (_settings.Spots.Count == 0)
        {
            return;
        }

        _spotIndex = (_spotIndex + 1) % _settings.Spots.Count;
        _spotSince = now;
        Stage = FishingStage.Travelling;
    }

    /// <summary>Records that a bite was acted on.</summary>
    public void NoteBite()
    {
        Bites++;
        Stage = FishingStage.Reeling;
    }

    /// <summary>
    /// Builds the subtree the root tree runs.
    /// </summary>
    /// <param name="bobberState">
    /// Reads the state byte of the character's bobber, or null when there is no bobber.
    /// </param>
    /// <param name="castLine">Casts the fishing spell.</param>
    /// <param name="applyLure">Applies the configured lure.</param>
    /// <param name="clickBobber">Interacts with the bobber to collect the catch.</param>
    public Node<IBotState> Build(
        Func<IBotState, byte?> bobberState,
        Func<IBotState, bool> castLine,
        Func<IBotState, bool> applyLure,
        Func<IBotState, bool> clickBobber)
    {
        ArgumentNullException.ThrowIfNull(bobberState);
        ArgumentNullException.ThrowIfNull(castLine);
        ArgumentNullException.ThrowIfNull(applyLure);
        ArgumentNullException.ThrowIfNull(clickBobber);

        return new PrioritySelector<IBotState>(
            // A bite is the only thing worth interrupting anything else for: the window is a
            // second or two and missing it wastes the whole cast.
            new If<IBotState>(
                state => Stage == FishingStage.Waiting && HasBite(bobberState(state)),
                new Do<IBotState>(state =>
                {
                    NoteBite();
                    bool clicked = clickBobber(state);
                    Stage = FishingStage.Idle;
                    return clicked ? RunStatus.Success : RunStatus.Failure;
                })
                { Name = "Reel in" })
            { Name = "A fish has bitten" },

            // Nothing bit in time. Recasting beats waiting on a bobber that has gone.
            new If<IBotState>(
                state => Stage == FishingStage.Waiting && CastHasExpired(state.Now),
                new Do<IBotState>(_ =>
                {
                    Stage = FishingStage.Idle;
                    return RunStatus.Success;
                })
                { Name = "Give up on this cast" })
            { Name = "The cast has expired" },

            new If<IBotState>(
                state => Stage == FishingStage.Waiting,
                new Do<IBotState>(_ => RunStatus.Running) { Name = "Watch the bobber" }),

            new If<IBotState>(
                state => ShouldMoveOn(state.Now) || Stage == FishingStage.Travelling,
                new Do<IBotState>(state =>
                {
                    if (CurrentSpot is not { } spot)
                    {
                        return RunStatus.Failure;
                    }

                    if (state.Position.Distance(spot) <= 3f)
                    {
                        state.StopMoving();
                        Stage = FishingStage.Idle;
                        return RunStatus.Success;
                    }

                    return state.MoveTo(spot) ? RunStatus.Running : RunStatus.Failure;
                })
                { Name = "Travel to a spot" })
            { Name = "Time to move" },

            new If<IBotState>(
                state => NeedsLure(state.Now),
                new Do<IBotState>(state =>
                {
                    Stage = FishingStage.ApplyingLure;

                    if (!applyLure(state))
                    {
                        // Out of lures. Fishing without one is slower, not impossible, so the
                        // clock is reset to stop the bot retrying every tick.
                        NoteLureApplied(state.Now);
                        return RunStatus.Failure;
                    }

                    NoteLureApplied(state.Now);
                    Stage = FishingStage.Idle;
                    return RunStatus.Success;
                })
                { Name = "Apply a lure" })
            { Name = "The lure has worn off" },

            new Do<IBotState>(state =>
            {
                Stage = FishingStage.Casting;

                if (!castLine(state))
                {
                    Stage = FishingStage.Idle;
                    return RunStatus.Failure;
                }

                NoteCast(state.Now, bobberState(state));
                return RunStatus.Running;
            })
            { Name = "Cast" })
        { Name = "Fish" };
    }
}
