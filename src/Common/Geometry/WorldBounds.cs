namespace WoWBuddy.Common.Geometry;

/// <summary>
/// Hard limits of the WoW world grid, used to sanity-check values read out of the client.
/// </summary>
/// <remarks>
/// <para>
/// A map is a 64x64 grid of 533.33333 yard tiles centred on the origin, so every legal
/// coordinate falls within +/- 32 * 533.33333 = 17066.666 yards. This is the single most
/// useful cheap check available when validating an offset: if a "position" read lands
/// outside this box, the pointer chain is wrong. Reading garbage is not itself dangerous,
/// but acting on garbage (walking to it, writing it into the click-to-move block) is, so
/// the object layer rejects out-of-range positions rather than passing them upward.
/// </para>
/// </remarks>
public static class WorldBounds
{
    /// <summary>Side length in yards of one map grid tile.</summary>
    public const float GridTileSize = 533.33333f;

    /// <summary>Number of grid tiles along one axis of a map.</summary>
    public const int GridTilesPerAxis = 64;

    /// <summary>Maximum absolute value of a legal X or Y coordinate.</summary>
    public const float MaxCoordinate = GridTileSize * (GridTilesPerAxis / 2);

    /// <summary>
    /// Generous vertical limit. The deepest and highest points in 3.3.5a content sit well
    /// inside this, so anything beyond it is a bad read rather than an exotic location.
    /// </summary>
    public const float MaxHeight = 20000f;

    /// <summary>
    /// True when <paramref name="position"/> could plausibly be a real world position.
    /// The origin counts as invalid: the client uses it for objects that are not yet placed.
    /// </summary>
    public static bool IsPlausible(Vector3 position)
    {
        if (!position.IsFinite || position.IsZero)
        {
            return false;
        }

        return MathF.Abs(position.X) <= MaxCoordinate
            && MathF.Abs(position.Y) <= MaxCoordinate
            && MathF.Abs(position.Z) <= MaxHeight;
    }
}
