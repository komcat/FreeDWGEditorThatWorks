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
}
