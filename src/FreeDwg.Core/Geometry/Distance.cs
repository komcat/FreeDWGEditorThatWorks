namespace FreeDwg.Core.Geometry;

/// <summary>
/// Closest-approach distances from a point to the shapes the scene is made
/// of. All in world units, all unsigned: a point on the shape is at zero.
/// </summary>
/// <remarks>
/// This is what picking is built on. Distances are to the geometry as it is
/// <em>drawn</em>, so an unfilled shape is measured to its outline -- clicking
/// the middle of a circle does not select the circle, exactly as it does not
/// in AutoCAD.
/// </remarks>
public static class Distance
{
    public static Vec2 ClosestOnSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        Vec2 ab = b - a;
        double lengthSquared = ab.LengthSquared;
        if (lengthSquared < 1e-300) return a;

        double t = Math.Clamp(Vec2.Dot(p - a, ab) / lengthSquared, 0.0, 1.0);
        return a + ab * t;
    }

    public static double PointToSegment(Vec2 p, Vec2 a, Vec2 b) =>
        Vec2.Distance(p, ClosestOnSegment(p, a, b));

    public static double PointToPolyline(Vec2 p, IReadOnlyList<Vec2> points, bool closed)
    {
        if (points.Count == 0) return double.PositiveInfinity;
        if (points.Count == 1) return Vec2.Distance(p, points[0]);

        double best = double.PositiveInfinity;
        int segments = closed ? points.Count : points.Count - 1;

        for (int i = 0; i < segments; i++)
        {
            double d = PointToSegment(p, points[i], points[(i + 1) % points.Count]);
            if (d < best) best = d;
        }
        return best;
    }

    /// <summary>Distance to the circle's outline, not to the disc it encloses.</summary>
    public static double PointToCircle(Vec2 p, Vec2 center, double radius) =>
        Math.Abs(Vec2.Distance(p, center) - Math.Abs(radius));

    /// <summary>
    /// Distance to a circular arc: radial where the point is within the sweep,
    /// and to the nearer endpoint where it is not.
    /// </summary>
    public static double PointToArc(Vec2 p, Vec2 center, double radius, double startAngle, double sweep)
    {
        radius = Math.Abs(radius);
        if (radius < 1e-300) return Vec2.Distance(p, center);
        if (Math.Abs(sweep) >= ArcMath.TwoPi - 1e-9) return PointToCircle(p, center, radius);

        Vec2 offset = p - center;

        // Dead centre: every angle is equally close, so the arc is at radius
        // in whichever direction, and Atan2(0, 0) would pick one arbitrarily.
        if (offset.LengthSquared < 1e-300) return radius;

        if (ArcMath.Contains(offset.Angle(), startAngle, sweep))
            return Math.Abs(offset.Length - radius);

        return Math.Min(
            Vec2.Distance(p, ArcMath.PointAt(center, radius, startAngle)),
            Vec2.Distance(p, ArcMath.PointAt(center, radius, startAngle + sweep)));
    }

    /// <summary>Distance to a filled rectangle: zero anywhere inside it.</summary>
    public static double PointToRect(Vec2 p, Bounds2 rect)
    {
        if (rect.IsEmpty) return double.PositiveInfinity;

        double dx = Math.Max(Math.Max(rect.MinX - p.X, p.X - rect.MaxX), 0.0);
        double dy = Math.Max(Math.Max(rect.MinY - p.Y, p.Y - rect.MaxY), 0.0);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Distance to a rectangle's outline, so the interior is not at zero.</summary>
    public static double PointToRectEdge(Vec2 p, Bounds2 rect)
    {
        if (rect.IsEmpty) return double.PositiveInfinity;

        var corners = Corners(rect);
        return PointToPolyline(p, corners, closed: true);
    }

    /// <summary>Corners of a rectangle, counter-clockwise from the minimum.</summary>
    public static Vec2[] Corners(Bounds2 rect) =>
    [
        new(rect.MinX, rect.MinY),
        new(rect.MaxX, rect.MinY),
        new(rect.MaxX, rect.MaxY),
        new(rect.MinX, rect.MaxY),
    ];

    /// <summary>Crossing-number test against one closed ring.</summary>
    public static bool PointInPolygon(Vec2 p, IReadOnlyList<Vec2> loop)
    {
        if (loop.Count < 3) return false;

        bool inside = false;
        for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
        {
            Vec2 a = loop[i], b = loop[j];

            // Half-open in Y so a vertex exactly at the ray is counted once.
            if (a.Y > p.Y == b.Y > p.Y) continue;

            double x = a.X + (p.Y - a.Y) / (b.Y - a.Y) * (b.X - a.X);
            if (p.X < x) inside = !inside;
        }
        return inside;
    }

    /// <summary>
    /// Even-odd test over a set of rings, which is how hatch islands are
    /// filled: a ring inside another reads as a hole.
    /// </summary>
    public static bool PointInLoops(Vec2 p, IReadOnlyList<IReadOnlyList<Vec2>> loops)
    {
        bool inside = false;
        foreach (var loop in loops)
            if (PointInPolygon(p, loop)) inside = !inside;
        return inside;
    }
}
