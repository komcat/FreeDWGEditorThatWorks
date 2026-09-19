using FreeDwg.Core.Geometry;
using FreeDwg.Core.Picking;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Scene.Entities;

public sealed class SLine : SceneEntity
{
    public SLine(Vec2 start, Vec2 end) { Start = start; End = end; }

    public Vec2 Start { get; set; }
    public Vec2 End { get; set; }

    protected override Bounds2 ComputeBounds() => Bounds2.FromCorners(Start, End);

    public override void Emit(in EmitContext context, in DisplayStyle style)
    {
        context.Sink.BeginFigure(Start, closed: false, style);
        context.Sink.LineTo(End);
        context.Sink.EndFigure();
    }

    public override double DistanceTo(Vec2 point, in PickContext context) =>
        Distance.PointToSegment(point, Start, End);

    public override bool IntersectsRect(Bounds2 rect, in PickContext context) =>
        Intersect.SegmentWithRect(Start, End, rect);
}
