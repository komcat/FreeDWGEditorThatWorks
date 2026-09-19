using FreeDwg.Core.Geometry;
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

    public override void Emit(IDrawingSink sink, in DisplayStyle style)
    {
        if (Vertices.Length < 2) return;

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
}
