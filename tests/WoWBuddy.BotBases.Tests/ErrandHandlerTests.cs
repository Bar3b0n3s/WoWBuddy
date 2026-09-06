using WoWBuddy.Behavior;
using WoWBuddy.BotBases;
using WoWBuddy.BotBases.Support;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Profiles;
using Xunit;

namespace WoWBuddy.BotBases.Tests;

public sealed class ErrandHandlerTests
{
    private static readonly Vector3 Town = new(1600f, -2400f, 60f);

    private const uint MerchantEntry = 5000;
    private const uint MailboxEntry = 142075;

    private static ErrandHandlerSettings Settings(
        bool repairer = true,
        bool mailbox = true,
        string recipient = "Bank") => new()
    {
        Vendors =
        [
            new ProfileVendor("Innkeeper", MerchantEntry, 0, Town, CanRepair: repairer),
            .. mailbox
                ? new[] { new ProfileVendor("Mailbox", MailboxEntry, 0, Town, IsMailbox: true) }
                : [],
        ],
        MailRecipient = recipient,
    };

    private static FakeBotState AtTheVendor(Errand errand, uint entry = MerchantEntry)
    {
        FakeBotState state = new() { Position = Town, CurrentErrand = errand };
        state.AddVisible(1, entry, distance: 2f);
        return state;
    }

    [Fact]
    public void WalksToTheVendorWhenItIsNotThereYet()
    {
        FakeBotState state = new() { Position = new Vector3(1000f, -2000f, 60f), CurrentErrand = Errand.Sell };

        Assert.Equal(RunStatus.Running, new ErrandHandler(Settings()).Build().Tick(state));
        Assert.Contains(Town, state.MoveRequests);
    }

    [Fact]
    public void GivesUpWhenItKnowsNowhereToGo()
    {
        // Failing rather than standing there: the profile has no vendor, and no amount of
        // waiting will produce one.
        FakeBotState state = new() { CurrentErrand = Errand.Sell };

        Assert.Equal(
            RunStatus.Failure,
            new ErrandHandler(new ErrandHandlerSettings()).Build().Tick(state));
    }

    [Fact]
    public void GivesUpWhenTheVendorCannotBeReached()
    {
        FakeBotState state = new()
        {
            Position = new Vector3(1000f, -2000f, 60f),
            CurrentErrand = Errand.Sell,
            MovementFailed = true,
        };

        Assert.Equal(RunStatus.Failure, new ErrandHandler(Settings()).Build().Tick(state));
    }

    [Fact]
    public void ClicksTheVendorOnceItIsStandingThere()
    {
        FakeBotState state = AtTheVendor(Errand.Sell);

        Assert.Equal(RunStatus.Running, new ErrandHandler(Settings()).Build().Tick(state));
        Assert.Contains(state.Actions, a => a.StartsWith("Interact", StringComparison.Ordinal));
        Assert.Contains("StopMoving", state.Actions);
    }

    [Fact]
    public void GivesUpWhenTheWindowNeverOpens()
    {
        // The vendor wandered off, the click missed, something interrupted it. Waiting forever
        // would leave the bot clicking at nothing for the rest of the session.
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        ErrandHandler handler = new(Settings(), () => now);

        FakeBotState state = AtTheVendor(Errand.Sell);

        Assert.Equal(RunStatus.Running, handler.Build().Tick(state));

        now += TimeSpan.FromSeconds(11);

        Assert.Equal(RunStatus.Failure, handler.Build().Tick(state));
    }

    [Fact]
    public void RepairsWhenTheWindowIsOpen()
    {
        FakeBotState state = AtTheVendor(Errand.Repair);
        state.VendorState.IsMerchantOpen = true;

        Assert.Equal(RunStatus.Success, new ErrandHandler(Settings()).Build().Tick(state));
        Assert.Contains("Repair", state.VendorState.Actions);
        Assert.Contains("Close", state.VendorState.Actions);
    }

    [Fact]
    public void GivesUpWhenTheCharacterCannotAffordTheRepair()
    {
        // Not something waiting will fix.
        FakeBotState state = AtTheVendor(Errand.Repair);
        state.VendorState.IsMerchantOpen = true;
        state.VendorState.CanAffordRepair = false;

        Assert.Equal(RunStatus.Failure, new ErrandHandler(Settings()).Build().Tick(state));
    }

    [Fact]
    public void GivesUpWhenTheVendorTurnsOutNotToRepair()
    {
        FakeBotState state = AtTheVendor(Errand.Repair);
        state.VendorState.IsMerchantOpen = true;
        state.VendorState.CanRepairHere = false;

        Assert.Equal(RunStatus.Failure, new ErrandHandler(Settings()).Build().Tick(state));
    }

    [Fact]
    public void SellsTheJunkAndKeepsTheRest()
    {
        FakeBotState state = AtTheVendor(Errand.Sell);
        state.VendorState.IsMerchantOpen = true;
        state.VendorState
            .With("Broken Fang", ItemQuality.Poor, sellPrice: 12)
            .With("Sturdy Sword", ItemQuality.Uncommon, sellPrice: 400)
            .With("Worthless Rock", ItemQuality.Poor, sellPrice: 0);

        Assert.Equal(RunStatus.Success, new ErrandHandler(Settings()).Build().Tick(state));

        Assert.Contains("Sell(Broken Fang)", state.VendorState.Actions);
        Assert.DoesNotContain("Sell(Sturdy Sword)", state.VendorState.Actions);

        // Nothing a vendor will not pay for.
        Assert.DoesNotContain("Sell(Worthless Rock)", state.VendorState.Actions);
    }

    [Fact]
    public void NothingToSellIsAFinishedErrandRatherThanAFailedOne()
    {
        // The bags may have been emptied by something else since the planner decided.
        FakeBotState state = AtTheVendor(Errand.Sell);
        state.VendorState.IsMerchantOpen = true;

        Assert.Equal(RunStatus.Success, new ErrandHandler(Settings()).Build().Tick(state));
        Assert.Contains("Close", state.VendorState.Actions);
    }

    [Fact]
    public void PostsKeepableItemsToTheConfiguredCharacter()
    {
        FakeBotState state = AtTheVendor(Errand.Mail, MailboxEntry);
        state.VendorState.IsMailboxOpen = true;
        state.VendorState
            .With("Sturdy Sword", ItemQuality.Uncommon, sellPrice: 400)
            .With("Broken Fang", ItemQuality.Poor, sellPrice: 12);

        Assert.Equal(RunStatus.Success, new ErrandHandler(Settings()).Build().Tick(state));

        // One letter, and only the thing worth keeping: posting what a vendor would take anyway
        // wastes the postage and the trip.
        Assert.Contains("Mail(Bank, 1)", state.VendorState.Actions);
    }

    [Fact]
    public void ALetterHoldsTwelveAttachmentsAndNoMore()
    {
        FakeBotState state = AtTheVendor(Errand.Mail, MailboxEntry);
        state.VendorState.IsMailboxOpen = true;

        for (int index = 0; index < 20; index++)
        {
            state.VendorState.With($"Sword {index}", ItemQuality.Uncommon, sellPrice: 400);
        }

        new ErrandHandler(Settings()).Build().Tick(state);

        Assert.Contains("Mail(Bank, 12)", state.VendorState.Actions);
    }

    [Fact]
    public void GivesUpOnMailWithNoRecipient()
    {
        FakeBotState state = AtTheVendor(Errand.Mail, MailboxEntry);
        state.VendorState.IsMailboxOpen = true;
        state.VendorState.With("Sturdy Sword", ItemQuality.Uncommon, sellPrice: 400);

        Assert.Equal(
            RunStatus.Failure,
            new ErrandHandler(Settings(recipient: string.Empty)).Build().Tick(state));
    }

    [Fact]
    public void TrainingIsReportedAsUnsupportedRatherThanApproximated()
    {
        // A trainer for the character's own class is something a profile cannot express and
        // this project has no data for.
        Assert.Null(new ErrandHandler(Settings()).Destination(Errand.Train, mapId: 0));
    }

    [Fact]
    public void AVendorOnAnotherMapIsNotADestination()
    {
        Assert.Null(new ErrandHandler(Settings()).Destination(Errand.Sell, mapId: 571));
    }

    [Fact]
    public void RepairingLooksForARepairerRatherThanAnyVendor()
    {
        Assert.Null(new ErrandHandler(Settings(repairer: false)).Destination(Errand.Repair, mapId: 0));
        Assert.NotNull(new ErrandHandler(Settings(repairer: false)).Destination(Errand.Sell, mapId: 0));
    }

    [Fact]
    public void MailGoesToAMailboxRatherThanAVendor()
    {
        Assert.Equal(
            MailboxEntry,
            new ErrandHandler(Settings()).Destination(Errand.Mail, mapId: 0)!.Entry);

        Assert.Null(new ErrandHandler(Settings(mailbox: false)).Destination(Errand.Mail, mapId: 0));
    }

    [Fact]
    public void NoErrandIsAlreadyFinished()
    {
        FakeBotState state = new() { CurrentErrand = Errand.None };

        Assert.Equal(RunStatus.Success, new ErrandHandler(Settings()).Build().Tick(state));
    }
}
