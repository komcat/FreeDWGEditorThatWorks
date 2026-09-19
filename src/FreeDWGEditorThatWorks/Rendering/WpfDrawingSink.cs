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
/// world units -- lines must not fatten as you zoom in. Block transforms are
/// therefore composed into a matrix the sink owns, not onto the context.
/// </remarks>
public sealed class WpfDrawingSink : IDrawingSink
{
    private readonly DrawingContext _dc;
    private readonly RenderSettings _settings;
    private readonly Dictionary<(uint Color, double Width), Pen> _penCache = new();
    private readonly Stack<Mat3> _transformStack = new();

    /// <summary>Current local space to device pixels, camera included.</summary>
    private Mat3 _toDevice;

    private StreamGeometry? _geometry;
    private StreamGeometryContext? _figure;
    private Pen? _pen;
    private Vec2 _currentPoint;

    public WpfDrawingSink(DrawingContext dc, Camera camera, RenderSettings settings)
    {
        _dc = dc;
        _settings = settings;
        _toDevice = camera.WorldToScreenMatrix;
    }

    public void PushTransform(in Mat3 transform)
    {
        _transformStack.Push(_toDevice);
        _toDevice = transform * _toDevice;
    }

    public void PopTransform() => _toDevice = _transformStack.Pop();

    public void BeginFigure(Vec2 start, bool closed, in DisplayStyle style)
    {
        EndFigure();

        _pen = GetPen(style);
        _geometry = new StreamGeometry();
        _figure = _geometry.Open();
        _figure.BeginFigure(ToPoint(start), isFilled: false, isClosed: closed);
        _currentPoint = start;
    }

    public void LineTo(Vec2 point)
    {
        _figure?.LineTo(ToPoint(point), isStroked: true, isSmoothJoin: false);
        _currentPoint = point;
    }

    public void ArcTo(Vec2 end, double radius, bool largeArc, bool clockwise)
    {
        if (_figure is null) return;

        Vec2 start = _currentPoint;
        _currentPoint = end;

        if (!_toDevice.IsSimilarity)
        {
            // Under a non-uniform block scale a circular arc becomes elliptical,
            // which no circular-arc primitive can express. Flatten it in local
            // space and let the transform do the rest -- the result is the
            // correct ellipse to within the tessellation tolerance.
            EmitFlattenedArc(start, end, radius, largeArc, clockwise);
            return;
        }

        double deviceRadius = radius * _toDevice.UniformScale;

        _figure.ArcTo(ToPoint(end), new Size(deviceRadius, deviceRadius),
            rotationAngle: 0, isLargeArc: largeArc, sweepDirection: DeviceSweep(clockwise),
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
        if (_toDevice.IsSimilarity)
        {
            double deviceRadius = radius * _toDevice.UniformScale;
            _dc.DrawEllipse(brush: null, GetPen(style), ToPoint(center), deviceRadius, deviceRadius);
            return;
        }

        // Non-uniform scale: the circle is an ellipse, possibly rotated.
        var points = ArcMath.Tessellate(center, radius, 0, ArcMath.TwoPi, radius * DeviceScaleBound);

        BeginFigure(points[0], closed: true, style);
        for (int i = 1; i < points.Length - 1; i++) LineTo(points[i]);
        EndFigure();
    }

    /// <summary>
    /// WPF names its sweep directions for how the arc <em>looks</em> on screen,
    /// and the world-to-device Y flip mirrors the plane, so the two cancel out.
    /// A block with a negative scale mirrors again and flips it back.
    /// </summary>
    /// <remarks>
    /// Verified by rendering, not by reasoning: getting this backwards turns
    /// every arc inside out while endpoints and bounds still look correct.
    /// </remarks>
    private SweepDirection DeviceSweep(bool clockwise)
    {
        bool mirrored = _toDevice.Determinant < 0;
        bool appearsClockwise = mirrored ? clockwise : !clockwise;
        return appearsClockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
    }

    /// <summary>Upper bound on how much the current transform scales a length.</summary>
    private double DeviceScaleBound => Math.Max(
        Math.Sqrt(_toDevice.M11 * _toDevice.M11 + _toDevice.M12 * _toDevice.M12),
        Math.Sqrt(_toDevice.M21 * _toDevice.M21 + _toDevice.M22 * _toDevice.M22));

    private void EmitFlattenedArc(Vec2 start, Vec2 end, double radius, bool largeArc, bool clockwise)
    {
        var (center, effectiveRadius, startAngle, sweep) =
            ArcFromEndpoints(start, end, radius, largeArc, clockwise);

        if (effectiveRadius <= 0)
        {
            _figure!.LineTo(ToPoint(end), isStroked: true, isSmoothJoin: false);
            return;
        }

        var points = ArcMath.Tessellate(center, effectiveRadius, startAngle, sweep,
            effectiveRadius * DeviceScaleBound);

        // Skip the first point: the figure is already sitting on it.
        for (int i = 1; i < points.Length; i++)
            _figure!.LineTo(ToPoint(points[i]), isStroked: true, isSmoothJoin: false);
    }

    /// <summary>
    /// Recovers centre and angles from the endpoint form of an arc (start, end,
    /// radius, large-arc and sweep flags), which is all a path sink is given.
    /// </summary>
    private static (Vec2 Center, double Radius, double StartAngle, double Sweep) ArcFromEndpoints(
        Vec2 start, Vec2 end, double radius, bool largeArc, bool clockwise)
    {
        Vec2 chord = end - start;
        double chordLength = chord.Length;
        if (chordLength < 1e-12) return (start, 0, 0, 0);

        // A radius smaller than the half-chord cannot reach both endpoints.
        double r = Math.Max(radius, chordLength / 2.0);
        double halfChord = chordLength / 2.0;
        double apothem = Math.Sqrt(Math.Max(0, r * r - halfChord * halfChord));

        // Of the two candidate centres, which one gives the requested
        // combination of direction and arc length.
        double side = (!clockwise) ^ largeArc ? 1.0 : -1.0;
        Vec2 center = Vec2.Lerp(start, end, 0.5) + chord.Normalized().Perp * (apothem * side);

        double startAngle = (start - center).Angle();
        double magnitude = 2.0 * Math.Asin(Math.Clamp(halfChord / r, -1.0, 1.0));
        if (largeArc) magnitude = ArcMath.TwoPi - magnitude;

        return (center, r, startAngle, clockwise ? -magnitude : magnitude);
    }

    private Point ToPoint(Vec2 local)
    {
        Vec2 device = _toDevice.Transform(local);
        return new Point(device.X, device.Y);
    }

    private Pen GetPen(in DisplayStyle rawStyle)
    {
        // Contrast adaptation lives here rather than in the render loop because
        // entities nested inside block definitions never pass through it.
        DisplayStyle style = _settings.Adapt(rawStyle);

        var key = (style.Color.Packed, _settings.StrokeWidthPixels(style));
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
