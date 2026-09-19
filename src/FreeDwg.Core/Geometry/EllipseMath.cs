namespace FreeDwg.Core.Geometry;

/// <summary>
/// Ellipse helpers. DWG parameterises an ellipse by its major-axis vector and
/// a minor/major ratio, and the start and end values are curve parameters, not
/// angles -- on a squashed ellipse the two differ noticeably.
/// </summary>
public static class EllipseMath
{
    /// <summary>The minor-axis vector implied by a major axis and a ratio.</summary>
    public static Vec2 MinorAxis(Vec2 majorAxis, double ratio) => majorAxis.Perp * ratio;

    public static Vec2 PointAt(Vec2 center, Vec2 majorAxis, double ratio, double parameter)
    {
        Vec2 minor = MinorAxis(majorAxis, ratio);
        return center + majorAxis * Math.Cos(parameter) + minor * Math.Sin(parameter);
    }

    /// <summary>
    /// Exact bounds of an elliptical arc. Each axis of a rotated ellipse
    /// reaches its extreme at a parameter that is not generally 0, pi/2, pi or
    /// 3pi/2, so the stationary points are solved for rather than sampled.
    /// </summary>
    public static Bounds2 Bounds(Vec2 center, Vec2 majorAxis, double ratio, double start, double sweep)
    {
        Vec2 minor = MinorAxis(majorAxis, ratio);

        var bounds = Bounds2.FromCorners(
            PointAt(center, majorAxis, ratio, start),
            PointAt(center, majorAxis, ratio, start + sweep));

        // x(t) = cx + Mx cos t + mx sin t is stationary where tan t = mx / Mx.
        foreach (double stationary in new[]
                 {
                     Math.Atan2(minor.X, majorAxis.X),
                     Math.Atan2(minor.Y, majorAxis.Y),
                 })
        {
            foreach (double t in new[] { stationary, stationary + Math.PI })
                if (ArcMath.Contains(t, start, sweep))
                    bounds = bounds.Union(PointAt(center, majorAxis, ratio, t));
        }

        return bounds;
    }

    /// <summary>
    /// Approximates an elliptical arc with a polyline fine enough to look
    /// smooth at the given zoom.
    /// </summary>
    public static Vec2[] Tessellate(Vec2 center, Vec2 majorAxis, double ratio,
        double start, double sweep, double pixelsPerUnit, double tolerancePixels = 0.25)
    {
        // Curvature is worst at the ends of the major axis, so size the step
        // from the larger radius and use it everywhere.
        double deviceRadius = majorAxis.Length * Math.Max(1.0, ratio) * pixelsPerUnit;

        double maxStep = deviceRadius > tolerancePixels
            ? 2.0 * Math.Acos(Math.Clamp(1.0 - tolerancePixels / deviceRadius, -1.0, 1.0))
            : Math.PI / 2;

        int segments = Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / Math.Max(maxStep, 1e-6)), 4, 8192);

        var points = new Vec2[segments + 1];
        for (int i = 0; i <= segments; i++)
            points[i] = PointAt(center, majorAxis, ratio, start + sweep * i / segments);
        return points;
    }
}
