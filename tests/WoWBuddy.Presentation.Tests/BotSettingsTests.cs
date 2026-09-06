using WoWBuddy.BotBases.Group;
using WoWBuddy.CombatRoutines;
using WoWBuddy.Common.Configuration;
using WoWBuddy.Presentation;
using Xunit;

namespace WoWBuddy.Presentation.Tests;

public sealed class BotSettingsTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"wowbuddy-settings-{Guid.NewGuid():N}");

    private MainViewModel Build(out FakeController controller)
    {
        controller = new FakeController();

        return new MainViewModel(
            new FakeDiscovery().With(1234),
            controller,
            new RoutineCatalogue(),
            plugins: null,
            new ConfigStore(_root));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ChoosingABotBaseIsRememberedForNextTime()
    {
        // Picking these again on every launch is the sort of friction that makes a tool feel
        // unfinished, and none of it is expensive to keep.
        MainViewModel first = Build(out _);
        first.SelectedBotBase = first.BotBases.First(option => option.Name == "Fish");

        MainViewModel second = Build(out _);

        Assert.Equal("Fish", second.SelectedBotBase.Name);
    }

    [Fact]
    public void TheChosenRoutineIsRememberedToo()
    {
        MainViewModel first = Build(out _);
        first.SelectedRoutine = first.Routines.First(routine => routine.Name == "Shadow Priest");

        MainViewModel second = Build(out _);

        Assert.Equal("Shadow Priest", second.SelectedRoutine!.Name);
    }

    [Fact]
    public void TheProfilePathComesBackAndIsValidatedAgain()
    {
        string path = Path.Combine(_root, "profile.xml");
        Directory.CreateDirectory(_root);
        File.WriteAllText(path, """<Profile Name="Kept" />""");

        MainViewModel first = Build(out _);
        first.ProfilePath = path;

        MainViewModel second = Build(out _);

        Assert.Equal(path, second.ProfilePath);
        Assert.Contains("Kept", second.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void NoSettingsFileMeansDefaultsRatherThanAFailure()
    {
        // Settings are a convenience; losing them should never stop the bot starting.
        MainViewModel model = Build(out _);

        Assert.Equal("Grind", model.SelectedBotBase.Name);
        Assert.NotNull(model.SelectedRoutine);
    }

    [Fact]
    public void ReadingSettingsBackDoesNotWriteThemAgain()
    {
        // Every assignment during a load would write the file, and a half-applied load would be
        // what got written.
        MainViewModel first = Build(out _);
        first.SelectedBotBase = first.BotBases.First(option => option.Name == "Gather");

        string file = new ConfigStore(_root).PathFor(BotSettings.FileName);
        DateTime written = File.GetLastWriteTimeUtc(file);

        _ = Build(out _);

        Assert.Equal(written, File.GetLastWriteTimeUtc(file));
    }

    [Fact]
    public void AWindowWithNowhereToKeepSettingsStillWorks()
    {
        MainViewModel model = new(new FakeDiscovery().With(1), new FakeController());

        model.SelectedBotBase = model.BotBases.First(option => option.Name == "Fish");

        Assert.Equal("Fish", model.SelectedBotBase.Name);
    }

    [Fact]
    public void MeshesDefaultToTheFolderBesideTheExecutable()
    {
        // Extracting them puts them wherever the extractor ran, and copying gigabytes to sit
        // next to the bot is a poor default to force on anyone.
        BotSettings settings = new();

        Assert.Equal(
            Path.Combine("/somewhere", "mmaps"),
            settings.ResolveMmaps("/somewhere"));

        Assert.Equal(
            "/elsewhere/mmaps",
            (settings with { MmapsDirectory = "/elsewhere/mmaps" }).ResolveMmaps("/somewhere"));
    }

    [Fact]
    public void NothingInSettingsCouldLogACharacterIn()
    {
        // This project makes no network calls, and storing an account password to be typed into
        // a game client is a promise it is not in a position to keep safely.
        string[] names = [.. typeof(BotSettings).GetProperties().Select(property => property.Name)];

        Assert.DoesNotContain(names, name =>
            name.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Account", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Credential", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheSettingsTheBotWillNotGuessAtAreEditableAndKept()
    {
        // Role, skinning, the mail recipient and where the meshes are: four things the bot
        // cannot work out for itself and refuses to assume.
        MainViewModel first = Build(out _);

        first.Role = PartyRole.Healer;
        first.CanSkin = true;
        first.MailRecipient = "Bank";
        first.MmapsDirectory = "/data/mmaps";

        MainViewModel second = Build(out _);

        Assert.Equal(PartyRole.Healer, second.Role);
        Assert.True(second.CanSkin);
        Assert.Equal("Bank", second.MailRecipient);
        Assert.Equal("/data/mmaps", second.MmapsDirectory);
    }

    [Fact]
    public void ChangingOneOfThemTellsTheWindow()
    {
        MainViewModel model = Build(out _);

        List<string> changed = [];
        model.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

        model.CanSkin = true;

        Assert.Contains(nameof(MainViewModel.CanSkin), changed);
    }

    [Fact]
    public void ATalentBuildIsCheckedAsItIsTyped()
    {
        // The alternative is finding out at the moment a level-up hands the character a point
        // it then spends wrongly.
        MainViewModel model = Build(out _);

        model.TalentBuild = "1:3, oops";
        Assert.NotEmpty(model.TalentBuildError);

        model.TalentBuild = "1:3, 1:3, 2:5";
        Assert.Empty(model.TalentBuildError);
    }

    [Fact]
    public void ATalentBuildIsKeptLikeEverythingElse()
    {
        MainViewModel first = Build(out _);
        first.TalentBuild = "1:3, 2:5";

        Assert.Equal("1:3, 2:5", Build(out _).TalentBuild);
    }

    [Fact]
    public void EverySettingSurvivesARoundTrip()
    {
        ConfigStore store = new(_root);

        BotSettings settings = new()
        {
            BotBase = "Dungeon",
            Routine = "Holy Priest",
            ProfilePath = "/profiles/x.xml",
            MmapsDirectory = "/data/mmaps",
            Role = PartyRole.Healer,
            CanSkin = true,
            MailRecipient = "Bank",
            CraftProfession = "Cooking",
            CraftRecipe = "Spiced Wolf Meat",
            TalentBuild = "1:3, 2:5",
        };

        store.Save(BotSettings.FileName, settings);

        Assert.Equal(settings, store.Load<BotSettings>(BotSettings.FileName));
    }
}
