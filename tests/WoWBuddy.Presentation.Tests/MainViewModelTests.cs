using System.ComponentModel;
using WoWBuddy.Presentation;
using Xunit;

namespace WoWBuddy.Presentation.Tests;

public sealed class MainViewModelTests
{
    private static MainViewModel Build(
        out FakeDiscovery discovery,
        out FakeController controller,
        bool supported = true)
    {
        discovery = new FakeDiscovery().With(1234, supported);
        controller = new FakeController();
        return new MainViewModel(discovery, controller);
    }

    [Fact]
    public void FindsClientsWhenItStarts()
    {
        MainViewModel model = Build(out FakeDiscovery discovery, out _);

        Assert.Equal(1, discovery.Calls);
        Assert.Single(model.Clients);
        Assert.Equal(1234, model.SelectedClient!.Value.ProcessId);
        Assert.Equal("One client found.", model.Status);
    }

    [Fact]
    public void SaysSoWhenThereIsNoClient()
    {
        MainViewModel model = new(new FakeDiscovery(), new FakeController());

        Assert.Empty(model.Clients);
        Assert.Null(model.SelectedClient);
        Assert.False(model.CanAttach);
        Assert.Contains("Start the game", model.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshingKeepsTheSelectedClientWhenItIsStillThere()
    {
        // Otherwise a refresh while picking between two windowed clients silently moves the
        // selection, and the user attaches to the wrong one.
        FakeDiscovery discovery = new FakeDiscovery().With(1).With(2);
        MainViewModel model = new(discovery, new FakeController());

        model.SelectedClient = model.Clients[1];
        model.RefreshClients();

        Assert.Equal(2, model.SelectedClient!.Value.ProcessId);
    }

    [Fact]
    public void RefusesToAttachToABuildItDoesNotKnow()
    {
        // Every offset in the table is for 12340. Against another build they point at whatever
        // happens to be there, and the bot would read plausible-looking rubbish and act on it.
        MainViewModel model = Build(out _, out FakeController controller, supported: false);

        Assert.False(model.CanAttach);
        Assert.False(model.AttachCommand.CanExecute(null));

        model.AttachCommand.Execute(null);

        Assert.Empty(controller.Actions);
    }

    [Fact]
    public void AttachingShowsTheVerificationReport()
    {
        MainViewModel model = Build(out _, out FakeController controller);
        controller.Report = "Unit position offset resolved at 0x798.";

        model.AttachCommand.Execute(null);

        Assert.True(model.IsAttached);
        Assert.Equal("Unit position offset resolved at 0x798.", model.VerificationReport);
        Assert.Equal("Attached.", model.Status);
    }

    [Fact]
    public void AFailedAttachStillShowsTheReport()
    {
        // The report is the only thing that says why, so it matters most when it failed.
        MainViewModel model = Build(out _, out FakeController controller);
        controller.AttachSucceeds = false;
        controller.Report = "Object manager pointer chain did not resolve.";

        model.AttachCommand.Execute(null);

        Assert.False(model.IsAttached);
        Assert.Contains("pointer chain", model.VerificationReport, StringComparison.Ordinal);
        Assert.Contains("failed", model.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CannotAttachTwice()
    {
        MainViewModel model = Build(out _, out _);

        model.AttachCommand.Execute(null);

        Assert.False(model.CanAttach);
    }

    [Fact]
    public void CannotStartBeforeAttaching()
    {
        MainViewModel model = Build(out _, out FakeController controller);

        Assert.False(model.CanStart);
        Assert.Equal("Attach to a client first.", model.StartBlockedReason);

        model.StartCommand.Execute(null);

        Assert.Empty(controller.Actions);
    }

    [Fact]
    public void StartsOnceAttached()
    {
        MainViewModel model = Build(out _, out FakeController controller);

        model.AttachCommand.Execute(null);
        model.EnableExecutionCommand.Execute(null);

        Assert.True(model.CanStart);
        Assert.Empty(model.StartBlockedReason);

        model.StartCommand.Execute(null);

        Assert.True(model.IsRunning);
        Assert.Contains(controller.Actions, action => action.StartsWith("Start(Grind", StringComparison.Ordinal));
    }

    [Fact]
    public void ABotBaseThatNeedsAProfileWillNotStartWithoutOne()
    {
        MainViewModel model = Build(out _, out FakeController controller);
        model.AttachCommand.Execute(null);
        model.EnableExecutionCommand.Execute(null);

        model.SelectedBotBase = model.BotBases.First(option => option.Name == "Questing");

        Assert.True(model.NeedsProfile);
        Assert.False(model.CanStart);
        Assert.Contains("needs a profile", model.StartBlockedReason, StringComparison.Ordinal);

        model.StartCommand.Execute(null);

        Assert.False(model.IsRunning);
    }

    [Fact]
    public void AProfileWithErrorsIsRefusedWhenItIsChosen()
    {
        // The whole point of validating at load: a mistake should say so when the profile is
        // chosen, not four hours into a session.
        string path = WriteProfile("""<Profile Name="x"><QuestOrder><RunTo Map="0" /></QuestOrder></Profile>""");

        try
        {
            MainViewModel model = Build(out _, out _);
            model.AttachCommand.Execute(null);
            model.EnableExecutionCommand.Execute(null);
            model.SelectedBotBase = model.BotBases.First(option => option.Name == "Questing");
            model.ProfilePath = path;

            Assert.Contains("error(s)", model.ProfileReport, StringComparison.Ordinal);
            Assert.False(model.CanStart);
            Assert.Contains("has errors", model.StartBlockedReason, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AProfileWithOnlyWarningsIsUsable()
    {
        string path = WriteProfile(
            """
            <Profile Name="Warned">
              <QuestOrder>
                <PickUp QuestId="9001" Entry="8100" Map="0" X="1500" Y="-2500" Z="60" />
              </QuestOrder>
            </Profile>
            """);

        try
        {
            MainViewModel model = Build(out _, out FakeController controller);
            model.AttachCommand.Execute(null);
            model.EnableExecutionCommand.Execute(null);
            model.SelectedBotBase = model.BotBases.First(option => option.Name == "Questing");
            model.ProfilePath = path;

            // Picked up and never handed in: worth saying, not worth refusing.
            Assert.Contains("warning(s)", model.Status, StringComparison.Ordinal);
            Assert.True(model.CanStart);

            model.StartCommand.Execute(null);

            Assert.Contains(controller.Actions, action => action.Contains(path, StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ClearingTheProfileClearsItsReport()
    {
        string path = WriteProfile("""<Profile Name="x" />""");

        try
        {
            MainViewModel model = Build(out _, out _);
            model.ProfilePath = path;
            Assert.NotEmpty(model.ProfileReport);

            model.ProfilePath = string.Empty;
            Assert.Empty(model.ProfileReport);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DetachingWhileRunningStopsFirstAndSaysSo()
    {
        // A user who clicks detach mid-session should not have to wonder whether the bot is
        // still playing.
        MainViewModel model = Build(out _, out FakeController controller);

        model.AttachCommand.Execute(null);
        model.EnableExecutionCommand.Execute(null);
        model.StartCommand.Execute(null);
        model.DetachCommand.Execute(null);

        Assert.False(model.IsRunning);
        Assert.False(model.IsAttached);
        Assert.Equal("Stopped and detached.", model.Status);
        Assert.Contains("Detach", controller.Actions);
    }

    [Fact]
    public void StoppingKeepsTheAttachment()
    {
        MainViewModel model = Build(out _, out _);

        model.AttachCommand.Execute(null);
        model.EnableExecutionCommand.Execute(null);
        model.StartCommand.Execute(null);
        model.StopCommand.Execute(null);

        Assert.False(model.IsRunning);
        Assert.True(model.IsAttached);
        Assert.True(model.CanStart);
    }

    [Fact]
    public void CommandsTellTheWindowWhenTheirStateChanges()
    {
        // A button that is enabled when it should not be is how a user attaches twice.
        MainViewModel model = Build(out _, out _);

        int raised = 0;
        model.StartCommand.CanExecuteChanged += (_, _) => raised++;

        model.AttachCommand.Execute(null);

        Assert.True(raised > 0);
    }

    [Fact]
    public void ChangingTheBotBaseAnnouncesWhetherAProfileIsNeeded()
    {
        MainViewModel model = Build(out _, out _);

        List<string> changed = [];
        ((INotifyPropertyChanged)model).PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

        model.SelectedBotBase = model.BotBases.First(option => option.Name == "Gather");

        Assert.Contains(nameof(MainViewModel.NeedsProfile), changed);
    }

    [Fact]
    public void EveryRoutineIsOfferedAndOneIsChosen()
    {
        MainViewModel model = Build(out _, out _);

        Assert.Equal(30, model.Routines.Count);
        Assert.NotNull(model.SelectedRoutine);
    }

    [Fact]
    public void EveryBotBaseIsOffered()
    {
        MainViewModel model = Build(out _, out _);

        Assert.Equal(6, model.BotBases.Count);
        Assert.Contains(model.BotBases, option => option.NeedsGroup);
        Assert.Contains(model.BotBases, option => option.NeedsProfile);
    }


    [Fact]
    public void ReadingIsAllowedBeforeActingIs()
    {
        // Attaching only reads. A user who wants to look at what the bot can see should not
        // have to let it write to the client to do that.
        MainViewModel model = Build(out _, out _);

        model.AttachCommand.Execute(null);

        Assert.True(model.IsAttached);
        Assert.False(model.CanExecute);
        Assert.True(model.CanEnableExecution);
        Assert.False(model.CanStart);
        Assert.Contains("read the client but not act on it", model.StartBlockedReason, StringComparison.Ordinal);
    }

    [Fact]
    public void EnablingExecutionShowsWhatTheClientSupports()
    {
        MainViewModel model = Build(out _, out FakeController controller);
        controller.ClientCapabilities = "Client capabilities: 9 of 11 available.";

        model.AttachCommand.Execute(null);
        model.EnableExecutionCommand.Execute(null);

        Assert.True(model.CanExecute);
        Assert.False(model.CanEnableExecution);
        Assert.Contains("9 of 11", model.ClientCapabilities, StringComparison.Ordinal);
        Assert.Equal("Execution enabled.", model.Status);
    }

    [Fact]
    public void AFailedExecutionHookLeavesTheBotAbleToRead()
    {
        MainViewModel model = Build(out _, out FakeController controller);
        controller.ExecutionSucceeds = false;

        model.AttachCommand.Execute(null);
        model.EnableExecutionCommand.Execute(null);

        Assert.True(model.IsAttached);
        Assert.False(model.CanExecute);
        Assert.False(model.CanStart);
        Assert.Contains("Could not install", model.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void DetachingGivesUpExecutionToo()
    {
        MainViewModel model = Build(out _, out _);

        model.AttachCommand.Execute(null);
        model.EnableExecutionCommand.Execute(null);
        model.DetachCommand.Execute(null);

        Assert.False(model.CanExecute);
        Assert.False(model.CanEnableExecution);
    }

    private static string WriteProfile(string xml)
    {
        string path = Path.Combine(Path.GetTempPath(), $"wowbuddy-ui-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, xml);
        return path;
    }
}
