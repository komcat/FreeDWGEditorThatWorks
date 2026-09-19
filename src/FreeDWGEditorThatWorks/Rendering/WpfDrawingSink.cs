using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Styling;

namespace FreeDWGEditorThatWorks.Rendering;

/// <summary>
/// Renders the scene's path vocabulary into a WPF <see cref="DrawingContext"/>.
/// </summary>
/// <remarks>
/// Points are transformed to device space here rather than by pushing a
/// transform onto the context. That is deliberate: a pushed transform scales
/// pen thickness too, and plot line widths are millimetres-at-plot-scale, not
/// world units -- lines must not fatten as you zoom in.
/// </remarks>
public sealed class WpfDrawingSink : IDrawingSink
{
    private readonly DrawingContext _dc;
    private readonly Camera _camera;
    private readonly RenderSettings _options;
    private readonly Dictionary<(uint Color, double Width), Pen> _penCache = new();

    private StreamGeometry? _geometry;
    private StreamGeometryContext? _figure;
    private Pen? _pen;

    public WpfDrawingSink(DrawingContext dc, Camera camera, RenderSettings options)
    {
        _dc = dc;
        _camera = camera;
        _options = options;
    }

    public void BeginFigure(Vec2 start, bool closed, in DisplayStyle style)
    {
        EndFigure();

        _pen = GetPen(style);
        _geometry = new StreamGeometry();
        _figure = _geometry.Open();
        _figure.BeginFigure(ToPoint(start), isFilled: false, isClosed: closed);
    }

    public void LineTo(Vec2 point) =>
        _figure?.LineTo(ToPoint(point), isStroked: true, isSmoothJoin: false);

    public void ArcTo(Vec2 end, double radius, bool largeArc, bool clockwise)
    {
        if (_figure is null) return;

        double deviceRadius = radius * _camera.Scale;

        // WPF names its sweep directions for how the arc *looks* on screen, and
        // the world-to-device Y flip mirrors the plane -- the two cancel out, so
        // a world-clockwise arc is a WPF-clockwise arc. (Verified by render, not
        // by reasoning: getting this backwards silently turns every bulge inside
        // out while bounds and endpoints still look correct.)
        SweepDirection sweep = clockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;

        _figure.ArcTo(ToPoint(end), new Size(deviceRadius, deviceRadius),
            rotationAngle: 0, isLargeArc: largeArc, sweepDirection: sweep,
            isStroked: true, isSmoothJoin: false);
    }

    public void EndFigure()
    {
        if (_figure is null || _geometry is null) return;

        ((IDisposable)_figure).Dispose();
        _figure = null;

        _geometry.Freeze();
        _dc.DrawGeometry(brush: null, _pen, _geometry);
        _geometry = null;
    }

    public void Circle(Vec2 center, double radius, in DisplayStyle style)
    {
        double deviceRadius = radius * _camera.Scale;
        _dc.DrawEllipse(brush: null, GetPen(style), ToPoint(center), deviceRadius, deviceRadius);
    }

    private Point ToPoint(Vec2 world)
    {
        Vec2 s = _camera.WorldToScreen(world);
        return new Point(s.X, s.Y);
    }

    private Pen GetPen(in DisplayStyle style)
    {
        var key = (style.Color.Packed, _options.StrokeWidthPixels(style));
        if (_penCache.TryGetValue(key, out var cached)) return cached;

        var brush = new SolidColorBrush(Color.FromRgb(style.Color.R, style.Color.G, style.Color.B));
        brush.Freeze();

        var pen = new Pen(brush, key.Item2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();

        _penCache[key] = pen;
        return pen;
    }
}
