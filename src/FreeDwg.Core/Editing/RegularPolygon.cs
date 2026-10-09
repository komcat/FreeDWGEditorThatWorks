using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene.Entities;

namespace FreeDwg.Core.Editing;

/// <summary>
/// A polyline that is a regular polygon -- every side the same length and
/// every corner the same angle -- read as its centre and its sizes.
/// </summary>
/// <remarks>
/// AutoCAD's POLYGON makes a closed lightweight polyline and nothing marks
/// it as a polygon afterwards, so, like the rectangle, it is recognised from
/// the shape. A polygon has no width and height worth the name; it has a
/// side length and two radii -- to the corners, and to the middle of the
/// sides, which are AutoCAD's inscribed and circumscribed -- and setting any
/// of them scales the whole thing about its centre.
/// </remarks>
public readonly record struct RegularPolygon(Vec2 Center, int Sides, double Radius)
{
    /// <summary>Centre to a corner.</summary>
    public double OuterRadius => Radius;

    /// <summary>Centre to the middle of a side.</summary>
    public double InnerRadius => Radius * Math.Cos(Math.PI / Sides);

    public double SideLength => 2 * Radius * Math.Sin(Math.PI / Sides);

    /// <summary>The regular polygon <paramref name="polyline"/> is, if it is one.</summary>
    public static bool TryRead(SPolyline polyline, out RegularPolygon polygon)
    {
        polygon = default;

        var v = polyline.Vertices;
        int n = v.Length;
        if (!polyline.Closed || n < 3) return false;
        if (v.Any(vertex => vertex.Bulge != 0)) return false;

        var center = Vec2.Zero;
        foreach (var vertex in v) center += vertex.Point;
        center = center * (1.0 / n);

        // Every corner the same distance out, and every side the same length:
        // together those make it regular, convex and evenly spaced.
        double radius = Vec2.Distance(center, v[0].Point);
        if (radius <= 1e-12) return false;

        double tolerance = 1e-9 * radius;
        double side = Vec2.Distance(v[0].Point, v[1].Point);

        for (int i = 0; i < n; i++)
        {
            if (Math.Abs(Vec2.Distance(center, v[i].Point) - radius) > tolerance) return false;
            if (Math.Abs(Vec2.Distance(v[i].Point, v[(i + 1) % n].Point) - side) > tolerance) return false;
        }

        // Equal sides on a circle could still go round it twice, as a star.
        if (Math.Abs(side - 2 * radius * Math.Sin(Math.PI / n)) > tolerance) return false;

        polygon = new RegularPolygon(center, n, radius);
        return true;
    }

    /// <summary>
    /// Scales the polygon about its centre so its corners sit at
    /// <paramref name="radius"/>, keeping where its corners point.
    /// </summary>
    public static bool Resize(SPolyline polyline, double radius)
    {
        if (radius <= 0 || !TryRead(polyline, out var polygon)) return false;

        double factor = radius / polygon.Radius;
        polyline.Vertices = polyline.Vertices
            .Select(vertex => new PolyVertex(polygon.Center + (vertex.Point - polygon.Center) * factor))
            .ToArray();
        polyline.InvalidateBounds();
        return true;
    }

    /// <summary>The corner radius that gives a side of <paramref name="length"/>.</summary>
    public double RadiusForSide(double length) => length / (2 * Math.Sin(Math.PI / Sides));

    /// <summary>The corner radius that puts the sides <paramref name="inner"/> from the centre.</summary>
    public double RadiusForInner(double inner) => inner / Math.Cos(Math.PI / Sides);
}
