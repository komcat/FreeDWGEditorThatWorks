using FreeDwg.Core.Geometry;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Rendering;

/// <summary>
/// The only thing scene entities know about drawing. Deliberately a small,
/// figure-oriented path vocabulary that maps 1:1 onto WPF StreamGeometry, an
/// SVG path, and a PDF content stream -- so the renderer can be retargeted or
/// unit tested (record the calls, assert on them) without touching the scene.
/// </summary>
/// <remarks>
/// All coordinates and radii are in <em>world</em> units; the sink owns the
/// camera transform. Angles and winding follow the world convention too:
/// counter-clockwise is positive. A sink drawing into a Y-down device space
/// is responsible for flipping the sweep direction.
/// </remarks>
public interface IDrawingSink
{
    /// <summary>Starts a stroked figure at <paramref name="start"/>.</summary>
    void BeginFigure(Vec2 start, bool closed, in DisplayStyle style);

    void LineTo(Vec2 point);

    /// <summary>
    /// Circular arc from the current point to <paramref name="end"/>.
    /// <paramref name="clockwise"/> is expressed in world space.
    /// </summary>
    void ArcTo(Vec2 end, double radius, bool largeArc, bool clockwise);

    void EndFigure();

    /// <summary>A full circle, which cannot be expressed as a single arc segment.</summary>
    void Circle(Vec2 center, double radius, in DisplayStyle style);

    /// <summary>
    /// Pushes a block-instance transform. Coordinates passed to the sink are
    /// from then on in that block's local space, and the sink composes the
    /// stack. Block definitions are stored once and instanced by transform, so
    /// a drawing with ten thousand copies of a symbol holds one copy of it.
    /// </summary>
    void PushTransform(in Mat3 transform);

    void PopTransform();

    /// <summary>
    /// Fills a set of closed rings using the even-odd rule, so that a ring
    /// inside another reads as a hole.
    /// </summary>
    void FillLoops(IReadOnlyList<IReadOnlyList<Vec2>> loops, in DisplayStyle style);

    /// <summary>
    /// Strokes many disconnected two-point runs at once. A hatch pattern is
    /// thousands of them, and one call lets the sink batch what would
    /// otherwise be thousands of separate figures.
    /// </summary>
    void Segments(IReadOnlyList<Segment2> segments, in DisplayStyle style);

    /// <summary>
    /// Draws a block of text. Shaping, measurement and wrapping are the sink's
    /// job: Core has no font stack, and only the renderer knows the metrics
    /// that alignment depends on.
    /// </summary>
    void Text(in TextRun run, in DisplayStyle style);
}
