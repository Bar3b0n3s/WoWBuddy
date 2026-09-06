namespace WoWBuddy.BotBases.Support;

/// <summary>Getting somewhere faster than walking.</summary>
/// <remarks>
/// Mounting is worth doing and easy to get wrong: a mount cast is interrupted by damage and
/// cancelled by moving, so a bot that tries at the wrong moment spends the journey starting
/// casts it never finishes and arrives later than if it had walked.
/// </remarks>
public interface ITravel
{
    /// <summary>True when the character is on a mount.</summary>
    bool IsMounted { get; }

    /// <summary>True when the character could mount here.</summary>
    /// <remarks>
    /// Indoors, in combat, underwater, in a battleground before the gates open — the client
    /// refuses in all of them, and asking first is cheaper than a failed cast per tick.
    /// </remarks>
    bool CanMount { get; }

    /// <summary>Gets on a mount. False when the client refused.</summary>
    bool Mount();

    /// <summary>Gets off.</summary>
    bool Dismount();
}

/// <summary>When it is worth getting on a mount.</summary>
public sealed record TravelSettings
{
    /// <summary>The shortest journey worth mounting for, in yards.</summary>
    /// <remarks>
    /// Mounting costs a cast and dismounting costs nothing, so the break-even point is short —
    /// but not zero: mounting to cross a clearing and dismounting on the far side wastes more
    /// time than it saves, and looks precisely like a program doing arithmetic.
    /// </remarks>
    public float WorthMountingFor { get; init; } = 100f;

    /// <summary>Whether to mount at all.</summary>
    public bool Enabled { get; init; }
}
