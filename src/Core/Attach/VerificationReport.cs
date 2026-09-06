using System.Text;

namespace WoWBuddy.Core.Attach;

/// <summary>Outcome of a single attach-time check.</summary>
public enum CheckStatus
{
    /// <summary>The check confirmed what it set out to confirm.</summary>
    Passed,

    /// <summary>The check could not run, usually because an earlier one failed.</summary>
    Skipped,

    /// <summary>Something is off but the bot can still run correctly.</summary>
    Warning,

    /// <summary>The client does not match the offset table. Attach must not proceed.</summary>
    Failed,
}

/// <summary>One attach-time check and what it found.</summary>
/// <param name="Name">Short name of the check.</param>
/// <param name="Status">Its outcome.</param>
/// <param name="Detail">What was actually observed, in enough detail to debug from.</param>
public readonly record struct VerificationCheck(string Name, CheckStatus Status, string Detail)
{
    public override string ToString() => $"[{Status,-7}] {Name}: {Detail}";
}

/// <summary>
/// The full result of verifying the offset table against a live client.
/// </summary>
/// <remarks>
/// <para>
/// This exists because an offset table is a claim about someone else's binary, and the only
/// honest way to hold such a claim is to test it. Every anchor the bot depends on is checked
/// against the running client before a single behaviour is allowed to run, and a failed check
/// stops attach outright rather than degrading quietly.
/// </para>
/// </remarks>
public sealed class VerificationReport
{
    private readonly List<VerificationCheck> _checks = [];

    /// <summary>All checks in the order they ran.</summary>
    public IReadOnlyList<VerificationCheck> Checks => _checks;

    /// <summary>True when nothing failed. Warnings do not block attach.</summary>
    public bool Passed => !_checks.Any(c => c.Status == CheckStatus.Failed);

    /// <summary>True when at least one check raised a warning.</summary>
    public bool HasWarnings => _checks.Any(c => c.Status == CheckStatus.Warning);

    internal void Add(string name, CheckStatus status, string detail) =>
        _checks.Add(new VerificationCheck(name, status, detail));

    internal void Pass(string name, string detail) => Add(name, CheckStatus.Passed, detail);

    internal void Fail(string name, string detail) => Add(name, CheckStatus.Failed, detail);

    internal void Warn(string name, string detail) => Add(name, CheckStatus.Warning, detail);

    internal void Skip(string name, string detail) => Add(name, CheckStatus.Skipped, detail);

    /// <summary>The first failure, or null when everything passed.</summary>
    public VerificationCheck? FirstFailure =>
        _checks.Where(c => c.Status == CheckStatus.Failed).Cast<VerificationCheck?>().FirstOrDefault();

    /// <summary>Renders the report as a block of text for the log or the UI.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.AppendLine(Passed
            ? "Offset verification passed."
            : "Offset verification FAILED. The bot will not attach.");

        foreach (VerificationCheck check in _checks)
        {
            builder.Append("  ").AppendLine(check.ToString());
        }

        return builder.ToString();
    }
}
