namespace FreeDwg.Core.Geometry;

/// <summary>
/// Where curves cross. Trim, extend, fillet and chamfer are all built on
/// this, and so is anything that ever wants an intersection snap.
/// </summary>
/// <remarks>
/// Three cases, because everything has already been reduced to segments and
/// arcs: segment-segment, segment-arc and arc-arc. Each is solved in closed
/// form rather than by subdivision -- an intersection that is a fraction of a
/// unit out is an intersection that leaves a gap, and gaps are what this
/// whole family of operations exists to remove.
/// </remarks>
public static class Intersection
{
    /// <summary>
    /// How far outside its own range a parameter may stray and still count.
    /// Endpoints that touch have to register, or a trim at a corner misses.
    /// </summary>
    public const double Epsilon = 1e-9;

    public static void Between(in CurvePiece a, in CurvePiece b, ICollection<Vec2> into)
    {
        if (a.IsDegenerate || b.IsDegenerate) return;
        if (!a.Bounds.Inflate(Epsilon).Intersects(b.Bounds.Inflate(Epsilon))) return;

        if (!a.IsArc && !b.IsArc) { SegmentSegment(a.A, a.B, b.A, b.B, into); return; }
        if (a.IsArc && b.IsArc) { ArcArc(a, b, into); return; }

        var segment = a.IsArc ? b : a;
        var arc = a.IsArc ? a : b;
        SegmentArc(segment.A, segment.B, arc, into);
    }

    /// <summary>
    /// Where two segments cross. Parallel runs give nothing, including the
    /// collinear overlap case: an overlap has no single crossing point, and
    /// trimming against one is not a thing anyone means to do.
    /// </summary>
    public static void SegmentSegment(Vec2 a0, Vec2 a1, Vec2 b0, Vec2 b1, ICollection<Vec2> into)
    {
        Vec2 r = a1 - a0;
        Vec2 s = b1 - b0;

        double denominator = Vec2.Cross(r, s);
        if (Math.Abs(denominator) < 1e-18) return;

        Vec2 gap = b0 - a0;
        double t = Vec2.Cross(gap, s) / denominator;
        double u = Vec2.Cross(gap, r) / denominator;

        if (t < -Epsilon || t > 1 + Epsilon || u < -Epsilon || u > 1 + Epsilon) return;

        into.Add(a0 + r * t);
    }

    /// <summary>Where a segment crosses an arc or a full circle.</summary>
    public static void SegmentArc(Vec2 a, Vec2 b, in CurvePiece arc, ICollection<Vec2> into)
    {
        foreach (double t in LineCircleParameters(a, b, arc.Center, arc.Radius))
        {
            if (t < -Epsilon || t > 1 + Epsilon) continue;

            var point = Vec2.Lerp(a, b, t);
            if (arc.Covers((point - arc.Center).Angle())) into.Add(point);
        }
    }

    /// <summary>
    /// Where the infinite line through two points crosses a circle, as
    /// parameters along that line. Extend needs the crossings beyond the end
    /// of a segment, which is exactly what the range filter throws away.
    /// </summary>
    public static IEnumerable<double> LineCircleParameters(Vec2 a, Vec2 b, Vec2 center, double radius)
    {
        Vec2 run = b - a;
        double lengthSquared = run.LengthSquared;
        if (lengthSquared < 1e-24 || radius <= 0) yield break;

        Vec2 offset = a - center;

        // |a + t*run - centre|^2 = r^2, solved for t.
        double half = Vec2.Dot(run, offset) / lengthSquared;
        double constant = (offset.LengthSquared - radius * radius) / lengthSquared;

        double discriminant = half * half - constant;
        if (discriminant < -Epsilon) yield break;

        double root = Math.Sqrt(Math.Max(discriminant, 0));

        // A tangent line touches once; returning it twice would have trim
        // believe there was a zero-length piece between two crossings.
        if (root < Epsilon)
        {
            yield return -half;
            yield break;
        }

        yield return -half - root;
        yield return -half + root;
    }

    /// <summary>Where two arcs or circles cross.</summary>
    public static void ArcArc(in CurvePiece first, in CurvePiece second, ICollection<Vec2> into)
    {
        Vec2 between = second.Center - first.Center;
        double distance = between.Length;

        double r0 = first.Radius, r1 = second.Radius;

        // Too far apart, one swallowed by the other, or concentric: the last
        // is either nothing or the whole circle, and neither is a crossing.
        if (distance < 1e-12 || distance > r0 + r1 + Epsilon || distance < Math.Abs(r0 - r1) - Epsilon) return;

        // Distance from the first centre to the line joining the two points.
        double along = (distance * distance + r0 * r0 - r1 * r1) / (2 * distance);
        double heightSquared = r0 * r0 - along * along;
        double height = heightSquared > 0 ? Math.Sqrt(heightSquared) : 0;

        Vec2 axis = between / distance;
        Vec2 foot = first.Center + axis * along;

        foreach (var point in height < Epsilon
                     ? new[] { foot }
                     : [foot + axis.Perp * height, foot - axis.Perp * height])
        {
            if (first.Covers((point - first.Center).Angle()) &&
                second.Covers((point - second.Center).Angle()))
            {
                into.Add(point);
            }
        }
    }

    /// <summary>
    /// Where the infinite extension of <paramref name="piece"/> meets
    /// <paramref name="other"/>. Extend reaches a boundary that the segment
    /// does not currently touch, so the crossings outside its own range are
    /// the whole point.
    /// </summary>
    public static void Unbounded(in CurvePiece piece, in CurvePiece other, ICollection<Vec2> into)
    {
        if (piece.IsArc)
        {
            // An arc extends round its own circle, so the full circle is what
            // the boundary has to be met on.
            Between(CurvePiece.Circle(piece.Center, piece.Radius), other, into);
            return;
        }

        if (other.IsArc)
        {
            foreach (double t in LineCircleParameters(piece.A, piece.B, other.Center, other.Radius))
            {
                var point = Vec2.Lerp(piece.A, piece.B, t);
                if (other.Covers((point - other.Center).Angle())) into.Add(point);
            }
            return;
        }

        // Two straight runs: the infinite line against the other segment.
        Vec2 r = piece.B - piece.A;
        Vec2 s = other.B - other.A;

        double denominator = Vec2.Cross(r, s);
        if (Math.Abs(denominator) < 1e-18) return;

        Vec2 gap = other.A - piece.A;
        double t2 = Vec2.Cross(gap, r) / denominator;
        if (t2 < -Epsilon || t2 > 1 + Epsilon) return;

        into.Add(piece.A + r * (Vec2.Cross(gap, s) / denominator));
    }
}
