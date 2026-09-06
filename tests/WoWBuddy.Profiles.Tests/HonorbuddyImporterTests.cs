using WoWBuddy.Profiles.Import;
using Xunit;

namespace WoWBuddy.Profiles.Tests;

public sealed class HonorbuddyImporterTests
{
    /// <summary>
    /// An Honorbuddy-shaped profile written for these tests.
    /// </summary>
    /// <remarks>
    /// Written here rather than taken from a real profile: no community profile is included in
    /// this repository, and every id and coordinate below is invented.
    /// </remarks>
    private const string Sample = """
        <HBProfile>
          <Name>Test Zone</Name>
          <MinLevel>1</MinLevel>
          <MaxLevel>10</MaxLevel>
          <Vendors>
            <Vendor Name="Smith" Entry="8300" Type="Repair" X="1500" Y="-2500" Z="60" />
          </Vendors>
          <Blackspots>
            <Blackspot X="1600" Y="-2600" Z="60" Radius="40" />
          </Blackspots>
          <AvoidMobs>
            <Mob Name="Something Nasty" Entry="8500" />
          </AvoidMobs>
          <QuestOrder>
            <PickUp QuestName="First" QuestId="9001" GiverName="Someone" GiverId="8100" X="1500" Y="-2500" Z="60" />
            <KillMobs QuestName="First" QuestId="9001" MobId="8200" KillCount="8">
              <HuntingGrounds>
                <Hotspot X="1560" Y="-2460" Z="60" />
                <Hotspot X="1580" Y="-2420" Z="60" />
              </HuntingGrounds>
            </KillMobs>
            <TurnIn QuestName="First" QuestId="9001" TurnInName="Someone" TurnInId="8100" X="1500" Y="-2500" Z="60" />
            <If Condition="Me.Level &gt;= 5">
              <PickUp QuestName="Second" QuestId="9002" GiverId="8100" X="1500" Y="-2500" Z="60" />
              <CollectItem QuestName="Second" QuestId="9002" ItemId="8600" CollectCount="6" MobId="8200"
                           CollectionDistance="80" X="1560" Y="-2460" Z="60" />
              <TurnIn QuestName="Second" QuestId="9002" TurnInId="8100" X="1500" Y="-2500" Z="60" />
            </If>
            <RunTo X="1700" Y="-2300" Z="62" />
          </QuestOrder>
        </HBProfile>
        """;

    [Fact]
    public void ConvertsAProfileItFullyUnderstands()
    {
        ProfileImportResult result = HonorbuddyImporter.Import(Sample, "test.xml", mapId: 1);

        Assert.True(result.ProducedSomething);
        Assert.Equal(7, result.StepsConverted);
        Assert.Equal(0, result.StepsNotUnderstood);
        Assert.Equal(1, result.ConditionsConverted);
        Assert.Equal(0, result.ConditionsLost);

        Profile profile = result.Profile!;
        Assert.Equal("Test Zone", profile.Name);
        Assert.Equal(1, profile.MinimumLevel);
        Assert.Equal(10, profile.MaximumLevel);

        Assert.Equal(
            [StepKind.PickUp, StepKind.Objective, StepKind.TurnIn,
             StepKind.PickUp, StepKind.Objective, StepKind.TurnIn, StepKind.RunTo],
            profile.Steps.Select(step => step.Kind));

        // The map came from the caller, so every step is on it.
        Assert.All(profile.Steps, step => Assert.Equal(1, step.MapId));
        Assert.All(profile.Vendors, vendor => Assert.Equal(1, vendor.MapId));
    }

    [Fact]
    public void KeepsWhatEachStepMeant()
    {
        ProfileImportResult result = HonorbuddyImporter.Import(Sample, "test.xml", mapId: 1);

        Profile profile = result.Profile!;

        ProfileStep kill = profile.Steps[1];
        Assert.Equal(ObjectiveKind.Kill, kill.Objective);
        Assert.Equal(9001u, kill.QuestId);
        Assert.Equal(8200u, kill.Entry);
        Assert.Equal(8, kill.Count);
        Assert.Equal(2, kill.Spots.Count);

        ProfileStep collect = profile.Steps[4];
        Assert.Equal(ObjectiveKind.Collect, collect.Objective);
        Assert.Equal(8600u, collect.ItemId);
        Assert.Equal(6, collect.Count);
        Assert.Equal(80f, collect.Radius);

        // The If was folded onto the three steps inside it.
        Assert.All(
            profile.Steps.Skip(3).Take(3),
            step => Assert.Equal(ConditionTerm.Level, Assert.Single(step.When).Term));

        Assert.Empty(profile.Steps[6].When);
    }

    [Fact]
    public void TheConversionIsAProfileTheLoaderAccepts()
    {
        ProfileImportResult result = HonorbuddyImporter.Import(Sample, "test.xml", mapId: 1);

        // The user gets a file, not just an object, so the round trip has to hold.
        ProfileLoadResult reloaded = ProfileLoader.Parse(result.ConvertedXml, "converted.xml");

        Assert.True(reloaded.Success, reloaded.Describe());
        Assert.Equal(result.Profile!.Steps.Count, reloaded.Profile!.Steps.Count);
        Assert.Contains("Converted from the Honorbuddy profile test.xml", result.ConvertedXml, StringComparison.Ordinal);
    }

    [Fact]
    public void SaysWhichMapItAssumed()
    {
        ProfileImportResult result = HonorbuddyImporter.Import(Sample, "test.xml", mapId: 1);

        Assert.Contains(
            result.Warnings,
            issue => issue.Message.Contains("put on map 1", StringComparison.Ordinal));
    }

    [Fact]
    public void AStepItDoesNotKnowIsCountedAndNamed()
    {
        ProfileImportResult result = HonorbuddyImporter.Import(
            """
            <HBProfile>
              <Name>x</Name>
              <QuestOrder>
                <RunTo X="1500" Y="-2500" Z="60" />
                <FlyTo X="1600" Y="-2600" Z="60" />
              </QuestOrder>
            </HBProfile>
            """,
            "test.xml");

        Assert.Equal(1, result.StepsConverted);
        Assert.Equal(1, result.StepsNotUnderstood);
        Assert.False(result.Success);

        Assert.Contains(
            result.Warnings,
            issue => issue.Element == "FlyTo"
                && issue.Message.Contains("missing from the converted profile", StringComparison.Ordinal));
    }

    [Fact]
    public void AConditionItCannotTranslateLeavesTheStepUngatedAndSaysSo()
    {
        ProfileImportResult result = HonorbuddyImporter.Import(
            """
            <HBProfile>
              <Name>x</Name>
              <QuestOrder>
                <RunTo X="1500" Y="-2500" Z="60" Condition="Me.IsInParty" />
              </QuestOrder>
            </HBProfile>
            """,
            "test.xml");

        // The step survives, because dropping it would lose quest progress. But the import is
        // not clean, and the report says exactly which step to look at.
        Assert.Equal(1, result.StepsConverted);
        Assert.Equal(1, result.ConditionsLost);
        Assert.False(result.Success);
        Assert.Empty(result.Profile!.Steps[0].When);

        Assert.Contains(
            result.Warnings,
            issue => issue.Message.Contains("runs unconditionally", StringComparison.Ordinal)
                && issue.Message.Contains("Me.IsInParty", StringComparison.Ordinal));
    }

    [Fact]
    public void AWhileWithAnUntranslatableConditionIsDroppedRatherThanLeftEndless()
    {
        ProfileImportResult result = HonorbuddyImporter.Import(
            """
            <HBProfile>
              <Name>x</Name>
              <QuestOrder>
                <While Condition="Me.IsInParty">
                  <RunTo X="1500" Y="-2500" Z="60" />
                </While>
              </QuestOrder>
            </HBProfile>
            """,
            "test.xml");

        // An If without its condition runs its contents once too often. A While without its
        // condition never stops, so it is left out entirely.
        Assert.Empty(result.Profile!.Steps);
        Assert.False(result.Success);
        Assert.Contains(
            result.Warnings,
            issue => issue.Message.Contains("never ends", StringComparison.Ordinal));
    }

    [Fact]
    public void AWhileItUnderstandsBecomesARepeat()
    {
        ProfileImportResult result = HonorbuddyImporter.Import(
            """
            <HBProfile>
              <Name>x</Name>
              <QuestOrder>
                <While Condition="Me.Level &lt; 10">
                  <RunTo X="1500" Y="-2500" Z="60" />
                </While>
              </QuestOrder>
            </HBProfile>
            """,
            "test.xml");

        ProfileStep repeat = Assert.Single(result.Profile!.Steps);
        Assert.Equal(StepKind.Repeat, repeat.Kind);
        Assert.Equal(StepKind.RunTo, Assert.Single(repeat.Inner).Kind);
    }

    [Fact]
    public void CustomBehavioursAreCarriedAcrossWithAWarningThatTheyAreCode()
    {
        ProfileImportResult result = HonorbuddyImporter.Import(
            """
            <HBProfile>
              <Name>x</Name>
              <QuestOrder>
                <CustomBehavior File="WaitTimer" WaitTime="5000" />
              </QuestOrder>
            </HBProfile>
            """,
            "test.xml");

        ProfileStep step = Assert.Single(result.Profile!.Steps);
        Assert.Equal(StepKind.CustomBehavior, step.Kind);
        Assert.Equal("WaitTimer", step.BehaviorName);
        Assert.Equal("5000", step.Args["WaitTime"]);

        Assert.Contains(
            result.Warnings,
            issue => issue.Message.Contains("Honorbuddy code and cannot be converted", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAttributeItDoesNotKnowIsReportedAsLost()
    {
        ProfileImportResult result = HonorbuddyImporter.Import(
            """
            <HBProfile>
              <Name>x</Name>
              <QuestOrder>
                <RunTo X="1500" Y="-2500" Z="60" UseFlightPath="true" />
              </QuestOrder>
            </HBProfile>
            """,
            "test.xml");

        Assert.Contains(
            result.Warnings,
            issue => issue.Message.Contains("UseFlightPath", StringComparison.Ordinal)
                && issue.Message.Contains("was lost", StringComparison.Ordinal));
    }

    [Fact]
    public void VendorsMailboxesBlackspotsAndAvoidedMobsComeAcross()
    {
        ProfileImportResult result = HonorbuddyImporter.Import(
            """
            <HBProfile>
              <Name>x</Name>
              <Vendors>
                <Vendor Name="Smith" Entry="8300" Type="Repair" X="1500" Y="-2500" Z="60" />
                <Vendor Name="Grocer" Entry="8301" Type="Food" X="1510" Y="-2510" Z="60" />
              </Vendors>
              <Mailboxes>
                <Mailbox Name="Post" Entry="8400" X="1520" Y="-2520" Z="60" />
              </Mailboxes>
              <Blackspots>
                <Blackspot X="1600" Y="-2600" Z="60" Radius="40" />
              </Blackspots>
              <AvoidMobs>
                <Mob Name="Something Nasty" Entry="8500" />
              </AvoidMobs>
              <QuestOrder>
                <RunTo X="1500" Y="-2500" Z="60" />
              </QuestOrder>
            </HBProfile>
            """,
            "test.xml");

        Profile profile = result.Profile!;

        Assert.Equal(3, profile.Vendors.Count);
        Assert.True(profile.Vendors[0].CanRepair);
        Assert.False(profile.Vendors[1].CanRepair);
        Assert.True(profile.Vendors[2].IsMailbox);
        Assert.Single(profile.Blackspots);
        Assert.Contains(8500u, profile.AvoidMobs);
    }

    [Fact]
    public void AProfileWithNoQuestOrderIsRefused()
    {
        ProfileImportResult result = HonorbuddyImporter.Import(
            "<HBProfile><Name>x</Name></HBProfile>",
            "test.xml");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, issue => issue.Message.Contains("nothing to convert", StringComparison.Ordinal));
    }

    [Fact]
    public void AWoWBuddyProfileIsNotMistakenForAnHonorbuddyOne()
    {
        Assert.False(HonorbuddyImporter.LooksLikeHonorbuddy("""<Profile Name="x" />"""));
        Assert.True(HonorbuddyImporter.LooksLikeHonorbuddy("<HBProfile><Name>x</Name></HBProfile>"));
        Assert.False(HonorbuddyImporter.LooksLikeHonorbuddy("not xml at all"));

        ProfileImportResult result = HonorbuddyImporter.Import("""<Profile Name="x" />""", "test.xml");
        Assert.False(result.Success);
        Assert.Contains(result.Errors, issue => issue.Message.Contains("loads directly", StringComparison.Ordinal));
    }

    [Fact]
    public void BrokenXmlIsReportedWithItsLine()
    {
        ProfileImportResult result = HonorbuddyImporter.Import("<HBProfile>\n  <QuestOrder>\n</HBProfile>", "test.xml");

        Assert.False(result.ProducedSomething);
        Assert.Contains("not valid XML", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingFileIsAnErrorNotAnException()
    {
        ProfileImportResult result =
            HonorbuddyImporter.ImportFile(Path.Combine(Path.GetTempPath(), "no-such-hb-profile.xml"));

        Assert.False(result.Success);
        Assert.Contains("no profile at", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReportCountsWhatHappened()
    {
        ProfileImportResult result = HonorbuddyImporter.Import(
            """
            <HBProfile>
              <Name>x</Name>
              <QuestOrder>
                <RunTo X="1500" Y="-2500" Z="60" Condition="Me.IsInParty" />
                <FlyTo X="1600" Y="-2600" Z="60" />
              </QuestOrder>
            </HBProfile>
            """,
            "test.xml");

        string report = result.Describe();

        Assert.Contains("converted 1 step(s)", report, StringComparison.Ordinal);
        Assert.Contains("1 step(s) were not understood", report, StringComparison.Ordinal);
        Assert.Contains("1 condition(s) could not be translated", report, StringComparison.Ordinal);
        Assert.Contains("needs checking by hand", report, StringComparison.Ordinal);
    }

    [Fact]
    public void TheConvertedFileDeclaresTheEncodingItIsActuallyWrittenIn()
    {
        ProfileImportResult result = HonorbuddyImporter.Import(Sample, "test.xml", mapId: 1);

        // The declaration comes from the writer's encoding, and the default reports UTF-16
        // while the file lands on disk as UTF-8. A declaration that lies is worse than none.
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", result.ConvertedXml, StringComparison.Ordinal);
    }

}
