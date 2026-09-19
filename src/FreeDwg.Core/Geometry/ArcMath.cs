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
