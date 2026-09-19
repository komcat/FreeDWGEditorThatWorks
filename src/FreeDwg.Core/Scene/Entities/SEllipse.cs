using FreeDwg.Core.Geometry;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Scene.Entities;

/// <summary>
/// A full or partial ellipse, held in DWG's own terms: a major-axis vector
/// from the centre, a minor/major ratio, and start and end curve parameters.
/// </summary>
public sealed class SEllipse : SceneEntity
{
    public SEllipse(Vec2 center, Vec2 majorAxis, double ratio, double startParameter, double sweep)
    {
        Center = center;
        MajorAxis = majorAxis;
        Ratio = ratio;
        StartParameter = startParameter;
        Sweep = sweep;
    }

    public Vec2 Center { get; set; }

    /// <summary>Vector from the centre to the end of the major axis; carries the rotation.</summary>
    public Vec2 MajorAxis { get; set; }

    public double Ratio { get; set; }
    public double StartParameter { get; set; }
    public double Sweep { get; set; }

    public bool IsClosed => Math.Abs(Sweep) >= ArcMath.TwoPi - 1e-9;

    protected override Bounds2 ComputeBounds() =>
        EllipseMath.Bounds(Center, MajorAxis, Ratio, StartParameter, Sweep);

    public override void Emit(in EmitContext context, in DisplayStyle style)
    {
        if (MajorAxis.LengthSquared <= 0 || Math.Abs(Sweep) < 1e-12) return;

        // Tessellated rather than kept analytic: the path vocabulary has a
        // circular arc but no elliptical one, and re-sampling per frame keeps
        // it smooth at any zoom for the price of an arc that is rarely dense.
        var points = EllipseMath.Tessellate(Center, MajorAxis, Ratio,
            StartParameter, Sweep, context.PixelsPerUnit);

        var sink = context.Sink;
        sink.BeginFigure(points[0], IsClosed, style);

        // A closed figure joins the last point to the first itself.
        int end = IsClosed ? points.Length - 1 : points.Length;
        for (int i = 1; i < end; i++) sink.LineTo(points[i]);

        sink.EndFigure();
    }
}
