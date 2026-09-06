using System.Globalization;

namespace WoWBuddy.Common.Geometry;

/// <summary>
/// A position in WoW world space.
/// </summary>
/// <remarks>
/// <para>
/// The client stores positions as three single-precision floats in the order X, Y, Z.
/// In WoW's coordinate system X is north, Y is west and Z is up; a unit's facing is a
/// separate radian value measured counter-clockwise from the +X axis. That means the
/// "compass" plane for pathing and distance checks is X/Y, and Z is only interesting
/// for line-of-sight, water and fall damage. <see cref="Distance2D"/> exists because
/// almost every gameplay range check (spell range, interaction range, node proximity)
/// wants the flat distance, not the true 3D one.
/// </para>
/// </remarks>
public readonly record struct Vector3(float X, float Y, float Z)
{
    /// <summary>The origin. Also the value the client reports for objects that have no position yet.</summary>
    public static readonly Vector3 Zero = new(0f, 0f, 0f);

    /// <summary>True when every component is finite (not NaN, not infinity).</summary>
    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);

    /// <summary>True when this is exactly the origin, which the client uses as "unset".</summary>
    public bool IsZero => X == 0f && Y == 0f && Z == 0f;

    /// <summary>Full three-dimensional distance to <paramref name="other"/>.</summary>
    public float Distance(Vector3 other)
    {
        float dx = X - other.X;
        float dy = Y - other.Y;
        float dz = Z - other.Z;
        return MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    /// <summary>
    /// Distance ignoring height. This is the one to use for gameplay range checks.
    /// </summary>
    public float Distance2D(Vector3 other)
    {
        float dx = X - other.X;
        float dy = Y - other.Y;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>Squared 3D distance. Prefer this when only comparing distances.</summary>
    public float DistanceSquared(Vector3 other)
    {
        float dx = X - other.X;
        float dy = Y - other.Y;
        float dz = Z - other.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    /// <summary>Length of this vector treated as a displacement from the origin.</summary>
    public float Length => MathF.Sqrt((X * X) + (Y * Y) + (Z * Z));

    /// <summary>
    /// The facing, in radians, that a unit standing at this position would need in order
    /// to look at <paramref name="target"/>. Normalised to <c>[0, 2*pi)</c> to match the
    /// range the client itself stores.
    /// </summary>
    public float FacingTo(Vector3 target)
    {
        float angle = MathF.Atan2(target.Y - Y, target.X - X);
        return NormaliseAngle(angle);
    }

    /// <summary>Wraps an arbitrary radian value into <c>[0, 2*pi)</c>.</summary>
    public static float NormaliseAngle(float radians)
    {
        const float twoPi = MathF.PI * 2f;
        radians %= twoPi;
        return radians < 0f ? radians + twoPi : radians;
    }

    /// <summary>
    /// Smallest absolute difference between two facings, accounting for the wrap at 2*pi.
    /// Always in <c>[0, pi]</c>.
    /// </summary>
    public static float AngleDelta(float a, float b)
    {
        const float twoPi = MathF.PI * 2f;
        float delta = MathF.Abs(NormaliseAngle(a) - NormaliseAngle(b));
        return delta > MathF.PI ? twoPi - delta : delta;
    }

    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vector3 operator *(Vector3 a, float scalar) => new(a.X * scalar, a.Y * scalar, a.Z * scalar);

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X:F2}, {Y:F2}, {Z:F2})");
}
