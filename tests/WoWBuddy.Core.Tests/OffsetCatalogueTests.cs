using WoWBuddy.Core.Offsets;
using Xunit;
using Xunit.Abstractions;

namespace WoWBuddy.Core.Tests;

/// <summary>
/// Guards the rule that no low-level fact enters the codebase without saying where it came
/// from.
/// </summary>
/// <remarks>
/// This is the test that enforces the project's central constraint. Adding an offset without
/// provenance breaks the build, which is the only way a rule like this survives contact with
/// a codebase that many people touch.
/// </remarks>
public sealed class OffsetCatalogueTests
{
    private readonly ITestOutputHelper _output;

    public OffsetCatalogueTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void EveryOffsetDeclaresWhereItCameFrom()
    {
        IReadOnlyList<CataloguedOffset> undocumented = OffsetCatalogue.Undocumented();

        Assert.True(
            undocumented.Count == 0,
            "These offsets have no [OffsetInfo] provenance. Every low-level constant must record " +
            "its source and how to verify it:" + Environment.NewLine +
            string.Join(Environment.NewLine, undocumented.Select(o => "  " + o.Path)));
    }

    [Fact]
    public void EveryOffsetExplainsHowToVerifyIt()
    {
        List<CataloguedOffset> missing = OffsetCatalogue.Enumerate()
            .Where(o => string.IsNullOrWhiteSpace(o.HowToVerify))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These offsets do not say how to check them against a real client:" + Environment.NewLine +
            string.Join(Environment.NewLine, missing.Select(o => "  " + o.Path)));
    }

    [Fact]
    public void UnverifiedOffsetsAreDeclaredAsSuch()
    {
        // Not a failure. Unverified values are allowed to exist, as long as they are labelled
        // and the code that uses them fails safe. This test exists to keep the list visible
        // in CI output rather than letting it quietly grow.
        IReadOnlyList<CataloguedOffset> needingWork = OffsetCatalogue.NeedingVerification();

        _output.WriteLine($"{needingWork.Count} offset(s) still need first-hand verification:");
        foreach (CataloguedOffset entry in needingWork)
        {
            _output.WriteLine($"  [{entry.Confidence}] {entry.Path} = {entry.Value}");
            _output.WriteLine($"      {entry.HowToVerify}");
        }

        Assert.All(needingWork, entry => Assert.False(string.IsNullOrWhiteSpace(entry.HowToVerify)));
    }

    [Fact]
    public void TheCoreAnchorsAreCorroboratedOrBetter()
    {
        // The pointer chain the whole bot depends on must never be built on a single source.
        string[] anchors =
        [
            "Offsets335a.ObjectManager.ClientConnection",
            "Offsets335a.ObjectManager.CurMgr",
            "Offsets335a.ObjectManager.FirstObject",
            "Offsets335a.ObjectManager.NextObject",
            "Offsets335a.ObjectManager.LocalPlayerGuid",
            "Offsets335a.Object.Type",
            "Offsets335a.Object.Guid",
        ];

        List<CataloguedOffset> catalogue = OffsetCatalogue.Enumerate().ToList();

        foreach (string anchor in anchors)
        {
            CataloguedOffset entry = Assert.Single(catalogue, c => c.Path == anchor);
            Assert.True(
                entry.Confidence >= OffsetConfidence.Corroborated,
                $"{anchor} is {entry.Confidence}; the core pointer chain requires at least Corroborated.");
        }
    }

    [Fact]
    public void MarkdownRenderingIncludesEveryOffset()
    {
        string markdown = OffsetCatalogue.ToMarkdown();

        foreach (CataloguedOffset entry in OffsetCatalogue.Enumerate())
        {
            Assert.Contains(entry.Path, markdown, StringComparison.Ordinal);
        }
    }
}
