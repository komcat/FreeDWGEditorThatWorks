namespace FreeDwg.Core.Geometry;

/// <summary>
/// A straight run or a circular arc: the two shapes every curve in the model
/// reduces to, exactly for lines, arcs, circles and polylines, and by
/// flattening for ellipses and splines.
/// </summary>
/// <remarks>
/// Intersection is a binary operation, and double dispatch over ten entity
/// types would be a hundred cases. Reducing everything to two primitives
/// first turns that into three: segment-segment, segment-arc and arc-arc.
/// It is also how real CAD kernels do it, for the same reason.
/// </remarks>
public readonly struct CurvePiece
{
    public readonly Vec2 A;
    public readonly Vec2 B;

    /// <summary>Zero for a straight run.</summary>
    public readonly double Radius;

    public readonly Vec2 Center;
    public readonly double StartAngle;
    public readonly double Sweep;

    private CurvePiece(Vec2 a, Vec2 b, Vec2 center, double radius, double startAngle, double sweep)
    {
        A = a; B = b; Center = center; Radius = radius; StartAngle = startAngle; Sweep = sweep;
    }

    public static CurvePiece Segment(Vec2 a, Vec2 b) => new(a, b, Vec2.Zero, 0, 0, 0);

    public static CurvePiece Arc(Vec2 center, double radius, double startAngle, double sweep) => new(
        ArcMath.PointAt(center, radius, startAngle),
        ArcMath.PointAt(center, radius, startAngle + sweep),
        center, radius, startAngle, sweep);

    public static CurvePiece Circle(Vec2 center, double radius) =>
        Arc(center, radius, 0, ArcMath.TwoPi);

    public bool IsArc => Radius > 0;

    public bool IsDegenerate => IsArc
        ? Math.Abs(Sweep) < 1e-12
        : (B - A).LengthSquared < 1e-24;

    public Bounds2 Bounds => IsArc
        ? ArcMath.Bounds(Center, Radius, StartAngle, Sweep)
        : Bounds2.FromCorners(A, B);

    /// <summary>True when <paramref name="angle"/> is within an arc's sweep.</summary>
    public bool Covers(double angle) => !IsArc || ArcMath.Contains(angle, StartAngle, Sweep);

    /// <summary>
    /// True when a point known to be on this piece's infinite line or full
    /// circle is also on the piece itself.
    /// </summary>
    public bool Contains(Vec2 point, double tolerance = 1e-9)
    {
        if (IsArc) return Covers((point - Center).Angle());

        Vec2 run = B - A;
        double lengthSquared = run.LengthSquared;
        if (lengthSquared < 1e-24) return Vec2.Distance(point, A) <= tolerance;

        double t = Vec2.Dot(point - A, run) / lengthSquared;
        double slack = tolerance / Math.Sqrt(lengthSquared);
        return t >= -slack && t <= 1 + slack;
    }

    public override string ToString() => IsArc
        ? $"arc {Center} r{Radius:0.###} {StartAngle:0.###}+{Sweep:0.###}"
        : $"segment {A}..{B}";
}
