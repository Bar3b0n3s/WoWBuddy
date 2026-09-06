using System.Text;

namespace WoWBuddy.GameApi.Capabilities;

/// <summary>
/// What the attached client can and cannot tell the bot.
/// </summary>
/// <remarks>
/// The same posture as the offset verification report, applied to Lua. A great deal of what the
/// bot needs has no memory offset this project has verified — the quest log, the party, bag
/// contents, the battleground queue — so it comes through the client's own scripting. Which of
/// those calls exist is a fact about the client, and it can be established in a second at attach
/// time rather than assumed and discovered at three in the morning.
/// </remarks>
public sealed class CapabilityReport
{
    private readonly Dictionary<GameCapability, CapabilityResult> _results = [];

    /// <summary>Records what one probe found.</summary>
    public void Add(CapabilityResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _results[result.Capability] = result;
    }

    /// <summary>Everything that was asked, in the order the enum declares it.</summary>
    public IReadOnlyList<CapabilityResult> Results =>
        [.. _results.Values.OrderBy(result => result.Capability)];

    /// <summary>True when the client can do this.</summary>
    /// <remarks>
    /// A capability never asked about counts as unavailable. Assuming otherwise would make a
    /// feature work by accident on a client that was never checked.
    /// </remarks>
    public bool Supports(GameCapability capability) =>
        _results.TryGetValue(capability, out CapabilityResult? result) && result.Available;

    /// <summary>Why a capability is unavailable, for a message the user can act on.</summary>
    public string Explain(GameCapability capability)
    {
        if (!_results.TryGetValue(capability, out CapabilityResult? result))
        {
            return $"{capability} was never checked against this client.";
        }

        if (result.Available)
        {
            return string.Empty;
        }

        string missing = string.Join(", ", result.Missing);

        return result.Probe.ExpectedAbsent
            ? $"{capability} is not available on this client ({missing} does not exist), which is "
                + $"a known limitation of 3.3.5a. {result.Probe.Consequence}"
            : $"{capability} is unavailable: this client has no {missing}. {result.Probe.Consequence}";
    }

    /// <summary>Capabilities that are missing and were expected to be there.</summary>
    public IReadOnlyList<CapabilityResult> Surprises =>
        [.. Results.Where(result => result.IsSurprise)];

    /// <summary>Capabilities that are missing and were known to be.</summary>
    public IReadOnlyList<CapabilityResult> KnownGaps =>
        [.. Results.Where(result => !result.Available && result.Probe.ExpectedAbsent)];

    /// <summary>True when everything the bot expected to find is there.</summary>
    public bool NothingUnexpected => Surprises.Count == 0;

    /// <summary>A report a user can read.</summary>
    public string Describe()
    {
        StringBuilder text = new();

        text.AppendLine($"Client capabilities: {Results.Count(r => r.Available)} of {Results.Count} available.");
        text.AppendLine();

        foreach (CapabilityResult result in Results)
        {
            string mark = result.Available ? "yes" : result.Probe.ExpectedAbsent ? "no (known)" : "NO";

            text.AppendLine($"  {result.Capability,-18} {mark,-11} {result.Probe.Purpose}");

            if (!result.Available)
            {
                text.AppendLine($"  {string.Empty,-18} {string.Empty,-11} missing: {string.Join(", ", result.Missing)}");
                text.AppendLine($"  {string.Empty,-18} {string.Empty,-11} {result.Probe.Consequence}");
            }
        }

        if (Surprises.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(
                "Something is missing that this bot expected to find. That usually means the "
                + "client is not the build it claims to be, in which case the offset table is "
                + "suspect too — check the offset verification report before running anything.");
        }

        return text.ToString();
    }

    public override string ToString() => Describe();
}
