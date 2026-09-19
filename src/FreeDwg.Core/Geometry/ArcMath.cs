namespace FreeDwg.Core.Geometry;

/// <summary>
/// Circular-arc helpers. Angles are radians, counter-clockwise from +X, in
/// world space. A positive sweep is counter-clockwise.
/// </summary>
public static class ArcMath
{
    public const double TwoPi = Math.PI * 2.0;

    /// <summary>Wraps an angle into [0, 2pi).</summary>
    public static double Normalize(double angle)
    {
        double a = angle % TwoPi;
        return a < 0 ? a + TwoPi : a;
    }

    public static Vec2 PointAt(Vec2 center, double radius, double angle) =>
        new(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));

    /// <summary>True if <paramref name="angle"/> lies on the arc described by start + sweep.</summary>
    public static bool Contains(double angle, double startAngle, double sweep)
    {
        double d = Normalize(angle - startAngle);
        return sweep >= 0
            ? d <= sweep + 1e-12
            : d - TwoPi >= sweep - 1e-12;
    }

    /// <summary>
    /// Tight bounds for an arc: the two endpoints, plus whichever of the four
    /// axis extremes the sweep actually crosses.
    /// </summary>
    public static Bounds2 Bounds(Vec2 center, double radius, double startAngle, double sweep)
    {
        var b = Bounds2.FromCorners(
            PointAt(center, radius, startAngle),
            PointAt(center, radius, startAngle + sweep));

        for (int quadrant = 0; quadrant < 4; quadrant++)
        {
            double a = quadrant * (Math.PI / 2);
            if (Contains(a, startAngle, sweep))
                b = b.Union(PointAt(center, radius, a));
        }
        return b;
    }

    /// <summary>
    /// Approximates an arc with a polyline fine enough that the deviation stays
    /// under <paramref name="toleranceDevice"/> pixels. Needed when a block's
    /// transform is not a similarity: a circle scaled non-uniformly is an
    /// ellipse, which no circular-arc primitive can express.
    /// </summary>
    public static Vec2[] Tessellate(Vec2 center, double radius, double startAngle, double sweep,
        double deviceRadius, double toleranceDevice = 0.25)
    {
        // Sagitta of a chord subtending angle t is r * (1 - cos(t/2)); invert
        // that for the largest step that stays within tolerance.
        double maxStep = deviceRadius > toleranceDevice
            ? 2.0 * Math.Acos(Math.Clamp(1.0 - toleranceDevice / deviceRadius, -1.0, 1.0))
            : Math.PI / 2;

        int segments = Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / Math.Max(maxStep, 1e-6)), 2, 4096);

        var points = new Vec2[segments + 1];
        for (int i = 0; i <= segments; i++)
            points[i] = PointAt(center, radius, startAngle + sweep * i / segments);
        return points;
    }

    /// <summary>
    /// Converts a DWG bulge-encoded polyline segment into arc geometry.
    /// Bulge is tan(sweep / 4): positive is counter-clockwise, 1.0 is a
    /// semicircle, 0.0 is a straight segment.
    /// </summary>
    public static (Vec2 Center, double Radius, double StartAngle, double Sweep) FromBulge(
        Vec2 start, Vec2 end, double bulge)
    {
        Vec2 chord = end - start;
        double chordLength = chord.Length;
        if (chordLength < 1e-12 || Math.Abs(bulge) < 1e-12)
            return (Vec2.Lerp(start, end, 0.5), 0, 0, 0);

        // Signed sagitta and signed radius; the sign carries the arc direction.
        double sagitta = bulge * chordLength / 2.0;
        double signedRadius = (chordLength * chordLength / 4.0 + sagitta * sagitta) / (2.0 * sagitta);

        Vec2 mid = Vec2.Lerp(start, end, 0.5);
        Vec2 center = mid + chord.Normalized().Perp * (signedRadius - sagitta);

        double radius = Math.Abs(signedRadius);
        double startAngle = (start - center).Angle();
        double sweep = 4.0 * Math.Atan(bulge);
        return (center, radius, startAngle, sweep);
    }

    /// <summary>
    /// The circle through three points, or false when they are collinear.
    /// </summary>
    /// <remarks>
    /// The centre is where the perpendicular bisectors of two of the chords
    /// meet, which is the determinant below. Drafting asks for this whenever
    /// an arc is given by where it starts, where it passes and where it ends.
    /// </remarks>
    public static bool TryCircleThrough(Vec2 a, Vec2 b, Vec2 c, out Vec2 center, out double radius)
    {
        double area2 = Vec2.Cross(b - a, c - a);

        // Collinear, or two points on top of each other: no finite circle.
        if (Math.Abs(area2) < 1e-12)
        {
            center = Vec2.Lerp(a, c, 0.5);
            radius = 0;
            return false;
        }

        double aa = a.LengthSquared, bb = b.LengthSquared, cc = c.LengthSquared;

        center = new Vec2(
            (aa * (b.Y - c.Y) + bb * (c.Y - a.Y) + cc * (a.Y - b.Y)) / (2 * area2),
            (aa * (c.X - b.X) + bb * (a.X - c.X) + cc * (b.X - a.X)) / (2 * area2));

        radius = Vec2.Distance(center, a);
        return true;
    }

    /// <summary>
    /// The arc that starts at <paramref name="start"/>, passes through
    /// <paramref name="through"/> and ends at <paramref name="end"/>.
    /// </summary>
    /// <remarks>
    /// The middle point is what picks the direction: of the two ways round
    /// from start to end, the arc takes whichever one it lies on.
    /// </remarks>
    public static bool TryArcThrough(Vec2 start, Vec2 through, Vec2 end,
        out Vec2 center, out double radius, out double startAngle, out double sweep)
    {
        startAngle = 0;
        sweep = 0;

        if (!TryCircleThrough(start, through, end, out center, out radius)) return false;

        startAngle = (start - center).Angle();

        double toEnd = Normalize((end - center).Angle() - startAngle);
        double toThrough = Normalize((through - center).Angle() - startAngle);

        // Counter-clockwise if the middle point comes before the end going
        // that way; otherwise the same arc the other way round, so negative.
        sweep = toThrough <= toEnd ? toEnd : toEnd - TwoPi;
        return Math.Abs(sweep) > 1e-12;
    }

    /// <summary>
    /// The parameters a path sink needs to stroke a bulge segment, without
    /// materialising the centre: |bulge| &gt; 1 means the arc is the long way round.
    /// </summary>
    public static (double Radius, bool LargeArc, bool Clockwise) BulgeToArcTo(
        Vec2 start, Vec2 end, double bulge)
    {
        double chordLength = Vec2.Distance(start, end);
        double sagitta = bulge * chordLength / 2.0;
        double radius = Math.Abs((chordLength * chordLength / 4.0 + sagitta * sagitta) / (2.0 * sagitta));
        return (radius, Math.Abs(bulge) > 1.0, bulge < 0);
    }
}
