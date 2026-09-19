namespace FreeDwg.Core.Geometry;

/// <summary>
/// Evaluation of a NURBS curve, which is what a DWG SPLINE is.
/// </summary>
/// <remarks>
/// This lives in Core rather than being delegated to the import library on
/// purpose: it is the one curve type with no closed form for the renderer to
/// fall back on, and keeping it here means the scene can be drawn, measured
/// and later edited without a DWG reader present.
/// </remarks>
public static class BSpline
{
    /// <summary>Roughly one sample per this many device pixels along the control polygon.</summary>
    private const double PixelsPerSample = 3.0;

    private const int MinSegments = 8;
    private const int MaxSegments = 8192;

    /// <summary>
    /// True when the knot vector is the size the degree and control point
    /// count require. A spline that fails this cannot be evaluated and is
    /// better drawn as its control polygon than not at all.
    /// </summary>
    public static bool IsValid(int controlPointCount, int knotCount, int degree) =>
        degree >= 1 && controlPointCount > degree && knotCount == controlPointCount + degree + 1;

    /// <summary>
    /// A point on the curve, by de Boor's algorithm. Weights may be null for a
    /// non-rational spline, which is the common case.
    /// </summary>
    public static Vec2 Evaluate(
        IReadOnlyList<Vec2> controlPoints,
        IReadOnlyList<double>? weights,
        IReadOnlyList<double> knots,
        int degree,
        double t)
    {
        int last = controlPoints.Count - 1;

        // The curve is only defined over the interior of the knot vector.
        double domainStart = knots[degree];
        double domainEnd = knots[last + 1];
        t = Math.Clamp(t, domainStart, domainEnd);

        int span = degree;
        while (span < last && t >= knots[span + 1]) span++;

        // Work in homogeneous coordinates so the rational case falls out of
        // the same recurrence as the polynomial one.
        var x = new double[degree + 1];
        var y = new double[degree + 1];
        var w = new double[degree + 1];

        for (int j = 0; j <= degree; j++)
        {
            int index = j + span - degree;
            double weight = weights is not null && index < weights.Count && weights[index] > 0
                ? weights[index]
                : 1.0;

            x[j] = controlPoints[index].X * weight;
            y[j] = controlPoints[index].Y * weight;
            w[j] = weight;
        }

        for (int r = 1; r <= degree; r++)
        {
            for (int j = degree; j >= r; j--)
            {
                int index = j + span - degree;
                double lower = knots[index];
                double upper = knots[index + degree + 1 - r];
                double denominator = upper - lower;

                // Repeated knots give a zero interval; the blend degenerates
                // to the right-hand point, which is what alpha = 0 yields.
                double alpha = denominator > 0 ? (t - lower) / denominator : 0.0;

                x[j] = (1 - alpha) * x[j - 1] + alpha * x[j];
                y[j] = (1 - alpha) * y[j - 1] + alpha * y[j];
                w[j] = (1 - alpha) * w[j - 1] + alpha * w[j];
            }
        }

        double divisor = w[degree];
        return Math.Abs(divisor) > 1e-12
            ? new Vec2(x[degree] / divisor, y[degree] / divisor)
            : new Vec2(x[degree], y[degree]);
    }

    /// <summary>
    /// Samples the curve densely enough to look smooth at the given zoom.
    /// </summary>
    /// <remarks>
    /// Sampling is uniform in the parameter, with the count taken from the
    /// control polygon's length. That over-samples a nearly straight stretch
    /// and under-samples a tight one compared with adaptive subdivision, but
    /// its cost is predictable, which matters more when a drawing holds
    /// thousands of splines and the count is recomputed as you zoom.
    /// </remarks>
    public static Vec2[] Tessellate(
        IReadOnlyList<Vec2> controlPoints,
        IReadOnlyList<double>? weights,
        IReadOnlyList<double> knots,
        int degree,
        double pixelsPerUnit)
    {
        if (!IsValid(controlPoints.Count, knots.Count, degree))
        {
            // Not evaluable: the control polygon at least follows the curve.
            var fallback = new Vec2[controlPoints.Count];
            for (int i = 0; i < fallback.Length; i++) fallback[i] = controlPoints[i];
            return fallback;
        }

        double polygonLength = 0;
        for (int i = 1; i < controlPoints.Count; i++)
            polygonLength += Vec2.Distance(controlPoints[i - 1], controlPoints[i]);

        int segments = Math.Clamp(
            (int)Math.Ceiling(polygonLength * pixelsPerUnit / PixelsPerSample),
            MinSegments, MaxSegments);

        double domainStart = knots[degree];
        double domainEnd = knots[controlPoints.Count];

        var points = new Vec2[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            double t = domainStart + (domainEnd - domainStart) * i / segments;
            points[i] = Evaluate(controlPoints, weights, knots, degree, t);
        }
        return points;
    }
}
