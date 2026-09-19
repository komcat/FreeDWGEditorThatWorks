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

    protected override void TransformGeometry(in Mat3 transform)
    {
        Center = transform.Transform(Center);
        Radius *= transform.UniformScale;
    }
}
