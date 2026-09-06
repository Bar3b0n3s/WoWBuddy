namespace WoWBuddy.Core.Offsets;

/// <summary>
/// How much a given offset is actually trusted.
/// </summary>
/// <remarks>
/// <para>
/// A wrong offset does not fail loudly. It silently returns a plausible-looking number,
/// and the bot then acts on it: walking to a garbage coordinate, or writing into a memory
/// block that is not the one it thinks it is. Every offset therefore records where its
/// value came from, and the attach sequence refuses to proceed on anything that has not
/// either been corroborated by independent sources or checked against the live client.
/// </para>
/// </remarks>
public enum OffsetConfidence
{
    /// <summary>
    /// No source. The value is a placeholder and must not be used. Anything marked this way
    /// carries a <c>TODO: verify</c> note describing how to find the real value.
    /// </summary>
    Unverified = 0,

    /// <summary>
    /// Published by exactly one source that could not be independently confirmed. Usable
    /// only behind a runtime check that would notice a bad value.
    /// </summary>
    SingleSource = 1,

    /// <summary>
    /// Sources disagree. The stored value is the majority reading; the alternatives are
    /// listed in the notes and resolved against the live client at attach time.
    /// </summary>
    Conflicted = 2,

    /// <summary>
    /// At least two independent public sources agree. Safe to use, still checked at attach.
    /// </summary>
    Corroborated = 3,

    /// <summary>
    /// Derived from the 3.3.5a server implementation's own field layout, which is the
    /// authoritative definition of what the client is being sent. The strongest evidence
    /// available without disassembling the client.
    /// </summary>
    ProtocolDefined = 4,
}
