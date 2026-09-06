using WoWBuddy.Behavior;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;
using WoWBuddy.Profiles;

namespace WoWBuddy.BotBases.Support;

/// <summary>Where to go and what to do when the bot breaks off to run an errand.</summary>
public sealed record ErrandHandlerSettings
{
    /// <summary>Vendors, repairers and mailboxes the bot knows about.</summary>
    /// <remarks>
    /// Supplied by a profile. Nothing ships: where a vendor stands is game data, and this
    /// project does not invent it.
    /// </remarks>
    public IReadOnlyList<ProfileVendor> Vendors { get; init; } = [];

    /// <summary>What to keep and what to sell.</summary>
    public LootSettings Loot { get; init; } = new();

    /// <summary>Who to post keepable items to.</summary>
    public string MailRecipient { get; init; } = string.Empty;

    /// <summary>How close counts as standing at the vendor.</summary>
    public float InteractRange { get; init; } = 4f;

    /// <summary>How many attachments one letter holds.</summary>
    /// <remarks>Twelve in this expansion, and the client silently ignores the thirteenth.</remarks>
    public const int AttachmentsPerLetter = 12;

    /// <summary>
    /// Finds a trainer for the character's class, given a map, a position and a class id.
    /// </summary>
    /// <remarks>
    /// Supplied from world data, because a profile has no way to say where a warrior trainer
    /// stands and the trainer flag alone does not distinguish one from a profession trainer or
    /// a mount vendor. Without it the Train errand reports that it cannot run.
    /// </remarks>
    public Func<int, Vector3, int, ProfileVendor?>? FindTrainer { get; init; }

    /// <summary>The character's class, for finding the right trainer.</summary>
    public int CharacterClass { get; init; }

    /// <summary>How long to wait for a window to open before giving up on the errand.</summary>
    /// <remarks>
    /// The character can be standing at the right spot and still not have a window: the vendor
    /// walked off, something interrupted the interaction, the click missed. Giving up returns
    /// the bot to what it was doing rather than leaving it clicking at nothing.
    /// </remarks>
    public TimeSpan WindowTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Walking to a vendor and doing the errand the planner asked for.
/// </summary>
/// <remarks>
/// <para>
/// The other half of the errand system. <see cref="ErrandPlanner"/> decides one is due; this
/// carries it out, and <see cref="RootTree"/> will not begin an errand unless this exists —
/// beginning something nothing can finish is how the bot used to stand still for a night.
/// </para>
/// <para>
/// Every path through it ends. It succeeds when the business is done, and fails when there is
/// nowhere to go, nothing to sell, no mail recipient, or the window never opened. Both outcomes
/// clear the errand and hand the tick back, because an errand that cannot be finished is worse
/// than one never started.
/// </para>
/// </remarks>
public sealed class ErrandHandler
{
    private readonly ErrandHandlerSettings _settings;
    private readonly LootRules _loot;
    private readonly VendorApproach _approach;

    private Vector3 _lastPosition;

    public ErrandHandler(ErrandHandlerSettings? settings = null, Func<DateTimeOffset>? clock = null)
    {
        _settings = settings ?? new ErrandHandlerSettings();
        _loot = new LootRules(_settings.Loot);
        _approach = new VendorApproach(_settings.InteractRange, _settings.WindowTimeout, clock);
    }

    /// <summary>Where it would go for an errand, or null when it knows nowhere.</summary>
    public ProfileVendor? Destination(Errand errand, int mapId) => errand switch
    {
        Errand.Repair => Nearest(mapId, vendor => vendor is { IsMailbox: false, CanRepair: true }),
        Errand.Sell => Nearest(mapId, vendor => !vendor.IsMailbox),
        Errand.Mail => Nearest(mapId, vendor => vendor.IsMailbox),

        // A profile cannot say where a warrior trainer stands, so this comes from world data
        // when there is any. Without it the errand is reported as impossible rather than
        // approximated with the nearest thing wearing a trainer flag.
        Errand.Train => _settings.FindTrainer?.Invoke(mapId, _lastPosition, _settings.CharacterClass),

        _ => null,
    };

    /// <summary>Builds the subtree the root tree runs while an errand is in progress.</summary>
    public Node<IBotState> Build() =>
        new Do<IBotState>(Run) { Name = "Run the errand" };

    private RunStatus Run(IBotState state)
    {
        Errand errand = state.CurrentErrand;

        // Kept so Destination can be asked without a state, which is what makes it testable and
        // what lets the window show where the bot would go before it sets off.
        _lastPosition = state.Position;

        if (errand == Errand.None)
        {
            return RunStatus.Success;
        }

        if (Destination(errand, state.MapId) is not { } destination)
        {
            Log.For<ErrandHandler>().Warning(
                "Nowhere to go for {Errand} on map {Map}. Give the profile a vendor, or turn "
                + "this errand off.", errand, state.MapId);

            return Abandon();
        }

        switch (_approach.Step(state, destination, wantMailbox: errand == Errand.Mail))
        {
            case ApproachResult.Travelling:
                return RunStatus.Running;

            case ApproachResult.Failed:
                return Abandon(_approach.Failure);
        }

        IVendorActions vendor = state.Vendor;

        return errand switch
        {
            Errand.Repair => DoRepair(vendor),
            Errand.Sell => DoSell(vendor),
            Errand.Mail => DoMail(vendor),
            _ => Abandon(),
        };
    }

    private RunStatus DoRepair(IVendorActions vendor)
    {
        if (!vendor.CanRepairHere)
        {
            return Abandon("That vendor cannot repair");
        }

        if (!vendor.Repair())
        {
            // Almost always not enough money, which is not something waiting will fix.
            return Abandon("Could not afford the repair");
        }

        Log.For<ErrandHandler>().Information("Repaired");

        vendor.Close();
        return Finish();
    }

    private RunStatus DoSell(IVendorActions vendor)
    {
        IReadOnlyList<BagSlot> forSale = _loot.SelectForSale(vendor.BagContents);

        if (forSale.Count == 0)
        {
            // Nothing to sell is a finished errand rather than a failed one: the bags may have
            // been emptied by something else since the planner decided.
            vendor.Close();
            return Finish();
        }

        int sold = 0;

        foreach (BagSlot slot in forSale)
        {
            if (vendor.Sell(slot))
            {
                sold++;
            }
        }

        Log.For<ErrandHandler>().Information("Sold {Count} stack(s)", sold);

        vendor.Close();
        return Finish();
    }

    private RunStatus DoMail(IVendorActions vendor)
    {
        if (_settings.MailRecipient.Length == 0)
        {
            return Abandon("No mail recipient is configured");
        }

        // Everything worth keeping that is not worth selling. Posting what a vendor would take
        // anyway wastes postage and a trip.
        IReadOnlyList<BagSlot> keepable =
        [
            .. vendor.BagContents
                .Where(slot => _loot.Decide(slot.Item, freeSlots: int.MaxValue) == LootAction.Keep)
                .Where(slot => !slot.Item.IsQuestItem)
                .Take(ErrandHandlerSettings.AttachmentsPerLetter),
        ];

        if (keepable.Count == 0)
        {
            vendor.Close();
            return Finish();
        }

        if (!vendor.Mail(_settings.MailRecipient, keepable))
        {
            return Abandon("The letter could not be sent");
        }

        Log.For<ErrandHandler>().Information(
            "Posted {Count} item(s) to {Recipient}", keepable.Count, _settings.MailRecipient);

        vendor.Close();
        return Finish();
    }

    private RunStatus Finish()
    {
        _approach.Reset();
        return RunStatus.Success;
    }

    private RunStatus Abandon(string? reason = null)
    {
        if (reason is not null)
        {
            Log.For<ErrandHandler>().Warning("Giving up on the errand: {Reason}", reason);
        }

        _approach.Reset();
        return RunStatus.Failure;
    }

    private ProfileVendor? Nearest(int mapId, Func<ProfileVendor, bool> matches)
    {
        ProfileVendor? best = null;

        foreach (ProfileVendor vendor in _settings.Vendors)
        {
            if (vendor.MapId != mapId || !matches(vendor))
            {
                continue;
            }

            best ??= vendor;
        }

        return best;
    }
}
