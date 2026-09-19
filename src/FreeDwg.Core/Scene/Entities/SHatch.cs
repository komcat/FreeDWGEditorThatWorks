using FreeDwg.Core.Geometry;
using FreeDwg.Core.Picking;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Scene.Entities;

/// <summary>
/// A filled or hatched region.
/// </summary>
/// <remarks>
/// Boundary loops arrive already flattened, and pattern fills arrive as line
/// segments already clipped to those loops, because both depend only on the
/// geometry and not on the zoom -- doing the clipping once at import is far
/// cheaper than re-deriving it every frame. Islands work because the loops are
/// filled even-odd.
/// </remarks>
public sealed class SHatch : SceneEntity
{
    public SHatch(IReadOnlyList<IReadOnlyList<Vec2>> loops)
    {
        Loops = loops;
    }

    /// <summary>Closed boundary rings. Nested rings read as islands under even-odd fill.</summary>
    public IReadOnlyList<IReadOnlyList<Vec2>> Loops { get; set; }

    public bool IsSolid { get; set; }

    /// <summary>Pattern strokes, already clipped to the boundary.</summary>
    public IReadOnlyList<Segment2> PatternSegments { get; set; } = Array.Empty<Segment2>();

    protected override Bounds2 ComputeBounds()
    {
        var bounds = Bounds2.Empty;

        foreach (var loop in Loops)
            foreach (var point in loop)
                bounds = bounds.Union(point);

        // A pattern is clipped to the boundary, so it cannot extend the bounds
        // -- but a hatch whose boundary failed to convert may still have them.
        if (bounds.IsEmpty)
            foreach (var segment in PatternSegments)
                bounds = bounds.Union(segment.A).Union(segment.B);

        return bounds;
    }

    public override void Emit(in EmitContext context, in DisplayStyle style)
    {
        if (IsSolid)
        {
            if (Loops.Count > 0) context.Sink.FillLoops(Loops, style);
            return;
        }

        if (PatternSegments.Count > 0) context.Sink.Segments(PatternSegments, style);
    }

    /// <summary>
    /// A hatch is picked anywhere inside it, pattern or solid. AutoCAD asks
    /// for a pattern line unless PICKSTYLE says otherwise, but a hatch is a
    /// region to everyone who is not AutoCAD, and clicking the middle of a
    /// visibly filled area and hitting nothing reads as a bug.
    /// </summary>
    public override double DistanceTo(Vec2 point, in PickContext context)
    {
        if (Loops.Count == 0)
        {
            // A hatch whose boundary failed to convert still has its pattern.
            double nearest = double.PositiveInfinity;
            foreach (var segment in PatternSegments)
                nearest = Math.Min(nearest, Distance.PointToSegment(point, segment.A, segment.B));
            return nearest;
        }

        if (Distance.PointInLoops(point, Loops)) return 0;

        double best = double.PositiveInfinity;
        foreach (var loop in Loops)
            best = Math.Min(best, Distance.PointToPolyline(point, loop, closed: true));
        return best;
    }

    public override bool IntersectsRect(Bounds2 rect, in PickContext context)
    {
        if (!Bounds.Intersects(rect)) return false;

        foreach (var loop in Loops)
            if (Intersect.PolylineWithRect(loop, closed: true, rect)) return true;

        // A rectangle wholly inside the filled area touches no boundary.
        if (Loops.Count > 0) return Distance.PointInLoops(rect.Center, Loops);

        foreach (var segment in PatternSegments)
            if (Intersect.SegmentWithRect(segment.A, segment.B, rect)) return true;

        return false;
    }
}
