using WoWBuddy.Common.Geometry;
using WoWBuddy.Profiles;

namespace WoWBuddy.BotBases.Support;

/// <summary>Which window a trip is waiting for.</summary>
/// <remarks>
/// They are different windows with different calls behind them, and waiting for the wrong one
/// means waiting forever at an NPC that opened perfectly. The Train errand did exactly that: it
/// walked to a trainer and then watched for a merchant.
/// </remarks>
public enum VendorWindow
{
    /// <summary>A merchant, for selling, repairing and buying.</summary>
    Merchant,

    /// <summary>A mailbox.</summary>
    Mailbox,

    /// <summary>A trainer.</summary>
    Trainer,
}

/// <summary>How far a trip to a vendor has got.</summary>
public enum ApproachResult
{
    /// <summary>Still walking, or still waiting for the window.</summary>
    Travelling,

    /// <summary>Standing at the vendor with its window open.</summary>
    Open,

    /// <summary>Gave up. <see cref="VendorApproach.Failure"/> says why.</summary>
    Failed,
}

/// <summary>
/// Getting the character to a vendor and its window open.
/// </summary>
/// <remarks>
/// <para>
/// The fiddly half of every trip to town, and the same fiddliness whether the errand is selling
/// junk or buying flux, so it lives in one place: walk there, stop, click, and give up if the
/// window never appears.
/// </para>
/// <para>
/// <b>Giving up matters more than arriving.</b> The character can be standing in exactly the
/// right spot with no window at all — the vendor wandered off, the click missed, something
/// interrupted the interaction — and without a timeout the bot clicks at nothing for the rest
/// of the session. That is the failure this class exists to prevent.
/// </para>
/// </remarks>
public sealed class VendorApproach
{
    private readonly float _interactRange;
    private readonly TimeSpan _windowTimeout;
    private readonly Func<DateTimeOffset> _clock;

    private ProfileVendor? _waitingFor;
    private DateTimeOffset _waitingSince = DateTimeOffset.MinValue;

    /// <summary>Builds an approach.</summary>
    /// <param name="interactRange">How close counts as standing at the vendor.</param>
    /// <param name="windowTimeout">How long to wait for a window before giving up.</param>
    /// <param name="clock">The current time, injected so the timeout can be tested.</param>
    public VendorApproach(
        float interactRange = 4f,
        TimeSpan? windowTimeout = null,
        Func<DateTimeOffset>? clock = null)
    {
        _interactRange = interactRange;
        _windowTimeout = windowTimeout ?? TimeSpan.FromSeconds(10);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Why the last attempt was given up on, or empty.</summary>
    public string Failure { get; private set; } = string.Empty;

    /// <summary>Forgets how long it has been waiting.</summary>
    public void Reset()
    {
        _waitingFor = null;
        Failure = string.Empty;
    }

    /// <summary>
    /// Takes one step towards having the vendor's window open.
    /// </summary>
    /// <param name="state">What the bot can see and do.</param>
    /// <param name="destination">Where to go and what to click.</param>
    /// <param name="window">Which window counts as having arrived.</param>
    public ApproachResult Step(
        IBotState state,
        ProfileVendor destination,
        VendorWindow window = VendorWindow.Merchant)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(destination);

        float distance = state.Position.Distance(destination.Position);

        if (distance > _interactRange)
        {
            return state.MoveTo(destination.Position) && !state.MovementFailed
                ? ApproachResult.Travelling
                : Fail($"Could not reach {destination.Name}");
        }

        state.StopMoving();

        bool open = window switch
        {
            VendorWindow.Mailbox => state.Vendor.IsMailboxOpen,
            VendorWindow.Trainer => state.Trainer.IsTrainerOpen,
            _ => state.Vendor.IsMerchantOpen,
        };

        if (open)
        {
            _waitingFor = null;
            return ApproachResult.Open;
        }

        DateTimeOffset now = _clock();

        if (_waitingFor != destination)
        {
            _waitingFor = destination;
            _waitingSince = now;
        }
        else if (now - _waitingSince > _windowTimeout)
        {
            return Fail($"{destination.Name} did not open a window");
        }

        VisibleObject? target = null;

        foreach (VisibleObject visible in state.VisibleObjects)
        {
            if (visible.Entry == destination.Entry && visible.HasPosition)
            {
                target = visible;
                break;
            }
        }

        // A mailbox is a game object and appears in the visible list; a vendor is a creature and
        // may not, in which case interacting by the profile's own entry is not possible and the
        // bot has to give up rather than click at the ground.
        if (target is not { } found)
        {
            return now - _waitingSince > _windowTimeout
                ? Fail($"Could not see {destination.Name}")
                : ApproachResult.Travelling;
        }

        // Clicking every tick is harmless — the client ignores it once the window is up — and
        // it recovers on its own if something else closes the window.
        state.Interact(found.Guid);
        return ApproachResult.Travelling;
    }

    private ApproachResult Fail(string reason)
    {
        Failure = reason;
        _waitingFor = null;
        return ApproachResult.Failed;
    }
}
