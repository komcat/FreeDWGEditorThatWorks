using FreeDwg.Core.Geometry;
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
}
