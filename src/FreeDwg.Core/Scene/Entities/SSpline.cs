using FreeDwg.Core.Editing;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Picking;
using FreeDwg.Core.Snapping;
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

    public override void CollectSnapPoints(SnapModes modes, ICollection<SnapCandidate> into)
    {
        // The ends only. Control points are not on the curve, and offering
        // one as a snap would put geometry where nothing is drawn.
        if (!modes.HasFlag(SnapModes.Endpoint) || IsClosed || !IsEvaluable) return;

        into.Add(new SnapCandidate(BSpline.Evaluate(ControlPoints, Weights, Knots, Degree, 0), SnapKind.Endpoint));
        into.Add(new SnapCandidate(BSpline.Evaluate(ControlPoints, Weights, Knots, Degree, 1), SnapKind.Endpoint));
    }

    public override void CollectGrips(ICollection<Grip> into)
    {
        // The control points, which is the one place a spline can be taken
        // hold of. They are not on the curve -- which is exactly why they
        // are grips and not snap points: a handle is something to pull, a
        // snap is somewhere to put geometry.
        for (int i = 0; i < ControlPoints.Count; i++)
            into.Add(new Grip(ControlPoints[i], GripRole.Shape, i));
    }

    protected override bool MoveGripGeometry(in Grip grip, Vec2 to)
    {
        if ((uint)grip.Index >= (uint)ControlPoints.Count) return false;

        // Knots and weights are untouched: moving a control point moves the
        // curve and nothing else about how it is parameterised.
        var moved = ControlPoints.ToArray();
        moved[grip.Index] = to;
        ControlPoints = moved;
        return true;
    }

    protected override void TransformGeometry(in Mat3 transform)
    {
        // A B-spline is affine invariant: transforming the control points
        // transforms the curve, with the knots and weights untouched.
        var moved = new Vec2[ControlPoints.Count];
        for (int i = 0; i < moved.Length; i++) moved[i] = transform.Transform(ControlPoints[i]);

        ControlPoints = moved;
    }

    protected override void CloneGeometry() => ControlPoints = ControlPoints.ToArray();

    public override void CollectCurves(ICollection<CurvePiece> into, double tolerance)
    {
        if (ControlPoints.Count < 2) return;

        var points = Flatten(tolerance);
        for (int i = 1; i < points.Length; i++) into.Add(CurvePiece.Segment(points[i - 1], points[i]));
    }
}
