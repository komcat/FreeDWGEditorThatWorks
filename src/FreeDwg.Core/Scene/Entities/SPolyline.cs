using FreeDwg.Core.Geometry;
using FreeDwg.Core.Picking;
using FreeDwg.Core.Snapping;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Scene.Entities;

/// <summary>
/// A vertex of a polyline. <see cref="Bulge"/> describes the arc from this
/// vertex to the next one: tan(sweep / 4), zero for a straight segment.
/// </summary>
public readonly struct PolyVertex
{
    public readonly Vec2 Point;
    public readonly double Bulge;

    public PolyVertex(Vec2 point, double bulge = 0) { Point = point; Bulge = bulge; }

    public override string ToString() => Bulge == 0 ? Point.ToString() : $"{Point} bulge {Bulge:0.###}";
}

/// <summary>
/// A connected run of line and arc segments. Bulges are kept rather than
/// flattened, so curved segments stay analytic like a standalone ARC does.
/// </summary>
public sealed class SPolyline : SceneEntity
{
    public SPolyline(PolyVertex[] vertices, bool closed)
    {
        Vertices = vertices;
        Closed = closed;
    }

    public PolyVertex[] Vertices { get; set; }
    public bool Closed { get; set; }

    private int SegmentCount => Closed ? Vertices.Length : Vertices.Length - 1;

    protected override Bounds2 ComputeBounds()
    {
        var b = Bounds2.Empty;
        foreach (var v in Vertices) b = b.Union(v.Point);

        // A bulged segment can swing well outside the hull of its endpoints.
        for (int i = 0; i < SegmentCount; i++)
        {
            var from = Vertices[i];
            if (Math.Abs(from.Bulge) < 1e-12) continue;

            Vec2 to = Vertices[(i + 1) % Vertices.Length].Point;
            var (center, radius, startAngle, sweep) = ArcMath.FromBulge(from.Point, to, from.Bulge);
            if (radius > 0) b = b.Union(ArcMath.Bounds(center, radius, startAngle, sweep));
        }
        return b;
    }

    public override void Emit(in EmitContext context, in DisplayStyle style)
    {
        if (Vertices.Length < 2) return;

        var sink = context.Sink;

        sink.BeginFigure(Vertices[0].Point, Closed, style);

        for (int i = 0; i < SegmentCount; i++)
        {
            var from = Vertices[i];
            Vec2 to = Vertices[(i + 1) % Vertices.Length].Point;

            if (Math.Abs(from.Bulge) < 1e-12 || Vec2.Distance(from.Point, to) < 1e-12)
            {
                sink.LineTo(to);
            }
            else
            {
                var (radius, largeArc, clockwise) = ArcMath.BulgeToArcTo(from.Point, to, from.Bulge);
                sink.ArcTo(to, radius, largeArc, clockwise);
            }
        }

        sink.EndFigure();
    }

    public override double DistanceTo(Vec2 point, in PickContext context)
    {
        if (Vertices.Length == 0) return double.PositiveInfinity;
        if (Vertices.Length == 1) return Vec2.Distance(point, Vertices[0].Point);

        double best = double.PositiveInfinity;

        for (int i = 0; i < SegmentCount; i++)
        {
            var from = Vertices[i];
            Vec2 to = Vertices[(i + 1) % Vertices.Length].Point;

            double d;
            if (Math.Abs(from.Bulge) < 1e-12)
            {
                d = Distance.PointToSegment(point, from.Point, to);
            }
            else
            {
                // Bulges are kept rather than flattened, so measure the arc
                // itself; a chord would read as much as a sagitta out.
                var (center, radius, startAngle, sweep) = ArcMath.FromBulge(from.Point, to, from.Bulge);
                d = radius > 0
                    ? Distance.PointToArc(point, center, radius, startAngle, sweep)
                    : Distance.PointToSegment(point, from.Point, to);
            }

            if (d < best) best = d;
        }

        return best;
    }

    public override bool IntersectsRect(Bounds2 rect, in PickContext context)
    {
        if (Vertices.Length == 0) return false;
        if (Vertices.Length == 1) return rect.Contains(Vertices[0].Point);

        for (int i = 0; i < SegmentCount; i++)
        {
            var from = Vertices[i];
            Vec2 to = Vertices[(i + 1) % Vertices.Length].Point;

            bool hit;
            if (Math.Abs(from.Bulge) < 1e-12)
            {
                hit = Intersect.SegmentWithRect(from.Point, to, rect);
            }
            else
            {
                var (center, radius, startAngle, sweep) = ArcMath.FromBulge(from.Point, to, from.Bulge);
                hit = radius > 0
                    ? Intersect.ArcWithRect(center, radius, startAngle, sweep, rect, context.Tolerance)
                    : Intersect.SegmentWithRect(from.Point, to, rect);
            }

            if (hit) return true;
        }

        return false;
    }

    public override void CollectSnapPoints(SnapModes modes, ICollection<SnapCandidate> into)
    {
        if (Vertices.Length == 0) return;

        if (modes.HasFlag(SnapModes.Endpoint))
            foreach (var vertex in Vertices)
                into.Add(new SnapCandidate(vertex.Point, SnapKind.Endpoint));

        bool wantsMid = modes.HasFlag(SnapModes.Midpoint);
        bool wantsCenter = modes.HasFlag(SnapModes.Center);
        if (!wantsMid && !wantsCenter) return;

        for (int i = 0; i < SegmentCount; i++)
        {
            var from = Vertices[i];
            Vec2 to = Vertices[(i + 1) % Vertices.Length].Point;

            if (Math.Abs(from.Bulge) < 1e-12)
            {
                if (wantsMid) into.Add(new SnapCandidate(Vec2.Lerp(from.Point, to, 0.5), SnapKind.Midpoint));
                continue;
            }

            // A bulged segment is an arc, so its midpoint is on the arc and
            // not on the chord -- the two are a whole sagitta apart.
            var (center, radius, startAngle, sweep) = ArcMath.FromBulge(from.Point, to, from.Bulge);
            if (radius <= 0) continue;

            if (wantsMid)
                into.Add(new SnapCandidate(
                    ArcMath.PointAt(center, radius, startAngle + sweep / 2), SnapKind.Midpoint));

            if (wantsCenter) into.Add(new SnapCandidate(center, SnapKind.Center));
        }
    }
}
