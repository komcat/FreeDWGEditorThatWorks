using FreeDwg.Core.Geometry;

namespace FreeDwg.Core.Snapping;

/// <summary>
/// Which kinds of point the cursor is allowed to jump to. A flags enum
/// because these are independent switches, exactly as AutoCAD's OSMODE is.
/// </summary>
[Flags]
public enum SnapModes
{
    None = 0,

    /// <summary>Ends of lines and arcs, and every vertex of a polyline.</summary>
    Endpoint = 1 << 0,

    /// <summary>Halfway along a line, a polyline segment or an arc.</summary>
    Midpoint = 1 << 1,

    /// <summary>Centres of circles, arcs and ellipses.</summary>
    Center = 1 << 2,

    /// <summary>The top, bottom, left and right of a circle, arc or ellipse.</summary>
    Quadrant = 1 << 3,

    /// <summary>Intersections of the drawing grid.</summary>
    Grid = 1 << 4,

    /// <summary>Everything that belongs to an object, which is everything but the grid.</summary>
    Objects = Endpoint | Midpoint | Center | Quadrant,

    All = Objects | Grid,
}

/// <summary>
/// What kind of point a snap found, so the cursor can show the marker that
/// goes with it -- a square for an endpoint, a triangle for a midpoint, and
/// so on, which is the convention every CAD user already reads.
/// </summary>
public enum SnapKind
{
    None,
    Endpoint,
    Midpoint,
    Center,
    Quadrant,
    Grid,
}

/// <summary>One point an entity is offering to snap to.</summary>
public readonly record struct SnapCandidate(Vec2 Point, SnapKind Kind);

/// <summary>Where the cursor actually went, and why.</summary>
public readonly record struct SnapResult(Vec2 Point, SnapKind Kind)
{
    public static SnapResult Miss(Vec2 point) => new(point, SnapKind.None);

    public bool Found => Kind != SnapKind.None;
}
