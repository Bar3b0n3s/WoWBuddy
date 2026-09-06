using WoWBuddy.Common.Configuration;
using Xunit;

namespace WoWBuddy.Common.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "wowbuddy-tests", Guid.NewGuid().ToString("N"));

    private sealed class TestSettings
    {
        public int PullDistance { get; set; } = 25;

        public string VendorName { get; set; } = string.Empty;

        public bool LootGreys { get; set; }
    }

    [Fact]
    public void LoadReturnsDefaultsWhenNothingHasBeenSaved()
    {
        var store = new ConfigStore(_root);

        TestSettings settings = store.Load<TestSettings>("combat");

        Assert.Equal(25, settings.PullDistance);
    }

    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        var store = new ConfigStore(_root);
        var settings = new TestSettings { PullDistance = 40, VendorName = "Innkeeper Allison", LootGreys = true };

        store.Save("combat", settings);
        TestSettings loaded = store.Load<TestSettings>("combat");

        Assert.Equal(40, loaded.PullDistance);
        Assert.Equal("Innkeeper Allison", loaded.VendorName);
        Assert.True(loaded.LootGreys);
    }

    [Fact]
    public void SettingsAreKeptSeparatePerCharacter()
    {
        var store = new ConfigStore(_root);
        var alpha = new CharacterKey("Icecrown", "Thrall");
        var beta = new CharacterKey("Icecrown", "Jaina");

        store.Save("combat", new TestSettings { PullDistance = 10 }, alpha);
        store.Save("combat", new TestSettings { PullDistance = 30 }, beta);

        Assert.Equal(10, store.Load<TestSettings>("combat", alpha).PullDistance);
        Assert.Equal(30, store.Load<TestSettings>("combat", beta).PullDistance);
    }

    [Fact]
    public void CorruptFilesAreQuarantinedAndDefaultsReturned()
    {
        // The bot runs unattended for hours; a settings file truncated by a crash must not
        // stop it starting next time.
        var store = new ConfigStore(_root);
        string path = store.PathFor("combat");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not json");

        TestSettings loaded = store.Load<TestSettings>("combat");

        Assert.Equal(25, loaded.PullDistance);
        Assert.True(File.Exists(path + ".corrupt"));
    }

    [Theory]
    [InlineData("Icecrown", "Thrall", "Icecrown_Thrall")]
    [InlineData("Lord/Aeon", "Bob", "Lord-Aeon_Bob")]
    [InlineData("", "Bob", "unknown_Bob")]
    public void CharacterFolderNamesAreFilesystemSafe(string realm, string name, string expected)
    {
        Assert.Equal(expected, new CharacterKey(realm, name).ToFolderName());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
