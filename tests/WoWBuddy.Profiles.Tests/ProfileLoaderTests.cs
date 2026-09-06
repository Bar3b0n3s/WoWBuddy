using Xunit;
using WoWBuddy.Common.Geometry;

namespace WoWBuddy.Profiles.Tests;

public sealed class ProfileLoaderTests
{
    private const string Minimal = """
        <Profile Name="Starter" Author="someone" Faction="Alliance" MinLevel="1" MaxLevel="10">
          <QuestOrder>
            <PickUp QuestId="9001" QuestName="Test Quest" Entry="8100" Map="0" X="1500" Y="-2500" Z="60" />
            <Objective QuestId="9001" Type="Kill" Entry="8200" Map="0" X="1560" Y="-2460" Z="60" Count="8" />
            <TurnIn QuestId="9001" Entry="8100" Map="0" X="1500" Y="-2500" Z="60" />
          </QuestOrder>
        </Profile>
        """;

    [Fact]
    public void ReadsAWholeProfile()
    {
        ProfileLoadResult result = ProfileLoader.Parse(Minimal, "starter.xml");

        Assert.True(result.Success, result.Describe());

        Profile profile = result.Profile!;
        Assert.Equal("Starter", profile.Name);
        Assert.Equal("someone", profile.Author);
        Assert.Equal(ProfileFaction.Alliance, profile.Faction);
        Assert.Equal(1, profile.MinimumLevel);
        Assert.Equal(10, profile.MaximumLevel);
        Assert.Equal("starter.xml", profile.Source);
        Assert.Equal(3, profile.Steps.Count);

        Assert.Equal(StepKind.PickUp, profile.Steps[0].Kind);
        Assert.Equal(9001u, profile.Steps[0].QuestId);
        Assert.Equal(8100u, profile.Steps[0].Entry);
        Assert.Equal(new Vector3(1500f, -2500f, 60f), profile.Steps[0].Position);

        Assert.Equal(ObjectiveKind.Kill, profile.Steps[1].Objective);
        Assert.Equal(8, profile.Steps[1].Count);

        Assert.Equal(StepKind.TurnIn, profile.Steps[2].Kind);
    }

    [Fact]
    public void ReadsVendorsBlackspotsAndAvoidedMobs()
    {
        ProfileLoadResult result = ProfileLoader.Parse(
            """
            <Profile Name="x">
              <Vendors>
                <Vendor Name="Smith" Entry="8300" Map="0" X="1500" Y="-2500" Z="60" Repair="true" />
                <Mailbox Name="Post" Entry="8400" Map="0" X="1510" Y="-2510" Z="60" />
              </Vendors>
              <Blackspots>
                <Blackspot Map="0" X="1600" Y="-2600" Z="60" Radius="40" Reason="elite patrol" />
              </Blackspots>
              <AvoidMobs>
                <Mob Entry="8500" Name="Something Nasty" />
              </AvoidMobs>
              <QuestOrder>
                <RunTo Map="0" X="1500" Y="-2500" Z="60" />
              </QuestOrder>
            </Profile>
            """);

        Assert.True(result.Success, result.Describe());

        Profile profile = result.Profile!;

        Assert.Equal(2, profile.Vendors.Count);
        Assert.True(profile.Vendors[0].CanRepair);
        Assert.False(profile.Vendors[0].IsMailbox);
        Assert.True(profile.Vendors[1].IsMailbox);
        Assert.False(profile.Vendors[1].CanRepair);

        Assert.Single(profile.Blackspots);
        Assert.True(profile.IsBlacklisted(0, new Vector3(1610f, -2610f, 60f)));
        Assert.False(profile.IsBlacklisted(0, new Vector3(1900f, -2900f, 60f)));
        Assert.False(profile.IsBlacklisted(1, new Vector3(1610f, -2610f, 60f)));

        Assert.Contains(8500u, profile.AvoidMobs);
    }

    [Fact]
    public void AnIfBecomesConditionsOnTheStepsInsideIt()
    {
        ProfileLoadResult result = ProfileLoader.Parse(
            """
            <Profile Name="x">
              <QuestOrder>
                <If Condition="Level >= 5">
                  <If Condition="!QuestCompleted(62)">
                    <RunTo Map="0" X="1500" Y="-2500" Z="60" Condition="Money &lt; 500" />
                  </If>
                </If>
              </QuestOrder>
            </Profile>
            """);

        Assert.True(result.Success, result.Describe());

        // The nesting is flattened: the runner only ever walks a list, and a step carries
        // every condition that governs it, outermost first.
        ProfileStep step = Assert.Single(result.Profile!.Steps);
        Assert.Equal(StepKind.RunTo, step.Kind);
        Assert.Equal(3, step.When.Count);
        Assert.Equal(ConditionTerm.Level, step.When[0].Term);
        Assert.Equal(ConditionTerm.QuestCompleted, step.When[1].Term);
        Assert.Equal(ConditionTerm.Money, step.When[2].Term);

        FakeConditionContext context = new() { Level = 5, Money = 100 };
        Assert.True(step.ConditionsHold(context));

        context.Level = 4;
        Assert.False(step.ConditionsHold(context));
    }

    [Fact]
    public void AWhileKeepsItsChildren()
    {
        ProfileLoadResult result = ProfileLoader.Parse(
            """
            <Profile Name="x">
              <QuestOrder>
                <While Condition="Level &lt; 10">
                  <Grind Map="0" X="1500" Y="-2500" Z="60" Radius="80" Condition="Level &lt; 10" />
                </While>
              </QuestOrder>
            </Profile>
            """);

        Assert.True(result.Success, result.Describe());

        ProfileStep step = Assert.Single(result.Profile!.Steps);
        Assert.Equal(StepKind.Repeat, step.Kind);
        Assert.Single(step.When);
        Assert.Equal(StepKind.Grind, Assert.Single(step.Inner).Kind);

        // AllSteps walks into repeats so validation and reporting see every step.
        Assert.Equal(2, result.Profile!.AllSteps().Count());
    }

    [Fact]
    public void AWhileWithoutAConditionIsRefused()
    {
        ProfileLoadResult result = ProfileLoader.Parse(
            """
            <Profile Name="x">
              <QuestOrder>
                <While>
                  <RunTo Map="0" X="1500" Y="-2500" Z="60" />
                </While>
              </QuestOrder>
            </Profile>
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, issue => issue.Message.Contains("repeat it forever", StringComparison.Ordinal));
    }

    [Fact]
    public void CustomBehaviorKeepsWhateverAttributesItWasGiven()
    {
        ProfileLoadResult result = ProfileLoader.Parse(
            """
            <Profile Name="x">
              <QuestOrder>
                <CustomBehavior Name="WaitTimer" WaitTime="5000" GoalText="waiting" />
              </QuestOrder>
            </Profile>
            """);

        Assert.True(result.Success, result.Describe());

        ProfileStep step = Assert.Single(result.Profile!.Steps);
        Assert.Equal("WaitTimer", step.BehaviorName);
        Assert.Equal("5000", step.Args["WaitTime"]);
        Assert.Equal("waiting", step.Args["GoalText"]);
        Assert.DoesNotContain("Name", step.Args.Keys);
    }

    [Fact]
    public void UnknownStepsAreReportedRatherThanDroppedSilently()
    {
        ProfileLoadResult result = ProfileLoader.Parse(
            """
            <Profile Name="x">
              <QuestOrder>
                <FlyTo Map="0" X="1500" Y="-2500" Z="60" />
                <RunTo Map="0" X="1500" Y="-2500" Z="60" />
              </QuestOrder>
            </Profile>
            """);

        Assert.True(result.Success, result.Describe());
        Assert.Single(result.Profile!.Steps);

        ProfileIssue issue = Assert.Single(result.Warnings, warning => warning.Element == "FlyTo");
        Assert.Contains("skipped", issue.Message, StringComparison.Ordinal);
        Assert.True(issue.LineNumber > 0);
    }

    [Fact]
    public void UnknownSectionsAndAttributesAreReported()
    {
        ProfileLoadResult result = ProfileLoader.Parse(
            """
            <Profile Name="x" Difficulty="hard">
              <Mounts><Mount Id="1" /></Mounts>
              <QuestOrder>
                <RunTo Map="0" X="1500" Y="-2500" Z="60" Speed="fast" />
              </QuestOrder>
            </Profile>
            """);

        Assert.True(result.Success, result.Describe());
        Assert.Contains(result.Warnings, issue => issue.Message.Contains("Difficulty", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, issue => issue.Element == "Mounts");
        Assert.Contains(result.Warnings, issue => issue.Message.Contains("Speed", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryProblemIsReportedNotJustTheFirst()
    {
        ProfileLoadResult result = ProfileLoader.Parse(
            """
            <Profile Name="x">
              <QuestOrder>
                <PickUp QuestId="abc" Entry="8100" Map="0" X="1500" Y="-2500" Z="60" />
                <PickUp Entry="8101" Map="0" X="1500" Y="-2500" Z="60" />
                <RunTo Map="0" />
                <Objective QuestId="9001" Type="Kill" Map="0" X="1560" Y="-2460" Z="60" Condition="Rubbish >= 1" />
              </QuestOrder>
            </Profile>
            """);

        Assert.False(result.Success);

        // A profile with four separate mistakes should list four, so the author can fix them
        // in one pass rather than one reload per mistake.
        Assert.Contains(result.Errors, issue => issue.Message.Contains("not an id", StringComparison.Ordinal));
        Assert.Contains(result.Errors, issue => issue.Message.Contains("needs a QuestId", StringComparison.Ordinal));
        Assert.Contains(result.Errors, issue => issue.Message.Contains("needs a position", StringComparison.Ordinal));
        Assert.Contains(result.Errors, issue => issue.Message.Contains("Rubbish", StringComparison.Ordinal));
    }

    [Fact]
    public void PositionsOutsideTheWorldAreRefused()
    {
        ProfileLoadResult result = ProfileLoader.Parse(
            """
            <Profile Name="x">
              <QuestOrder>
                <RunTo Map="0" X="999999" Y="100" Z="50" />
              </QuestOrder>
            </Profile>
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, issue => issue.Message.Contains("not somewhere in the world", StringComparison.Ordinal));
    }

    [Fact]
    public void DecimalsAreReadTheSameWayEverywhere()
    {
        ProfileLoadResult result = ProfileLoader.Parse(
            """
            <Profile Name="x">
              <QuestOrder>
                <RunTo Map="0" X="1499.05" Y="-2500.53" Z="60.9" />
              </QuestOrder>
            </Profile>
            """);

        Assert.True(result.Success, result.Describe());

        ProfileStep step = Assert.Single(result.Profile!.Steps);
        Assert.Equal(1499.05f, step.Position.X, 2);
        Assert.Equal(-2500.53f, step.Position.Y, 2);
    }

    [Fact]
    public void AnObjectiveCanUseHotspotsInsteadOfASinglePoint()
    {
        ProfileLoadResult result = ProfileLoader.Parse(
            """
            <Profile Name="x">
              <QuestOrder>
                <PickUp QuestId="9001" Entry="8100" Map="0" X="1500" Y="-2500" Z="60" />
                <Objective QuestId="9001" Type="Collect" ItemId="8600" Map="0">
                  <Hotspot X="1560" Y="-2460" Z="60" />
                  <Hotspot X="1580" Y="-2420" Z="60" />
                </Objective>
                <TurnIn QuestId="9001" Entry="8100" Map="0" X="1500" Y="-2500" Z="60" />
              </QuestOrder>
            </Profile>
            """);

        Assert.True(result.Success, result.Describe());

        ProfileStep objective = result.Profile!.Steps[1];
        Assert.Equal(2, objective.Spots.Count);
        Assert.Equal(2, objective.AllPositions().Count());
    }

    [Fact]
    public void AnUnknownObjectiveTypeStillLoadsButSaysSo()
    {
        ProfileLoadResult result = ProfileLoader.Parse(
            """
            <Profile Name="x">
              <QuestOrder>
                <PickUp QuestId="9001" Entry="8100" Map="0" X="1500" Y="-2500" Z="60" />
                <Objective QuestId="9001" Type="EscortNpc" Map="0" X="1560" Y="-2460" Z="60" />
                <TurnIn QuestId="9001" Entry="8100" Map="0" X="1500" Y="-2500" Z="60" />
              </QuestOrder>
            </Profile>
            """);

        Assert.True(result.Success, result.Describe());
        Assert.Equal(ObjectiveKind.Unknown, result.Profile!.Steps[1].Objective);
        Assert.Contains(result.Warnings, issue => issue.Message.Contains("EscortNpc", StringComparison.Ordinal));
    }

    [Fact]
    public void QuestOrdersThatCannotPlayOutAreFlagged()
    {
        ProfileLoadResult result = ProfileLoader.Parse(
            """
            <Profile Name="x">
              <QuestOrder>
                <TurnIn QuestId="9002" Entry="8100" Map="0" X="1500" Y="-2500" Z="60" />
                <PickUp QuestId="9001" Entry="8100" Map="0" X="1500" Y="-2500" Z="60" />
                <PickUp QuestId="9001" Entry="8100" Map="0" X="1500" Y="-2500" Z="60" />
              </QuestOrder>
            </Profile>
            """);

        // All warnings: a profile meant to run after another one legitimately starts with a
        // turn-in, so the loader tells the author rather than refusing the file.
        Assert.True(result.Success, result.Describe());
        Assert.Contains(result.Warnings, issue => issue.Message.Contains("without this profile ever picking it up", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, issue => issue.Message.Contains("picked up more than once", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, issue => issue.Message.Contains("never handed in", StringComparison.Ordinal));
    }

    [Fact]
    public void BrokenXmlIsReportedWithItsLine()
    {
        ProfileLoadResult result = ProfileLoader.Parse("<Profile>\n  <QuestOrder>\n</Profile>");

        Assert.False(result.Success);
        Assert.Null(result.Profile);

        ProfileIssue issue = Assert.Single(result.Errors);
        Assert.Contains("not valid XML", issue.Message, StringComparison.Ordinal);
        Assert.True(issue.LineNumber > 0);
    }

    [Fact]
    public void AnHonorbuddyProfileIsRecognisedAndPointedAtTheImporter()
    {
        ProfileLoadResult result = ProfileLoader.Parse("<HBProfile><Name>x</Name></HBProfile>");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, issue => issue.Message.Contains("importer", StringComparison.Ordinal));
    }

    [Fact]
    public void LevelAndFactionRangesAreChecked()
    {
        ProfileLoadResult tooNarrow = ProfileLoader.Parse("""<Profile Name="x" MinLevel="20" MaxLevel="10" />""");
        Assert.False(tooNarrow.Success);
        Assert.Contains(tooNarrow.Errors, issue => issue.Message.Contains("MinLevel", StringComparison.Ordinal));

        ProfileLoadResult badFaction = ProfileLoader.Parse("""<Profile Name="x" Faction="Scourge" />""");
        Assert.False(badFaction.Success);
        Assert.Contains(badFaction.Errors, issue => issue.Message.Contains("Scourge", StringComparison.Ordinal));

        ProfileLoadResult ok = ProfileLoader.Parse("""<Profile Name="x" Faction="Both" MinLevel="5" MaxLevel="15" />""");
        Assert.True(ok.Success, ok.Describe());
        Assert.Equal(ProfileFaction.Any, ok.Profile!.Faction);
        Assert.True(ok.Profile.SuitsLevel(5));
        Assert.False(ok.Profile.SuitsLevel(16));
        Assert.True(ok.Profile.SuitsFaction(ProfileFaction.Horde));
    }

    [Fact]
    public void AMissingFileIsAnErrorNotAnException()
    {
        ProfileLoadResult result = ProfileLoader.LoadFile(Path.Combine(Path.GetTempPath(), "no-such-profile.xml"));

        Assert.False(result.Success);
        Assert.Contains("no profile at", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AProfileRoundTripsThroughAFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"wowbuddy-{Guid.NewGuid():N}.xml");

        try
        {
            File.WriteAllText(path, Minimal);

            ProfileLoadResult result = ProfileLoader.LoadFile(path);

            Assert.True(result.Success, result.Describe());
            Assert.Equal(Path.GetFileName(path), result.Source);
            Assert.Equal(3, result.Profile!.Steps.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheReportNamesTheFileAndCountsTheProblems()
    {
        ProfileLoadResult result = ProfileLoader.Parse(
            """<Profile Name="x" Nonsense="1"><QuestOrder><RunTo Map="0" /></QuestOrder></Profile>""",
            "broken.xml");

        string report = result.Describe();

        Assert.Contains("broken.xml", report, StringComparison.Ordinal);
        Assert.Contains("1 error(s)", report, StringComparison.Ordinal);
        Assert.Contains("Nonsense", report, StringComparison.Ordinal);
    }
}
