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

    /// <summary>
    /// The foot of a right angle dropped from the point being drawn from.
    /// </summary>
    Perpendicular = 1 << 5,

    /// <summary>Where a line from the point being drawn from touches a circle.</summary>
    Tangent = 1 << 6,

    /// <summary>
    /// Lines out from points the cursor has rested on, so a new point can be
    /// placed level with or above one that already exists.
    /// </summary>
    Tracking = 1 << 7,

    /// <summary>Where two objects cross.</summary>
    Intersection = 1 << 8,

    /// <summary>
    /// Rays at regular angles out of the point being drawn from, so a line
    /// can be run at thirty degrees without measuring one.
    /// </summary>
    Polar = 1 << 9,

    /// <summary>Everything that belongs to an object, which is everything but the grid.</summary>
    Objects = Endpoint | Midpoint | Center | Quadrant | Perpendicular | Tangent | Intersection,

    All = Objects | Grid | Tracking | Polar,
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
    Perpendicular,
    Tangent,

    /// <summary>Where two objects cross.</summary>
    Intersection,

    /// <summary>Lined up with a point the cursor rested on earlier.</summary>
    Tracking,

    /// <summary>Running at one of the polar angles from the last point.</summary>
    Polar,
}

/// <summary>One point an entity is offering to snap to.</summary>
public readonly record struct SnapCandidate(Vec2 Point, SnapKind Kind)
{
    /// <summary>
    /// How near the cursor has to be for this to be offered, and what it is
    /// ranked by. NaN means the distance to <see cref="Point"/> itself.
    /// </summary>
    /// <remarks>
    /// Most snaps are their own target: you point at the corner you want.
    /// A tangent is not. The touch point can be a quarter of the way round
    /// the rim from where you are pointing, so what you aim at is the
    /// *circle* and the answer is worked out from it -- which is how every
    /// CAD tool behaves, and the only way the snap is usable at all, since
    /// aiming at the touch point would mean knowing where it is beforehand.
    /// Perpendicular has the same shape: you point at the line, not at the
    /// foot of the right angle.
    /// </remarks>
    public double Reach { get; init; } = double.NaN;

    /// <summary>What the cursor has to be near, given a fallback.</summary>
    public double ReachFrom(Vec2 cursor) =>
        double.IsNaN(Reach) ? Vec2.Distance(Point, cursor) : Reach;

    /// <summary>
    /// True when the answer is somewhere other than what was pointed at.
    /// </summary>
    /// <remarks>
    /// These rank below the snaps whose answer is under the cursor. Pointing
    /// at the end of a line that happens to start on a circle should give
    /// that end, not a tangent point a quarter of the way round the rim --
    /// even when the rim is a hair nearer than the end is. It is the
    /// ordering AutoCAD uses, and the reason is the same: a snap you can see
    /// beats one that has to be worked out.
    /// </remarks>
    public bool IsProjected => !double.IsNaN(Reach);
}

/// <summary>Where the cursor actually went, and why.</summary>
public readonly record struct SnapResult(Vec2 Point, SnapKind Kind)
{
    public static SnapResult Miss(Vec2 point) => new(point, SnapKind.None);

    public bool Found => Kind != SnapKind.None;

    /// <summary>
    /// Points the answer was lined up with, for the dashed guides that
    /// explain it. Empty for every snap that stands on its own.
    /// </summary>
    /// <remarks>
    /// A tracked point placed level with a corner across the sheet is
    /// otherwise indistinguishable from a point placed by hand nearby; the
    /// guide back to the corner is the whole explanation.
    /// </remarks>
    public IReadOnlyList<Vec2> Guides { get; init; } = [];
}
