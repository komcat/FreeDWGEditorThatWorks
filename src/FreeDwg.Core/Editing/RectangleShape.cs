using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene.Entities;

namespace FreeDwg.Core.Editing;

/// <summary>
/// A polyline that is a rectangle, read as one: a corner, a width and a
/// height, so the two can be edited as numbers.
/// </summary>
/// <remarks>
/// DWG has no rectangle -- what the rectangle tool makes, and what AutoCAD's
/// RECTANG makes, is a closed four-vertex polyline -- so this is recognised
/// from the shape rather than remembered from how it was drawn. A rotated
/// one counts too.
/// <para>
/// "Width" is whichever pair of sides runs more nearly horizontal, and
/// "height" the other, so the names mean what they look like on screen even
/// for a rectangle whose vertices start up its left side. Resizing keeps the
/// first vertex where it is -- for one drawn here, the corner clicked first --
/// and moves the sides opposite it.
/// </para>
/// </remarks>
public readonly record struct RectangleShape(Vec2 Corner, Vec2 Along, Vec2 Across)
{
    public double Width => Along.Length;
    public double Height => Across.Length;

    /// <summary>The rectangle <paramref name="polyline"/> is, if it is one.</summary>
    public static bool TryRead(SPolyline polyline, out RectangleShape shape)
    {
        shape = default;

        var v = polyline.Vertices;
        if (!polyline.Closed || v.Length != 4) return false;
        if (v.Any(vertex => vertex.Bulge != 0)) return false;

        Vec2 corner = v[0].Point;
        Vec2 first = v[1].Point - corner;
        Vec2 last = v[3].Point - corner;

        double size = Math.Max(first.Length, last.Length);
        if (first.Length <= 1e-12 || last.Length <= 1e-12) return false;

        // Square corners, and the fourth vertex where the other three put it.
        double tolerance = 1e-9 * size;
        if (Math.Abs(Vec2.Dot(first, last)) > tolerance * size) return false;
        if (Vec2.Distance(v[2].Point, corner + first + last) > tolerance) return false;

        shape = Math.Abs(first.X) * last.Length >= Math.Abs(last.X) * first.Length
            ? new RectangleShape(corner, first, last)
            : new RectangleShape(corner, last, first);
        return true;
    }

    /// <summary>
    /// Gives a rectangle a new width, height or both, in place, keeping its
    /// first vertex, its directions and its vertex order. False if it is not
    /// a rectangle or a size is not a size.
    /// </summary>
    /// <remarks>
    /// Meant for a clone, as every shape edit is: the properties panel swaps
    /// the result in through <c>ReplaceEntities</c>, which is what makes it
    /// undoable and what keeps the handle the file knows it by.
    /// </remarks>
    public static bool Resize(SPolyline polyline, double? width, double? height)
    {
        if (!TryRead(polyline, out var shape)) return false;
        if (width is <= 0 || height is <= 0) return false;

        var along = shape.Along.Normalized() * (width ?? shape.Width);
        var across = shape.Across.Normalized() * (height ?? shape.Height);

        // Back into the vertices' own order: whichever of the two edges out
        // of the first corner was the width, it stays that edge.
        var v = polyline.Vertices;
        bool widthFirst = Vec2.Distance(v[1].Point - shape.Corner, shape.Along) <= 1e-9 * Math.Max(1, shape.Width);
        var toSecond = widthFirst ? along : across;
        var toLast = widthFirst ? across : along;

        polyline.Vertices =
        [
            new PolyVertex(shape.Corner),
            new PolyVertex(shape.Corner + toSecond),
            new PolyVertex(shape.Corner + toSecond + toLast),
            new PolyVertex(shape.Corner + toLast),
        ];
        polyline.InvalidateBounds();
        return true;
    }
}
