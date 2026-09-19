namespace FreeDwg.Core.Geometry;

/// <summary>
/// A 2D affine transform, stored as the six significant cells of a 3x3 matrix.
/// Row-vector convention, matching System.Windows.Media.Matrix:
/// <code>
///     [x' y' 1] = [x y 1] * | M11 M12 0 |
///                           | M21 M22 0 |
///                           | OX  OY  1 |
/// </code>
/// So <c>a * b</c> means "apply a, then b".
/// </summary>
public readonly struct Mat3
{
    public readonly double M11, M12, M21, M22, OffsetX, OffsetY;

    public Mat3(double m11, double m12, double m21, double m22, double offsetX, double offsetY)
    {
        M11 = m11; M12 = m12; M21 = m21; M22 = m22; OffsetX = offsetX; OffsetY = offsetY;
    }

    public static readonly Mat3 Identity = new(1, 0, 0, 1, 0, 0);

    public static Mat3 Translation(double dx, double dy) => new(1, 0, 0, 1, dx, dy);
    public static Mat3 Translation(Vec2 d) => new(1, 0, 0, 1, d.X, d.Y);
    public static Mat3 Scaling(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);
    public static Mat3 Scaling(double s) => new(s, 0, 0, s, 0, 0);

    public static Mat3 Rotation(double radians)
    {
        double c = Math.Cos(radians), s = Math.Sin(radians);
        return new(c, s, -s, c, 0, 0);
    }

    /// <summary>Rotation about an arbitrary world point.</summary>
    public static Mat3 RotationAbout(double radians, Vec2 origin) =>
        Translation(-origin) * Rotation(radians) * Translation(origin);

    /// <summary>Applies <paramref name="a"/> first, then <paramref name="b"/>.</summary>
    public static Mat3 operator *(Mat3 a, Mat3 b) => new(
        a.M11 * b.M11 + a.M12 * b.M21,
        a.M11 * b.M12 + a.M12 * b.M22,
        a.M21 * b.M11 + a.M22 * b.M21,
        a.M21 * b.M12 + a.M22 * b.M22,
        a.OffsetX * b.M11 + a.OffsetY * b.M21 + b.OffsetX,
        a.OffsetX * b.M12 + a.OffsetY * b.M22 + b.OffsetY);

    public double Determinant => M11 * M22 - M12 * M21;

    /// <summary>Transforms a point (translation applies).</summary>
    public Vec2 Transform(Vec2 p) => new(
        p.X * M11 + p.Y * M21 + OffsetX,
        p.X * M12 + p.Y * M22 + OffsetY);

    /// <summary>Transforms a direction (translation does not apply).</summary>
    public Vec2 TransformVector(Vec2 v) => new(
        v.X * M11 + v.Y * M21,
        v.X * M12 + v.Y * M22);

    /// <summary>
    /// True when the transform is a uniform scale, rotation and/or mirror --
    /// the cases under which a circle stays a circle and an arc stays an arc.
    /// </summary>
    public bool IsSimilarity
    {
        get
        {
            double rowDot = M11 * M21 + M12 * M22;
            double len1 = M11 * M11 + M12 * M12;
            double len2 = M21 * M21 + M22 * M22;
            double scale = Math.Max(len1, len2);
            if (scale <= 0) return false;
            return Math.Abs(rowDot) <= 1e-9 * scale && Math.Abs(len1 - len2) <= 1e-9 * scale;
        }
    }

    /// <summary>Uniform scale factor, meaningful when <see cref="IsSimilarity"/>.</summary>
    public double UniformScale => Math.Sqrt(Math.Abs(Determinant));

    /// <summary>Axis-aligned bounds of the transformed rectangle.</summary>
    public Bounds2 TransformBounds(Bounds2 bounds)
    {
        if (bounds.IsEmpty) return bounds;

        // Rotation means the transformed corners, not the transformed extremes.
        return Bounds2.Empty
            .Union(Transform(new Vec2(bounds.MinX, bounds.MinY)))
            .Union(Transform(new Vec2(bounds.MaxX, bounds.MinY)))
            .Union(Transform(new Vec2(bounds.MaxX, bounds.MaxY)))
            .Union(Transform(new Vec2(bounds.MinX, bounds.MaxY)));
    }

    public bool TryInvert(out Mat3 inverse)
    {
        double det = Determinant;
        if (Math.Abs(det) < 1e-300) { inverse = Identity; return false; }
        double inv = 1.0 / det;
        double m11 = M22 * inv;
        double m12 = -M12 * inv;
        double m21 = -M21 * inv;
        double m22 = M11 * inv;
        inverse = new(m11, m12, m21, m22,
            -(OffsetX * m11 + OffsetY * m21),
            -(OffsetX * m12 + OffsetY * m22));
        return true;
    }

    public override string ToString() =>
        $"[{M11:0.###} {M12:0.###}; {M21:0.###} {M22:0.###}; {OffsetX:0.###} {OffsetY:0.###}]";
}
