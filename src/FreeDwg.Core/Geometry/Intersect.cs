namespace FreeDwg.Core.Geometry;

/// <summary>
/// Boolean "does this shape meet that rectangle" tests, for crossing
/// selection: a drag that touches any part of an object takes it.
/// </summary>
/// <remarks>
/// Every test here treats the shape as its outline. Whether the inside of a
/// closed shape also counts is the entity's decision -- a solid hatch says
/// yes, a circle says no -- and is applied on top of these.
/// </remarks>
public static class Intersect
{
    /// <summary>
    /// True when any part of the segment lies in the rectangle, including a
    /// segment that is wholly inside it. Liang-Barsky clipping, stopped as
    /// soon as the visible parameter range is known to be non-empty.
    /// </summary>
    public static bool SegmentWithRect(Vec2 a, Vec2 b, Bounds2 rect)
    {
        if (rect.IsEmpty) return false;

        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double t0 = 0.0, t1 = 1.0;

        if (!Clip(-dx, a.X - rect.MinX, ref t0, ref t1)) return false;
        if (!Clip(dx, rect.MaxX - a.X, ref t0, ref t1)) return false;
        if (!Clip(-dy, a.Y - rect.MinY, ref t0, ref t1)) return false;
        if (!Clip(dy, rect.MaxY - a.Y, ref t0, ref t1)) return false;

        return true;

        // p * t <= q for the whole surviving range, narrowing it as we go.
        static bool Clip(double p, double q, ref double t0, ref double t1)
        {
            if (p == 0) return q >= 0;   // parallel to this edge: in or out for good

            double r = q / p;
            if (p < 0)
            {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }
            return true;
        }
    }

    public static bool PolylineWithRect(IReadOnlyList<Vec2> points, bool closed, Bounds2 rect)
    {
        if (rect.IsEmpty || points.Count == 0) return false;
        if (points.Count == 1) return rect.Contains(points[0]);

        int segments = closed ? points.Count : points.Count - 1;
        for (int i = 0; i < segments; i++)
            if (SegmentWithRect(points[i], points[(i + 1) % points.Count], rect)) return true;

        return false;
    }

    /// <summary>
    /// True when the circle's outline meets the rectangle: the rectangle has
    /// to reach the circle without swallowing it whole.
    /// </summary>
    public static bool CircleWithRect(Vec2 center, double radius, Bounds2 rect)
    {
        if (rect.IsEmpty) return false;

        radius = Math.Abs(radius);
        if (Distance.PointToRect(center, rect) > radius) return false;

        // The whole rectangle inside the circle means the outline misses it.
        double farthest = 0;
        foreach (var corner in Distance.Corners(rect))
            farthest = Math.Max(farthest, Vec2.Distance(center, corner));

        return farthest >= radius;
    }

    /// <summary>
    /// True when a circular arc meets the rectangle. Approximated by
    /// flattening: the chord error stays a fraction of <paramref name="tolerance"/>,
    /// which is the pick radius, so it cannot change an answer the user could
    /// have aimed at deliberately.
    /// </summary>
    public static bool ArcWithRect(Vec2 center, double radius, double startAngle, double sweep,
        Bounds2 rect, double tolerance)
    {
        if (rect.IsEmpty || radius <= 0 || Math.Abs(sweep) < 1e-12) return false;
        if (!ArcMath.Bounds(center, radius, startAngle, sweep).Intersects(rect)) return false;
        if (Math.Abs(sweep) >= ArcMath.TwoPi - 1e-9) return CircleWithRect(center, radius, rect);

        var points = ArcMath.Tessellate(center, radius, startAngle, sweep,
            deviceRadius: Resolution.DeviceRadius(radius, tolerance));

        return PolylineWithRect(points, closed: false, rect);
    }

    /// <summary>True when <paramref name="inner"/> lies entirely within <paramref name="outer"/>.</summary>
    public static bool RectContainsRect(Bounds2 outer, Bounds2 inner)
    {
        if (outer.IsEmpty || inner.IsEmpty) return false;
        return inner.MinX >= outer.MinX && inner.MaxX <= outer.MaxX
            && inner.MinY >= outer.MinY && inner.MaxY <= outer.MaxY;
    }
}

/// <summary>
/// Turns a pick tolerance into the sampling density the tessellators want.
/// </summary>
/// <remarks>
/// Curves re-sample themselves against pixels per unit, because on screen the
/// thing to stay under is a fraction of a pixel. Picking has no pixels -- it
/// has a tolerance in world units, which plays exactly the same role: sample
/// finely enough that the flattening error is well inside the radius the user
/// is allowed to miss by.
/// </remarks>
public static class Resolution
{
    public static double UnitsToSamples(double tolerance) =>
        tolerance > 0 ? 1.0 / tolerance : 1e6;

    public static double DeviceRadius(double radius, double tolerance) =>
        radius * UnitsToSamples(tolerance);
}
