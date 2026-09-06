namespace WoWBuddy.Profiles;

/// <summary>How badly wrong something in a profile is.</summary>
public enum ProfileIssueSeverity
{
    /// <summary>The profile will run, but not the way its author probably meant.</summary>
    Warning,

    /// <summary>The profile cannot be run as written.</summary>
    Error,
}

/// <summary>Something the loader found in a profile.</summary>
/// <param name="Severity">How badly wrong it is.</param>
/// <param name="Message">A sentence naming the problem and, where possible, the fix.</param>
/// <param name="LineNumber">The line it was written on, or 0 when unknown.</param>
/// <param name="Element">The element it was found in, for messages.</param>
public sealed record ProfileIssue(
    ProfileIssueSeverity Severity,
    string Message,
    int LineNumber = 0,
    string Element = "")
{
    public override string ToString()
    {
        string where = LineNumber > 0
            ? $"line {LineNumber}"
            : "profile";

        string what = string.IsNullOrEmpty(Element)
            ? where
            : $"{where}, <{Element}>";

        return $"{Severity}: {what}: {Message}";
    }
}

/// <summary>What loading a profile produced.</summary>
/// <param name="Profile">The profile, or null when it could not be loaded at all.</param>
/// <param name="Issues">Everything the loader found, in the order it found it.</param>
/// <param name="Source">Where the profile was read from.</param>
public sealed record ProfileLoadResult(
    Profile? Profile,
    IReadOnlyList<ProfileIssue> Issues,
    string Source)
{
    /// <summary>True when a profile came back and nothing was an error.</summary>
    public bool Success => Profile is not null && !HasErrors;

    /// <summary>True when at least one issue stops the profile running.</summary>
    public bool HasErrors => Issues.Any(issue => issue.Severity == ProfileIssueSeverity.Error);

    /// <summary>Only the issues that stop the profile running.</summary>
    public IEnumerable<ProfileIssue> Errors =>
        Issues.Where(issue => issue.Severity == ProfileIssueSeverity.Error);

    /// <summary>Only the issues worth mentioning but not fatal.</summary>
    public IEnumerable<ProfileIssue> Warnings =>
        Issues.Where(issue => issue.Severity == ProfileIssueSeverity.Warning);

    /// <summary>A report a user can read, one issue per line.</summary>
    public string Describe()
    {
        int errors = Errors.Count();
        int warnings = Warnings.Count();

        List<string> lines =
        [
            $"{Source}: {errors} error(s), {warnings} warning(s)."
        ];

        lines.AddRange(Issues.Select(issue => "  " + issue));

        return string.Join(Environment.NewLine, lines);
    }
}
