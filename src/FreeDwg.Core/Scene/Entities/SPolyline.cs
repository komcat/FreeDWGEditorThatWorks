using FreeDwg.Core.Editing;
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

        return NearestSegment(point, out double distance) < 0 ? double.PositiveInfinity : distance;
    }

    /// <summary>
    /// Which segment <paramref name="point"/> is nearest to, and how near.
    /// -1 when there are no segments at all.
    /// </summary>
    /// <remarks>
    /// The index as well as the distance, because filleting a polyline needs
    /// to know <em>which</em> corner was pointed at, and working it out again
    /// at the call site would be a second copy of the bulge handling below --
    /// the copy that gets it wrong and measures an arc by its chord.
    /// </remarks>
    public int NearestSegment(Vec2 point, out double distance)
    {
        distance = double.PositiveInfinity;
        int best = -1;

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

            if (d >= distance) continue;

            distance = d;
            best = i;
        }

        return best;
    }

    /// <summary>Convenience for callers that only want the index.</summary>
    public int NearestSegment(Vec2 point) => NearestSegment(point, out _);

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

    /// <summary>
    /// Where the midpoint grip of a segment sits in the index space:
    /// vertices first, then one per bulged segment.
    /// </summary>
    private int BulgeGripIndex(int segment) => Vertices.Length + segment;

    public override void CollectGrips(ICollection<Grip> into)
    {
        for (int i = 0; i < Vertices.Length; i++)
            into.Add(new Grip(Vertices[i].Point, GripRole.Shape, i));

        // The midpoint of an arc segment, which re-bulges it. A straight
        // segment offers none: a handle that turned a line into an arc on a
        // nudge would change what the segment is rather than where it goes.
        for (int i = 0; i < SegmentCount; i++)
        {
            var from = Vertices[i];
            if (Math.Abs(from.Bulge) < 1e-12) continue;

            Vec2 to = Vertices[(i + 1) % Vertices.Length].Point;
            var (center, radius, startAngle, sweep) = ArcMath.FromBulge(from.Point, to, from.Bulge);
            if (radius <= 0) continue;

            into.Add(new Grip(ArcMath.PointAt(center, radius, startAngle + sweep / 2),
                GripRole.Shape, BulgeGripIndex(i)));
        }
    }

    protected override bool MoveGripGeometry(in Grip grip, Vec2 to)
    {
        if ((uint)grip.Index < (uint)Vertices.Length)
        {
            // The bulge belongs to the segment leaving this vertex, so it
            // rides along: dragging the end of an arc segment keeps it an
            // arc rather than flattening it.
            Vertices[grip.Index] = new PolyVertex(to, Vertices[grip.Index].Bulge);
            return true;
        }

        int segment = grip.Index - Vertices.Length;
        if ((uint)segment >= (uint)SegmentCount) return false;

        var from = Vertices[segment];
        Vec2 next = Vertices[(segment + 1) % Vertices.Length].Point;

        // A bulge is tan(sweep / 4) and the sweep is signed, so re-solving
        // the arc through the dragged point carries the direction with it:
        // dragging one across its chord turns the segment the other way.
        if (!ArcMath.TryArcThrough(from.Point, to, next, out _, out _, out _, out double sweep))
            return false;

        Vertices[segment] = new PolyVertex(from.Point, Math.Tan(sweep / 4));
        return true;
    }

    protected override void TransformGeometry(in Mat3 transform)
    {
        bool mirrored = transform.IsMirror;
        var moved = new PolyVertex[Vertices.Length];

        for (int i = 0; i < moved.Length; i++)
        {
            var vertex = Vertices[i];

            // A bulge is a signed sweep, so a mirror negates it for the same
            // reason it negates an arc's.
            moved[i] = new PolyVertex(
                transform.Transform(vertex.Point),
                mirrored ? -vertex.Bulge : vertex.Bulge);
        }

        Vertices = moved;
    }

    protected override void CloneGeometry() => Vertices = (PolyVertex[])Vertices.Clone();

    public override void CollectCurves(ICollection<CurvePiece> into, double tolerance)
    {
        for (int i = 0; i < SegmentCount; i++)
        {
            var from = Vertices[i];
            Vec2 to = Vertices[(i + 1) % Vertices.Length].Point;

            if (Math.Abs(from.Bulge) < 1e-12)
            {
                into.Add(CurvePiece.Segment(from.Point, to));
                continue;
            }

            // Bulges are arcs, and trimming to the chord of one would cut in
            // the wrong place by a whole sagitta.
            var (center, radius, startAngle, sweep) = ArcMath.FromBulge(from.Point, to, from.Bulge);

            if (radius > 0) into.Add(CurvePiece.Arc(center, radius, startAngle, sweep));
            else into.Add(CurvePiece.Segment(from.Point, to));
        }
    }
}
