using FreeDwg.Core.Geometry;
using FreeDwg.Core.Picking;
using FreeDwg.Core.Snapping;
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

    public override void CollectSnapPoints(SnapModes modes, ICollection<SnapCandidate> into)
    {
        if (modes.HasFlag(SnapModes.Endpoint))
        {
            into.Add(new SnapCandidate(Start, SnapKind.Endpoint));
            into.Add(new SnapCandidate(End, SnapKind.Endpoint));
        }

        if (modes.HasFlag(SnapModes.Midpoint))
            into.Add(new SnapCandidate(Vec2.Lerp(Start, End, 0.5), SnapKind.Midpoint));
    }
}
