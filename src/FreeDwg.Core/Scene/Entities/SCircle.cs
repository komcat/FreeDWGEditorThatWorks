using FreeDwg.Core.Editing;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Picking;
using FreeDwg.Core.Snapping;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Scene.Entities;

/// <summary>
/// Kept analytic rather than tessellated, so it stays a true circle at any
/// zoom level instead of degrading into a visible polygon.
/// </summary>
public sealed class SCircle : SceneEntity
{
    public SCircle(Vec2 center, double radius) { Center = center; Radius = radius; }

    public Vec2 Center { get; set; }
    public double Radius { get; set; }

    protected override Bounds2 ComputeBounds() => Bounds2.FromCorners(
        new Vec2(Center.X - Radius, Center.Y - Radius),
        new Vec2(Center.X + Radius, Center.Y + Radius));

    public override void Emit(in EmitContext context, in DisplayStyle style) =>
        context.Sink.Circle(Center, Radius, style);


    // The outline, not the disc: a click in the middle of a circle is a click
    // on whatever is inside it, as it is in every CAD tool.
    public override double DistanceTo(Vec2 point, in PickContext context) =>
        Distance.PointToCircle(point, Center, Radius);

    public override bool IntersectsRect(Bounds2 rect, in PickContext context) =>
        Intersect.CircleWithRect(Center, Radius, rect);

    public override void CollectSnapPoints(SnapModes modes, ICollection<SnapCandidate> into)
    {
        if (modes.HasFlag(SnapModes.Center))
            into.Add(new SnapCandidate(Center, SnapKind.Center));

        if (!modes.HasFlag(SnapModes.Quadrant) || Radius <= 0) return;

        // Right, top, left, bottom: the four points a circle is dimensioned
        // from, and what makes one circle land tangent to another.
        into.Add(new SnapCandidate(new Vec2(Center.X + Radius, Center.Y), SnapKind.Quadrant));
        into.Add(new SnapCandidate(new Vec2(Center.X, Center.Y + Radius), SnapKind.Quadrant));
        into.Add(new SnapCandidate(new Vec2(Center.X - Radius, Center.Y), SnapKind.Quadrant));
        into.Add(new SnapCandidate(new Vec2(Center.X, Center.Y - Radius), SnapKind.Quadrant));
    }

    public override void CollectGrips(ICollection<Grip> into)
    {
        into.Add(new Grip(Center, GripRole.Move));
        if (Radius <= 0) return;

        // The four quadrants, which is where a circle is dimensioned from.
        into.Add(new Grip(new Vec2(Center.X + Radius, Center.Y), GripRole.Shape, 0));
        into.Add(new Grip(new Vec2(Center.X, Center.Y + Radius), GripRole.Shape, 1));
        into.Add(new Grip(new Vec2(Center.X - Radius, Center.Y), GripRole.Shape, 2));
        into.Add(new Grip(new Vec2(Center.X, Center.Y - Radius), GripRole.Shape, 3));
    }

    protected override bool MoveGripGeometry(in Grip grip, Vec2 to)
    {
        if ((uint)grip.Index > 3) return false;

        // All four do the same thing: a circle has one dimension, so
        // dragging the top of one is asking for a radius and not for an
        // ellipse. Which grip was taken only matters to the eye.
        double radius = Vec2.Distance(Center, to);
        if (radius <= 0) return false;

        Radius = radius;
        return true;
    }

    protected override void TransformGeometry(in Mat3 transform)
    {
        Center = transform.Transform(Center);
        Radius *= transform.UniformScale;
    }

    public override void CollectCurves(ICollection<CurvePiece> into, double tolerance)
    {
        if (Radius > 0) into.Add(CurvePiece.Circle(Center, Radius));
    }
}
