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
    public void TrainingIsReportedAsUnsupportedWithoutWorldData()
    {
        // A profile cannot say where a warrior trainer stands, and the trainer flag alone does
        // not distinguish one from a profession trainer or a mount vendor.
        Assert.Null(new ErrandHandler(Settings()).Destination(Errand.Train, mapId: 0));
    }

    [Fact]
    public void TrainingFindsAClassTrainerWhenWorldDataCanSupplyOne()
    {
        ProfileVendor trainer = new("Warrior Trainer", 913, 0, Town);

        ErrandHandler handler = new(Settings() with
        {
            CharacterClass = 1,
            FindTrainer = (map, _, characterClass) =>
                map == 0 && characterClass == 1 ? trainer : null,
        });

        Assert.Equal(trainer, handler.Destination(Errand.Train, mapId: 0));

        // And not somebody else's trainer.
        ErrandHandler mage = new(Settings() with
        {
            CharacterClass = 8,
            FindTrainer = (map, _, characterClass) =>
                map == 0 && characterClass == 1 ? trainer : null,
        });

        Assert.Null(mage.Destination(Errand.Train, mapId: 0));
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

    // ---- training ---------------------------------------------------------------------------

    private const uint TrainerEntry = 5001;

    private static ErrandHandlerSettings TrainingSettings(long keepBack = 10_000) => Settings() with
    {
        CharacterClass = 1,
        KeepCopperWhenTraining = keepBack,
        FindTrainer = (_, _, _) => new ProfileVendor("Warrior Trainer", TrainerEntry, 0, Town),
    };

    private static FakeBotState AtTheTrainer(long copper = 100_000)
    {
        FakeBotState state = new() { Position = Town, CurrentErrand = Errand.Train };
        state.AddVisible(1, TrainerEntry, distance: 2f);
        state.Inventory = new InventoryState(16, 16, 100d, copper);
        state.TrainerState.IsTrainerOpen = true;
        return state;
    }

    [Fact]
    public void ItWaitsForTheTrainersWindowRatherThanAMerchants()
    {
        // The bug this pins: the errand waited for a merchant window at a trainer, which was
        // never going to appear, so it clicked for ten seconds and gave up. Every time.
        FakeBotState state = AtTheTrainer();
        state.TrainerState.IsTrainerOpen = false;

        Assert.Equal(RunStatus.Running, new ErrandHandler(TrainingSettings()).Build().Tick(state));
        Assert.Contains($"Interact({state.VisibleObjects[0].Guid})", state.Actions);
    }

    [Fact]
    public void LearnsEverythingItCanAffordAndClosesTheWindow()
    {
        FakeBotState state = AtTheTrainer(copper: 100_000);
        state.TrainerState.Teaching("Heroic Strike", 5_000).Teaching("Rend", 8_000);

        Assert.Equal(RunStatus.Success, new ErrandHandler(TrainingSettings()).Build().Tick(state));

        Assert.Contains("Learn(Heroic Strike)", state.TrainerState.Actions);
        Assert.Contains("Learn(Rend)", state.TrainerState.Actions);
        Assert.Contains("CloseTrainer", state.TrainerState.Actions);
    }

    [Fact]
    public void ItKeepsMoneyBackSoTheCharacterCanStillRepair()
    {
        // A trainer will take every copper the character has, and a character that cannot
        // repair dies to things it used to beat.
        FakeBotState state = AtTheTrainer(copper: 12_000);
        state.TrainerState.Teaching("Heroic Strike", 5_000);

        new ErrandHandler(TrainingSettings(keepBack: 10_000)).Build().Tick(state);

        Assert.DoesNotContain(
            state.TrainerState.Actions,
            action => action.StartsWith("Learn(", StringComparison.Ordinal));
    }

    [Fact]
    public void ItLearnsWhatItCanAffordAndLeavesWhatItCannot()
    {
        // In the trainer's own order, which is level order, so a character short of money gets
        // the earliest things it is missing rather than one expensive rank of something.
        FakeBotState state = AtTheTrainer(copper: 16_000);
        state.TrainerState.Teaching("Heroic Strike", 5_000).Teaching("Rend", 50_000);

        new ErrandHandler(TrainingSettings()).Build().Tick(state);

        Assert.Contains("Learn(Heroic Strike)", state.TrainerState.Actions);
        Assert.DoesNotContain("Learn(Rend)", state.TrainerState.Actions);
    }

    [Fact]
    public void ATrainerWithNothingToTeachIsAFinishedErrandNotAFailedOne()
    {
        // A character that is up to date has nothing to learn, and the errand is over. Failing
        // would be read as "could not do it", and the planner would send it back.
        FakeBotState state = AtTheTrainer();

        Assert.Equal(RunStatus.Success, new ErrandHandler(TrainingSettings()).Build().Tick(state));
        Assert.Contains("CloseTrainer", state.TrainerState.Actions);
    }

    [Fact]
    public void ATrainerThatRefusesIsNotAskedThirtyMoreTimes()
    {
        FakeBotState state = AtTheTrainer();
        state.TrainerState.Teaching("Heroic Strike", 5_000);
        state.TrainerState.CanLearn = false;

        new ErrandHandler(TrainingSettings()).Build().Tick(state);

        Assert.Single(
            state.TrainerState.Actions,
            action => action.StartsWith("Learn(", StringComparison.Ordinal));
    }

    [Fact]
    public void WithNoWorldDataThereIsNowhereToTrainAndItSaysSo()
    {
        // A profile cannot say where a warrior trainer stands, so without an export the errand
        // is impossible rather than approximated with the nearest thing wearing a trainer flag.
        FakeBotState state = AtTheTrainer();

        Assert.Equal(RunStatus.Failure, new ErrandHandler(Settings()).Build().Tick(state));
    }
}
