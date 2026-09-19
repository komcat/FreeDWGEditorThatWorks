using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;

namespace FreeDwg.Core.Editing;

/// <summary>
/// Cutting things back to where they meet other things, and pushing them out
/// until they do.
/// </summary>
/// <remarks>
/// Both work the same way: find where the boundaries cross the target, turn
/// those crossings into parameters along it, and keep or drop the stretch the
/// user pointed at. Doing it in the target's own parameter -- distance along
/// a line, angle around an arc -- is what makes "the piece you clicked" a
/// well-defined thing rather than a guess.
/// </remarks>
public static class Trimming
{
    /// <summary>
    /// Cuts the stretch of <paramref name="target"/> containing
    /// <paramref name="pick"/> back to the nearest crossings either side.
    /// </summary>
    public static EditPlan Trim(SceneEntity target, IReadOnlyList<CurvePiece> boundaries, Vec2 pick) =>
        target switch
        {
            SLine line => TrimLine(line, boundaries, pick),
            SArc arc => TrimArc(arc, boundaries, pick),
            SCircle circle => TrimCircle(circle, boundaries, pick),
            _ => EditPlan.Nothing,
        };

    /// <summary>
    /// Pushes the end of <paramref name="target"/> nearest
    /// <paramref name="pick"/> out to the first boundary it reaches.
    /// </summary>
    public static EditPlan Extend(SceneEntity target, IReadOnlyList<CurvePiece> boundaries, Vec2 pick) =>
        target switch
        {
            SLine line => ExtendLine(line, boundaries, pick),
            SArc arc => ExtendArc(arc, boundaries, pick),
            _ => EditPlan.Nothing,
        };

    // ---- lines -----------------------------------------------------------

    private static EditPlan TrimLine(SLine line, IReadOnlyList<CurvePiece> boundaries, Vec2 pick)
    {
        Vec2 run = line.End - line.Start;
        double lengthSquared = run.LengthSquared;
        if (lengthSquared < 1e-24) return EditPlan.Nothing;

        var cuts = Crossings(CurvePiece.Segment(line.Start, line.End), boundaries)
            .Select(point => Vec2.Dot(point - line.Start, run) / lengthSquared)
            .Where(t => t > Intersection.Epsilon && t < 1 - Intersection.Epsilon)
            .ToList();

        if (cuts.Count == 0) return EditPlan.Nothing;

        double at = Math.Clamp(Vec2.Dot(pick - line.Start, run) / lengthSquared, 0, 1);
        var (from, to) = Surrounding(cuts, at, 0, 1);

        var pieces = new List<SceneEntity>();

        // What is left is whatever was outside the stretch that was cut.
        if (from > Intersection.Epsilon)
            pieces.Add(Like(line, line.Start, Vec2.Lerp(line.Start, line.End, from)));

        if (to < 1 - Intersection.Epsilon)
            pieces.Add(Like(line, Vec2.Lerp(line.Start, line.End, to), line.End));

        return EditPlan.Replace(line, pieces.ToArray());
    }

    private static EditPlan ExtendLine(SLine line, IReadOnlyList<CurvePiece> boundaries, Vec2 pick)
    {
        Vec2 run = line.End - line.Start;
        double lengthSquared = run.LengthSquared;
        if (lengthSquared < 1e-24) return EditPlan.Nothing;

        // Which end is being pushed: whichever half the click was in.
        bool fromEnd = Vec2.Dot(pick - line.Start, run) / lengthSquared > 0.5;

        double best = double.NaN;

        foreach (var point in Reaches(CurvePiece.Segment(line.Start, line.End), boundaries))
        {
            double t = Vec2.Dot(point - line.Start, run) / lengthSquared;

            // Strictly beyond the end being pushed, and the nearest such.
            if (fromEnd)
            {
                if (t <= 1 + Intersection.Epsilon) continue;
                if (double.IsNaN(best) || t < best) best = t;
            }
            else
            {
                if (t >= -Intersection.Epsilon) continue;
                if (double.IsNaN(best) || t > best) best = t;
            }
        }

        if (double.IsNaN(best)) return EditPlan.Nothing;

        var reached = Vec2.Lerp(line.Start, line.End, best);

        return EditPlan.Replace(line, fromEnd
            ? Like(line, line.Start, reached)
            : Like(line, reached, line.End));
    }

    // ---- arcs and circles -------------------------------------------------

    private static EditPlan TrimArc(SArc arc, IReadOnlyList<CurvePiece> boundaries, Vec2 pick)
    {
        if (arc.Radius <= 0 || Math.Abs(arc.Sweep) < 1e-12) return EditPlan.Nothing;

        var piece = CurvePiece.Arc(arc.Center, arc.Radius, arc.StartAngle, arc.Sweep);

        var cuts = Crossings(piece, boundaries)
            .Select(point => Along(arc, point))
            .Where(t => t > Intersection.Epsilon && t < 1 - Intersection.Epsilon)
            .ToList();

        if (cuts.Count == 0) return EditPlan.Nothing;

        double at = Math.Clamp(Along(arc, pick), 0, 1);
        var (from, to) = Surrounding(cuts, at, 0, 1);

        var pieces = new List<SceneEntity>();

        if (from > Intersection.Epsilon)
            pieces.Add(Like(arc, arc.StartAngle, arc.Sweep * from));

        if (to < 1 - Intersection.Epsilon)
            pieces.Add(Like(arc, arc.StartAngle + arc.Sweep * to, arc.Sweep * (1 - to)));

        return EditPlan.Replace(arc, pieces.ToArray());
    }

    /// <summary>
    /// A circle cut anywhere becomes an arc: the piece the user pointed at is
    /// removed and what is left is the rest of the way round.
    /// </summary>
    private static EditPlan TrimCircle(SCircle circle, IReadOnlyList<CurvePiece> boundaries, Vec2 pick)
    {
        if (circle.Radius <= 0) return EditPlan.Nothing;

        var piece = CurvePiece.Circle(circle.Center, circle.Radius);

        var angles = Crossings(piece, boundaries)
            .Select(point => ArcMath.Normalize((point - circle.Center).Angle()))
            .OrderBy(angle => angle)
            .ToList();

        if (angles.Count < 2) return EditPlan.Nothing;

        double at = ArcMath.Normalize((pick - circle.Center).Angle());

        // Round a circle the crossings wrap, so the stretch containing the
        // pick may be the one spanning the seam at zero.
        for (int i = 0; i < angles.Count; i++)
        {
            double from = angles[i];
            double to = angles[(i + 1) % angles.Count];
            double span = ArcMath.Normalize(to - from);

            if (ArcMath.Normalize(at - from) > span) continue;

            // Keep the complement: from the far cut round to this one.
            var kept = Like(circle, to, ArcMath.TwoPi - span);
            return EditPlan.Replace(circle, kept);
        }

        return EditPlan.Nothing;
    }

    private static EditPlan ExtendArc(SArc arc, IReadOnlyList<CurvePiece> boundaries, Vec2 pick)
    {
        if (arc.Radius <= 0 || Math.Abs(arc.Sweep) < 1e-12) return EditPlan.Nothing;

        var piece = CurvePiece.Arc(arc.Center, arc.Radius, arc.StartAngle, arc.Sweep);
        bool fromEnd = Along(arc, pick) > 0.5;

        double best = double.NaN;

        foreach (var point in Reaches(piece, boundaries))
        {
            double t = Along(arc, point);

            if (fromEnd)
            {
                if (t <= 1 + Intersection.Epsilon) continue;
                if (double.IsNaN(best) || t < best) best = t;
            }
            else
            {
                if (t >= -Intersection.Epsilon) continue;
                if (double.IsNaN(best) || t > best) best = t;
            }
        }

        if (double.IsNaN(best)) return EditPlan.Nothing;

        return EditPlan.Replace(arc, fromEnd
            ? Like(arc, arc.StartAngle, arc.Sweep * best)
            : Like(arc, arc.StartAngle + arc.Sweep * best, arc.Sweep * (1 - best)));
    }

    /// <summary>
    /// How far round the arc a point lies, as a fraction of the sweep. Values
    /// outside 0..1 are beyond one end or the other, which is what extend
    /// looks for.
    /// </summary>
    private static double Along(SArc arc, Vec2 point)
    {
        double offset = ArcMath.Normalize((point - arc.Center).Angle() - arc.StartAngle);

        // Measured the way the arc actually turns, so a clockwise arc reads
        // forwards rather than backwards.
        if (arc.Sweep < 0) offset -= ArcMath.TwoPi;

        double t = offset / arc.Sweep;

        // The far side of the circle is ambiguous; call it the nearer end.
        return t > 1 + (ArcMath.TwoPi / Math.Abs(arc.Sweep) - 1) / 2 ? t - ArcMath.TwoPi / arc.Sweep : t;
    }

    // ---- shared -----------------------------------------------------------

    private static List<Vec2> Crossings(in CurvePiece target, IReadOnlyList<CurvePiece> boundaries)
    {
        var points = new List<Vec2>();
        foreach (var boundary in boundaries) Intersection.Between(target, boundary, points);
        return points;
    }

    private static List<Vec2> Reaches(in CurvePiece target, IReadOnlyList<CurvePiece> boundaries)
    {
        var points = new List<Vec2>();
        foreach (var boundary in boundaries) Intersection.Unbounded(target, boundary, points);
        return points;
    }

    /// <summary>The two cuts either side of <paramref name="at"/>.</summary>
    private static (double From, double To) Surrounding(List<double> cuts, double at, double low, double high)
    {
        cuts.Sort();

        double from = low, to = high;
        foreach (double cut in cuts)
        {
            if (cut <= at) from = cut;
            else { to = cut; break; }
        }

        return (from, to);
    }

    /// <summary>A new line with the same layer and style as an old one.</summary>
    private static SLine Like(SLine original, Vec2 start, Vec2 end) => new(start, end)
    {
        LayerIndex = original.LayerIndex,
        Style = original.Style,
        Inherits = original.Inherits,
    };

    private static SArc Like(SArc original, double startAngle, double sweep) =>
        new(original.Center, original.Radius, startAngle, sweep)
        {
            LayerIndex = original.LayerIndex,
            Style = original.Style,
            Inherits = original.Inherits,
        };

    private static SArc Like(SCircle original, double startAngle, double sweep) =>
        new(original.Center, original.Radius, startAngle, sweep)
        {
            LayerIndex = original.LayerIndex,
            Style = original.Style,
            Inherits = original.Inherits,
        };
}
