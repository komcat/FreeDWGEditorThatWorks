using FreeDwg.Core.Editing;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Picking;
using FreeDwg.Core.Snapping;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Scene.Entities;

/// <summary>A circular arc. <see cref="Sweep"/> is signed; positive is counter-clockwise.</summary>
public sealed class SArc : SceneEntity
{
    public SArc(Vec2 center, double radius, double startAngle, double sweep)
    {
        Center = center;
        Radius = radius;
        StartAngle = startAngle;
        Sweep = sweep;
    }

    public Vec2 Center { get; set; }
    public double Radius { get; set; }
    public double StartAngle { get; set; }
    public double Sweep { get; set; }

    public Vec2 StartPoint => ArcMath.PointAt(Center, Radius, StartAngle);
    public Vec2 EndPoint => ArcMath.PointAt(Center, Radius, StartAngle + Sweep);

    /// <summary>The point halfway round the sweep, not halfway along the chord.</summary>
    public Vec2 MidPoint => ArcMath.PointAt(Center, Radius, StartAngle + Sweep / 2);

    protected override Bounds2 ComputeBounds() =>
        ArcMath.Bounds(Center, Radius, StartAngle, Sweep);

    public override void Emit(in EmitContext context, in DisplayStyle style)
    {
        var sink = context.Sink;
        double sweep = Math.Abs(Sweep);

        // A full (or near-full) sweep has coincident endpoints, which no single
        // arc segment can express -- an ArcTo there is ambiguous and most
        // rasterisers collapse it to nothing.
        if (sweep >= ArcMath.TwoPi - 1e-9)
        {
            sink.Circle(Center, Radius, style);
            return;
        }
        if (sweep < 1e-12 || Radius <= 0) return;

        sink.BeginFigure(StartPoint, closed: false, style);
        sink.ArcTo(EndPoint, Radius, largeArc: sweep > Math.PI, clockwise: Sweep < 0);
        sink.EndFigure();
    }

    public override double DistanceTo(Vec2 point, in PickContext context) =>
        Distance.PointToArc(point, Center, Radius, StartAngle, Sweep);

    public override bool IntersectsRect(Bounds2 rect, in PickContext context) =>
        Intersect.ArcWithRect(Center, Radius, StartAngle, Sweep, rect, context.Tolerance);

    public override void CollectSnapPoints(SnapModes modes, ICollection<SnapCandidate> into)
    {
        if (Radius <= 0) return;

        if (modes.HasFlag(SnapModes.Endpoint))
        {
            into.Add(new SnapCandidate(StartPoint, SnapKind.Endpoint));
            into.Add(new SnapCandidate(EndPoint, SnapKind.Endpoint));
        }

        if (modes.HasFlag(SnapModes.Midpoint))
            into.Add(new SnapCandidate(MidPoint, SnapKind.Midpoint));

        if (modes.HasFlag(SnapModes.Center))
            into.Add(new SnapCandidate(Center, SnapKind.Center));

        if (!modes.HasFlag(SnapModes.Quadrant)) return;

        // Only the quadrants the arc actually reaches: an arc has no left
        // side to snap to if it never gets there.
        for (int quadrant = 0; quadrant < 4; quadrant++)
        {
            double angle = quadrant * (Math.PI / 2);
            if (ArcMath.Contains(angle, StartAngle, Sweep))
                into.Add(new SnapCandidate(ArcMath.PointAt(Center, Radius, angle), SnapKind.Quadrant));
        }
    }

    public override void CollectGrips(ICollection<Grip> into)
    {
        into.Add(new Grip(Center, GripRole.Move));
        if (Radius <= 0 || Math.Abs(Sweep) < 1e-12) return;

        into.Add(new Grip(StartPoint, GripRole.Shape, 0));
        into.Add(new Grip(EndPoint, GripRole.Shape, 1));
        into.Add(new Grip(MidPoint, GripRole.Shape, 2));
    }

    protected override bool MoveGripGeometry(in Grip grip, Vec2 to)
    {
        if ((uint)grip.Index > 2) return false;

        // Re-solved through three points, which is how the arc tool builds
        // one in the first place. The point in the middle is what says which
        // way round the arc goes, so a dragged end cannot quietly flip the
        // sweep -- the trap this codebase has already paid for twice.
        var (start, through, end) = grip.Index switch
        {
            0 => (to, MidPoint, EndPoint),
            1 => (StartPoint, MidPoint, to),
            _ => (StartPoint, to, EndPoint),
        };

        if (!ArcMath.TryArcThrough(start, through, end,
                out var center, out double radius, out double startAngle, out double sweep))
        {
            // Three points in a line are not an arc. The drag does nothing
            // rather than collapsing the entity into a degenerate one.
            return false;
        }

        Center = center;
        Radius = radius;
        StartAngle = startAngle;
        Sweep = sweep;
        return true;
    }

    protected override void TransformGeometry(in Mat3 transform)
    {
        // Take the start point across before the centre moves, so the new
        // start angle is measured from where the arc actually begins.
        Vec2 start = transform.Transform(StartPoint);

        Center = transform.Transform(Center);
        Radius *= transform.UniformScale;
        StartAngle = (start - Center).Angle();

        // A mirror turns the plane over, so the arc sweeps the other way
        // round. Miss this and every arc in a mirrored selection bulges
        // inside out while its endpoints stay exactly where they belong.
        if (transform.IsMirror) Sweep = -Sweep;
    }

    public override void CollectCurves(ICollection<CurvePiece> into, double tolerance)
    {
        if (Radius > 0 && Math.Abs(Sweep) > 1e-12)
            into.Add(CurvePiece.Arc(Center, Radius, StartAngle, Sweep));
    }
}
