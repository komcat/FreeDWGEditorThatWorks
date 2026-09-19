using FreeDwg.Core.Geometry;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Rendering;

/// <summary>
/// Forwards everything to another sink with the style replaced, so an entity
/// paints in one colour whatever its own says.
/// </summary>
/// <remarks>
/// This is how selection highlighting reaches inside a block instance. The
/// style handed to the sink comes from the entity, not from the emit context,
/// so there is nothing to thread an override down -- but every style passes
/// through the sink on its way to the screen, and that can be intercepted.
/// Entities and <see cref="EmitContext"/> stay unaware.
/// </remarks>
public sealed class StyleOverrideSink : IDrawingSink
{
    private readonly IDrawingSink _inner;
    private readonly Rgb _color;

    public StyleOverrideSink(IDrawingSink inner, Rgb color)
    {
        _inner = inner;
        _color = color;
    }

    /// <summary>Dashes are kept: a hidden line stays recognisable when selected.</summary>
    private DisplayStyle Override(in DisplayStyle style) => style with { Color = _color };

    public void BeginFigure(Vec2 start, bool closed, in DisplayStyle style) =>
        _inner.BeginFigure(start, closed, Override(style));

    public void LineTo(Vec2 point) => _inner.LineTo(point);

    public void ArcTo(Vec2 end, double radius, bool largeArc, bool clockwise) =>
        _inner.ArcTo(end, radius, largeArc, clockwise);

    public void EndFigure() => _inner.EndFigure();

    public void Circle(Vec2 center, double radius, in DisplayStyle style) =>
        _inner.Circle(center, radius, Override(style));

    public void PushTransform(in Mat3 transform) => _inner.PushTransform(transform);
    public void PopTransform() => _inner.PopTransform();
    public void PushClip(Bounds2 rectangle) => _inner.PushClip(rectangle);
    public void PopClip() => _inner.PopClip();

    public void FillLoops(IReadOnlyList<IReadOnlyList<Vec2>> loops, in DisplayStyle style) =>
        _inner.FillLoops(loops, Override(style));

    public void Segments(IReadOnlyList<Segment2> segments, in DisplayStyle style) =>
        _inner.Segments(segments, Override(style));

    public void Text(in TextRun run, in DisplayStyle style) => _inner.Text(run, Override(style));
}
