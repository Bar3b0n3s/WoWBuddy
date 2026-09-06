namespace WoWBuddy.Core.Offsets;

/// <summary>
/// Records the provenance of a single offset constant.
/// </summary>
/// <remarks>
/// Applied to every constant in <see cref="Offsets335a"/> and <see cref="UpdateFields335a"/>.
/// A unit test asserts that no constant is missing one, so a value can never be added
/// without saying where it came from.
/// </remarks>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class OffsetInfoAttribute : Attribute
{
    public OffsetInfoAttribute(OffsetConfidence confidence, string source)
    {
        Confidence = confidence;
        Source = source;
    }

    /// <summary>How far this value can be trusted before a runtime check.</summary>
    public OffsetConfidence Confidence { get; }

    /// <summary>Where the value came from, in enough detail to re-check it.</summary>
    public string Source { get; }

    /// <summary>
    /// How to independently verify or re-derive the value: a pattern scan, a disassembly
    /// landmark, or the runtime check that validates it.
    /// </summary>
    public string HowToVerify { get; init; } = string.Empty;

    /// <summary>Conflicting values seen in other sources, if any.</summary>
    public string Conflicts { get; init; } = string.Empty;
}
