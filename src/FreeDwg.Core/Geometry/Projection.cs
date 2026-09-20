namespace FreeDwg.Core.Geometry;

/// <summary>
/// Points found by relating the cursor to a point you are drawing *from*,
/// rather than to a curve on its own: the foot of a perpendicular, and where
/// a tangent touches.
/// </summary>
/// <remarks>
/// These are the snaps that cannot be precomputed per entity, because the
/// answer moves with the point the line is being drawn from. That is why
/// they work on <see cref="CurvePiece"/> and take a <c>from</c>, while
/// endpoints and centres are offered by entities without being asked
/// anything.
/// </remarks>
public static class Projection
{
    /// <summary>
    /// The foot of the perpendicular from <paramref name="from"/> onto a
    /// segment, if it lands on the segment itself.
    /// </summary>
    /// <remarks>
    /// Off the ends it is refused rather than clamped. A clamped foot is the
    /// endpoint, which endpoint snap already offers, and offering it twice
    /// under a name that promises a right angle would be a lie.
    /// </remarks>
    public static bool PerpendicularToSegment(Vec2 from, Vec2 a, Vec2 b, out Vec2 foot)
    {
        foot = a;

        Vec2 run = b - a;
        double lengthSquared = run.LengthSquared;
        if (lengthSquared < 1e-24) return false;

        double t = Vec2.Dot(from - a, run) / lengthSquared;
        if (t <= 1e-9 || t >= 1 - 1e-9) return false;

        foot = a + run * t;
        return true;
    }

    /// <summary>
    /// Where the perpendicular from <paramref name="from"/> meets an arc:
    /// along the line through the centre, since a radius meets the curve at
    /// a right angle.
    /// </summary>
    public static void PerpendicularToArc(Vec2 from, in CurvePiece arc, ICollection<Vec2> into)
    {
        if (!arc.IsArc) return;

        Vec2 offset = from - arc.Center;
        if (offset.LengthSquared < 1e-18) return;   // dead centre: every direction is perpendicular

        Vec2 direction = offset.Normalized();

        foreach (var point in new[]
                 {
                     arc.Center + direction * arc.Radius,
                     arc.Center - direction * arc.Radius,
                 })
        {
            if (arc.Covers((point - arc.Center).Angle())) into.Add(point);
        }
    }

    /// <summary>
    /// Where a line from <paramref name="from"/> touches an arc without
    /// crossing it. Nothing when the point is inside the circle, which has
    /// no tangent through it.
    /// </summary>
    public static void TangentToArc(Vec2 from, in CurvePiece arc, ICollection<Vec2> into)
    {
        if (!arc.IsArc) return;

        Vec2 offset = from - arc.Center;
        double distance = offset.Length;

        // On or inside the rim: no tangent, and at exactly the rim the two
        // touch points collapse onto the cursor itself.
        if (distance <= arc.Radius + 1e-9) return;

        // Centre, touch point and origin make a right angle at the touch,
        // so the angle at the centre between the origin and the touch is
        // acos(r / d) -- measured from the direction of the origin, either
        // side of it.
        double spread = Math.Acos(Math.Clamp(arc.Radius / distance, -1.0, 1.0));
        double toward = offset.Angle();

        foreach (double angle in new[] { toward + spread, toward - spread })
        {
            if (arc.Covers(angle)) into.Add(ArcMath.PointAt(arc.Center, arc.Radius, angle));
        }
    }
}
