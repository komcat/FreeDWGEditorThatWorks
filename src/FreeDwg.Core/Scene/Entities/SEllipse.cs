using FreeDwg.Core.Geometry;
using FreeDwg.Core.Picking;
using FreeDwg.Core.Snapping;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Scene.Entities;

/// <summary>
/// A full or partial ellipse, held in DWG's own terms: a major-axis vector
/// from the centre, a minor/major ratio, and start and end curve parameters.
/// </summary>
public sealed class SEllipse : SceneEntity
{
    public SEllipse(Vec2 center, Vec2 majorAxis, double ratio, double startParameter, double sweep)
    {
        Center = center;
        MajorAxis = majorAxis;
        Ratio = ratio;
        StartParameter = startParameter;
        Sweep = sweep;
    }

    public Vec2 Center { get; set; }

    /// <summary>Vector from the centre to the end of the major axis; carries the rotation.</summary>
    public Vec2 MajorAxis { get; set; }

    public double Ratio { get; set; }
    public double StartParameter { get; set; }
    public double Sweep { get; set; }

    public bool IsClosed => Math.Abs(Sweep) >= ArcMath.TwoPi - 1e-9;

    protected override Bounds2 ComputeBounds() =>
        EllipseMath.Bounds(Center, MajorAxis, Ratio, StartParameter, Sweep);

    public override void Emit(in EmitContext context, in DisplayStyle style)
    {
        if (MajorAxis.LengthSquared <= 0 || Math.Abs(Sweep) < 1e-12) return;

        // Tessellated rather than kept analytic: the path vocabulary has a
        // circular arc but no elliptical one, and re-sampling per frame keeps
        // it smooth at any zoom for the price of an arc that is rarely dense.
        var points = EllipseMath.Tessellate(Center, MajorAxis, Ratio,
            StartParameter, Sweep, context.PixelsPerUnit);

        var sink = context.Sink;
        sink.BeginFigure(points[0], IsClosed, style);

        // A closed figure joins the last point to the first itself.
        int end = IsClosed ? points.Length - 1 : points.Length;
        for (int i = 1; i < end; i++) sink.LineTo(points[i]);

        sink.EndFigure();
    }

    /// <summary>
    /// Flattened at the pick tolerance rather than the zoom, for the same
    /// reason <see cref="Emit"/> flattens at the zoom: there is no closed form
    /// for the distance to an ellipse worth solving here.
    /// </summary>
    private Vec2[] Flatten(double tolerance) =>
        EllipseMath.Tessellate(Center, MajorAxis, Ratio, StartParameter, Sweep,
            Resolution.UnitsToSamples(tolerance));

    public override double DistanceTo(Vec2 point, in PickContext context)
    {
        if (MajorAxis.LengthSquared <= 0 || Math.Abs(Sweep) < 1e-12) return double.PositiveInfinity;
        return Distance.PointToPolyline(point, Flatten(context.Tolerance), IsClosed);
    }

    public override bool IntersectsRect(Bounds2 rect, in PickContext context)
    {
        if (MajorAxis.LengthSquared <= 0 || Math.Abs(Sweep) < 1e-12) return false;
        if (!Bounds.Intersects(rect)) return false;
        return Intersect.PolylineWithRect(Flatten(context.Tolerance), IsClosed, rect);
    }

    public override void CollectSnapPoints(SnapModes modes, ICollection<SnapCandidate> into)
    {
        if (MajorAxis.LengthSquared <= 0) return;

        if (modes.HasFlag(SnapModes.Center))
            into.Add(new SnapCandidate(Center, SnapKind.Center));

        if (modes.HasFlag(SnapModes.Endpoint) && !IsClosed)
        {
            into.Add(new SnapCandidate(
                EllipseMath.PointAt(Center, MajorAxis, Ratio, StartParameter), SnapKind.Endpoint));
            into.Add(new SnapCandidate(
                EllipseMath.PointAt(Center, MajorAxis, Ratio, StartParameter + Sweep), SnapKind.Endpoint));
        }

        if (!modes.HasFlag(SnapModes.Quadrant)) return;

        // The ends of the two axes, which for a rotated ellipse are not the
        // top and sides of its bounding box.
        for (int quarter = 0; quarter < 4; quarter++)
        {
            double parameter = quarter * (Math.PI / 2);
            if (!IsClosed && !ArcMath.Contains(parameter, StartParameter, Sweep)) continue;

            into.Add(new SnapCandidate(
                EllipseMath.PointAt(Center, MajorAxis, Ratio, parameter), SnapKind.Quadrant));
        }
    }

    protected override void TransformGeometry(in Mat3 transform)
    {
        Center = transform.Transform(Center);

        // The major axis is a direction and a length at once, so it goes
        // across as a vector; the ratio survives any similarity untouched.
        MajorAxis = transform.TransformVector(MajorAxis);

        if (transform.IsMirror)
        {
            StartParameter = -StartParameter;
            Sweep = -Sweep;
        }
    }

    public override void CollectCurves(ICollection<CurvePiece> into, double tolerance)
    {
        if (MajorAxis.LengthSquared <= 0 || Math.Abs(Sweep) < 1e-12) return;

        var points = Flatten(tolerance);
        for (int i = 1; i < points.Length; i++) into.Add(CurvePiece.Segment(points[i - 1], points[i]));

        if (IsClosed && points.Length > 2)
            into.Add(CurvePiece.Segment(points[^1], points[0]));
    }
}
