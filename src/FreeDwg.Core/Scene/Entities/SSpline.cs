using FreeDwg.Core.Geometry;
using FreeDwg.Core.Picking;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Scene.Entities;

/// <summary>
/// A NURBS curve, kept in its defining form rather than flattened at import.
/// </summary>
/// <remarks>
/// Tessellating once on load would bake in a zoom level: fine at the extents,
/// visibly faceted at 100x. Keeping the control points and knots lets the
/// curve be re-sampled for the zoom actually in use, and leaves the data an
/// editor could later manipulate.
/// </remarks>
public sealed class SSpline : SceneEntity
{
    public SSpline(IReadOnlyList<Vec2> controlPoints, IReadOnlyList<double> knots, int degree)
    {
        ControlPoints = controlPoints;
        Knots = knots;
        Degree = degree;
    }

    public IReadOnlyList<Vec2> ControlPoints { get; set; }
    public IReadOnlyList<double> Knots { get; set; }

    /// <summary>Null for a non-rational spline, which is the common case.</summary>
    public IReadOnlyList<double>? Weights { get; set; }

    public int Degree { get; set; }
    public bool IsClosed { get; set; }

    public bool IsEvaluable => BSpline.IsValid(ControlPoints.Count, Knots.Count, Degree);

    protected override Bounds2 ComputeBounds()
    {
        // A B-spline lies inside the convex hull of its control points, so
        // their bounds are a valid (if slightly loose) bound without having to
        // evaluate the curve at all.
        var bounds = Bounds2.Empty;
        foreach (var point in ControlPoints) bounds = bounds.Union(point);
        return bounds;
    }

    public override void Emit(in EmitContext context, in DisplayStyle style)
    {
        if (ControlPoints.Count < 2) return;

        var points = BSpline.Tessellate(ControlPoints, Weights, Knots, Degree, context.PixelsPerUnit);
        if (points.Length < 2) return;

        var sink = context.Sink;
        sink.BeginFigure(points[0], IsClosed, style);
        for (int i = 1; i < points.Length; i++) sink.LineTo(points[i]);
        sink.EndFigure();
    }

    private Vec2[] Flatten(double tolerance) =>
        BSpline.Tessellate(ControlPoints, Weights, Knots, Degree, Resolution.UnitsToSamples(tolerance));

    public override double DistanceTo(Vec2 point, in PickContext context)
    {
        if (ControlPoints.Count < 2) return double.PositiveInfinity;

        var points = Flatten(context.Tolerance);
        if (points.Length < 2) return double.PositiveInfinity;

        return Distance.PointToPolyline(point, points, IsClosed);
    }

    public override bool IntersectsRect(Bounds2 rect, in PickContext context)
    {
        // The control hull already bounds the curve, so this rejects cheaply.
        if (ControlPoints.Count < 2 || !Bounds.Intersects(rect)) return false;

        return Intersect.PolylineWithRect(Flatten(context.Tolerance), IsClosed, rect);
    }
}
