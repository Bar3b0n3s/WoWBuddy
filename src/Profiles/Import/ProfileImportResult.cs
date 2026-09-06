namespace WoWBuddy.Profiles.Import;

/// <summary>What converting a profile from another bot's format produced.</summary>
/// <param name="Profile">The converted profile, or null when nothing usable came out.</param>
/// <param name="ConvertedXml">
/// The conversion as a WoWBuddy profile, ready to save and edit. Present even when the import
/// is not clean: a file with ten steps to check by hand is worth far more than a refusal.
/// </param>
/// <param name="Issues">Everything the importer and the loader found, in order.</param>
/// <param name="Source">Where the original came from.</param>
/// <param name="StepsConverted">How many steps came across.</param>
/// <param name="StepsNotUnderstood">How many were skipped because the importer did not know them.</param>
/// <param name="ConditionsConverted">How many conditions were translated.</param>
/// <param name="ConditionsLost">
/// How many conditions could not be translated. Each one is a step that now runs when it used
/// to be gated, which is why any of these makes the import unclean.
/// </param>
public sealed record ProfileImportResult(
    Profile? Profile,
    string ConvertedXml,
    IReadOnlyList<ProfileIssue> Issues,
    string Source,
    int StepsConverted = 0,
    int StepsNotUnderstood = 0,
    int ConditionsConverted = 0,
    int ConditionsLost = 0)
{
    /// <summary>
    /// True when the whole profile came across and the result can be run as-is.
    /// </summary>
    /// <remarks>
    /// A lost condition counts against this even though the converted file still loads. A step
    /// that used to say "only below level 20" and now says nothing will do the wrong thing an
    /// hour into a session, so the importer will not call that clean.
    /// </remarks>
    public bool Success =>
        Profile is not null
        && StepsNotUnderstood == 0
        && ConditionsLost == 0
        && !Issues.Any(issue => issue.Severity == ProfileIssueSeverity.Error);

    /// <summary>True when a profile came out at all, clean or not.</summary>
    public bool ProducedSomething => Profile is not null;

    /// <summary>Only the issues that stop the converted profile loading.</summary>
    public IEnumerable<ProfileIssue> Errors =>
        Issues.Where(issue => issue.Severity == ProfileIssueSeverity.Error);

    /// <summary>Only the issues worth reading but not fatal.</summary>
    public IEnumerable<ProfileIssue> Warnings =>
        Issues.Where(issue => issue.Severity == ProfileIssueSeverity.Warning);

    /// <summary>A report a user can read before deciding whether to trust the conversion.</summary>
    public string Describe()
    {
        List<string> lines =
        [
            $"{Source}: converted {StepsConverted} step(s).",
        ];

        if (StepsNotUnderstood > 0)
        {
            lines.Add($"  {StepsNotUnderstood} step(s) were not understood and are missing from the result.");
        }

        if (ConditionsConverted > 0)
        {
            lines.Add($"  {ConditionsConverted} condition(s) were translated.");
        }

        if (ConditionsLost > 0)
        {
            lines.Add(
                $"  {ConditionsLost} condition(s) could not be translated. Those steps now run "
                + "unconditionally: check each one before using this profile.");
        }

        lines.Add(string.Empty);
        lines.AddRange(Issues.Select(issue => "  " + issue));
        lines.Add(string.Empty);

        lines.Add(Success
            ? "The whole profile came across."
            : "This conversion needs checking by hand before it is used.");

        return string.Join(Environment.NewLine, lines);
    }
}
