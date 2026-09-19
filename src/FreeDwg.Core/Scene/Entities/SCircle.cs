using FreeDwg.Core.Geometry;
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
}
