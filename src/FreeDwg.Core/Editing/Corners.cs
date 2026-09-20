using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;

namespace FreeDwg.Core.Editing;

/// <summary>
/// Fillet and chamfer: rounding or cutting the corner where two lines meet.
/// </summary>
/// <remarks>
/// Both work on the corner the two lines <em>would</em> make, whether or not
/// they currently reach it, and both keep the half of each line that the
/// user pointed at. That is the whole user interface: where you click on each
/// line says which side survives, which is how AutoCAD has always done it and
/// why it needs no further questions.
/// <para>
/// Straight edges only. Arc-to-line and arc-to-arc fillets are a solvable but
/// much larger problem -- a tangent circle to two arbitrary curves -- and the
/// two straight edges case is the overwhelming majority of real use.
/// </para>
/// <para>
/// Two separate entities become three (two shortened, one joining them). Two
/// segments of the <em>same polyline</em> stay one entity: the corner vertex
/// is replaced by the two tangent points, and the arc rides between them as a
/// bulge. That is better than exploding the polyline first, which is what
/// this was waiting on: a rectangle with a rounded corner is still a
/// rectangle, and it still has the handle the file knows it by.
/// </para>
/// </remarks>
public static class Corners
{
    /// <summary>
    /// Rounds the corner with an arc of <paramref name="radius"/>. A radius
    /// of zero simply brings both lines to the corner, which is what
    /// AutoCAD's zero-radius fillet does and is quietly one of its most used
    /// features.
    /// </summary>
    public static EditPlan Fillet(SLine first, Vec2 firstPick, SLine second, Vec2 secondPick, double radius)
    {
        if (!Corner(first, second, out var corner)) return EditPlan.Nothing;

        Vec2 keepFirst = FarEnd(first, corner);
        Vec2 keepSecond = FarEnd(second, corner);

        if (radius <= 0)
            return Join(first, keepFirst, corner, second, keepSecond, corner);

        Vec2 alongFirst = (keepFirst - corner).Normalized();
        Vec2 alongSecond = (keepSecond - corner).Normalized();

        double half = Angle(alongFirst, alongSecond) / 2;

        // Parallel or doubled back: no corner to round.
        if (half < 1e-9 || half > Math.PI / 2 - 1e-9) return EditPlan.Nothing;

        // How far back from the corner the arc has to start for its radius to
        // come out right: r / tan(half the included angle).
        double setback = radius / Math.Tan(half);

        if (setback > Vec2.Distance(corner, keepFirst) || setback > Vec2.Distance(corner, keepSecond))
            return EditPlan.Nothing;

        Vec2 tangentFirst = corner + alongFirst * setback;
        Vec2 tangentSecond = corner + alongSecond * setback;

        // The centre sits on the bisector, r / sin(half) from the corner.
        Vec2 bisector = (alongFirst + alongSecond).Normalized();
        Vec2 center = corner + bisector * (radius / Math.Sin(half));

        double startAngle = (tangentFirst - center).Angle();
        double endAngle = (tangentSecond - center).Angle();
        double sweep = ArcMath.Normalize(endAngle - startAngle);

        // Always the short way round: the long way would loop back over the
        // lines it is supposed to be joining.
        if (sweep > Math.PI) sweep -= ArcMath.TwoPi;

        var arc = new SArc(center, radius, startAngle, sweep)
        {
            LayerIndex = first.LayerIndex,
            Style = first.Style,
            Inherits = first.Inherits,
        };

        var plan = Join(first, keepFirst, tangentFirst, second, keepSecond, tangentSecond);
        return new EditPlan(plan.Removed, [.. plan.Added, arc]);
    }

    /// <summary>
    /// Cuts the corner off with a straight run, <paramref name="distance"/>
    /// back along each line.
    /// </summary>
    public static EditPlan Chamfer(SLine first, Vec2 firstPick, SLine second, Vec2 secondPick, double distance)
    {
        if (!Corner(first, second, out var corner)) return EditPlan.Nothing;
        if (distance <= 0) return Fillet(first, firstPick, second, secondPick, 0);

        Vec2 keepFirst = FarEnd(first, corner);
        Vec2 keepSecond = FarEnd(second, corner);

        if (distance > Vec2.Distance(corner, keepFirst) || distance > Vec2.Distance(corner, keepSecond))
            return EditPlan.Nothing;

        Vec2 cutFirst = corner + (keepFirst - corner).Normalized() * distance;
        Vec2 cutSecond = corner + (keepSecond - corner).Normalized() * distance;

        var bridge = new SLine(cutFirst, cutSecond)
        {
            LayerIndex = first.LayerIndex,
            Style = first.Style,
            Inherits = first.Inherits,
        };

        var plan = Join(first, keepFirst, cutFirst, second, keepSecond, cutSecond);
        return new EditPlan(plan.Removed, [.. plan.Added, bridge]);
    }

    /// <summary>
    /// Rounds the corner of a polyline where two of its own segments meet,
    /// leaving it one polyline.
    /// </summary>
    /// <remarks>
    /// The two picks say which segments, and the corner is the vertex they
    /// share. Anything else -- the same segment twice, two that do not touch,
    /// or either end of an open run -- is not a corner and is left alone.
    /// </remarks>
    public static EditPlan Fillet(SPolyline polyline, Vec2 firstPick, Vec2 secondPick, double radius) =>
        Corner(polyline, firstPick, secondPick, radius, rounded: true);

    /// <summary>Cuts that same corner off straight instead.</summary>
    public static EditPlan Chamfer(SPolyline polyline, Vec2 firstPick, Vec2 secondPick, double distance) =>
        Corner(polyline, firstPick, secondPick, distance, rounded: false);

    private static EditPlan Corner(SPolyline polyline, Vec2 firstPick, Vec2 secondPick,
        double size, bool rounded)
    {
        if (size <= 0) return EditPlan.Nothing;

        int first = polyline.NearestSegment(firstPick);
        int second = polyline.NearestSegment(secondPick);

        return SharedVertex(polyline, first, second, out int vertex)
            ? Corner(polyline, vertex, size, rounded)
            : EditPlan.Nothing;
    }

    /// <summary>
    /// The vertex two segments have in common, if they have one.
    /// </summary>
    /// <remarks>
    /// Segment k runs from vertex k to vertex k+1, so consecutive segments
    /// share the vertex between them -- and on a closed polyline the last and
    /// the first share vertex zero, which is the corner of a rectangle that
    /// would otherwise be the one you cannot round.
    /// </remarks>
    private static bool SharedVertex(SPolyline polyline, int first, int second, out int vertex)
    {
        vertex = -1;

        int segments = polyline.Closed ? polyline.Vertices.Length : polyline.Vertices.Length - 1;
        if (first < 0 || second < 0 || first == second || segments < 2) return false;

        int low = Math.Min(first, second);
        int high = Math.Max(first, second);

        if (high - low == 1)
        {
            vertex = high;
            return true;
        }

        if (polyline.Closed && low == 0 && high == segments - 1)
        {
            vertex = 0;
            return true;
        }

        return false;
    }

    private static EditPlan Corner(SPolyline polyline, int vertex, double size, bool rounded)
    {
        var vertices = polyline.Vertices;
        int count = vertices.Length;

        if (!polyline.Closed && (vertex <= 0 || vertex >= count - 1)) return EditPlan.Nothing;

        int before = (vertex - 1 + count) % count;
        int after = (vertex + 1) % count;

        // Filleting into an existing arc is a different problem -- a tangent
        // circle to a curve rather than to two lines -- so a corner that
        // already has one is left alone rather than approximated.
        if (Math.Abs(vertices[before].Bulge) > 1e-12 || Math.Abs(vertices[vertex].Bulge) > 1e-12)
            return EditPlan.Nothing;

        Vec2 corner = vertices[vertex].Point;
        Vec2 incoming = corner - vertices[before].Point;
        Vec2 outgoing = vertices[after].Point - corner;

        double inLength = incoming.Length;
        double outLength = outgoing.Length;
        if (inLength < 1e-12 || outLength < 1e-12) return EditPlan.Nothing;

        Vec2 into = incoming / inLength;
        Vec2 outOf = outgoing / outLength;

        double cross = Vec2.Cross(into, outOf);
        double dot = Vec2.Dot(into, outOf);
        double turn = Angle(into, outOf);

        // Straight through, or doubled back on itself: no corner either way.
        if (turn < 1e-9 || turn > Math.PI - 1e-9) return EditPlan.Nothing;

        // How far back from the corner each side is cut. For a round the
        // radius has to come out right, which is r * tan(half the turn); for
        // a chamfer the distance is the distance.
        //
        // The half-angle comes from the cross and the dot rather than from
        // tan(acos(...)/2): the round trip through an angle costs the last
        // couple of digits, and a right angle -- which is most corners
        // anybody fillets -- then sets back by 9.999999999999998 instead of
        // by the radius they asked for.
        double setback = rounded ? size * (Math.Abs(cross) / (1 + dot)) : size;

        // Eating past the neighbouring vertex would move a corner that was
        // not the one picked.
        if (setback <= 0 || setback > inLength || setback > outLength) return EditPlan.Nothing;

        // tan(sweep / 4), and the sweep turns whichever way the corner does:
        // get the sign wrong and the arc bulges out of the shape instead of
        // rounding it, with both ends still exactly where they belong.
        double bulge = rounded ? Math.Tan(Math.Sign(cross) * turn / 4) : 0;

        var moved = new List<PolyVertex>(count + 1);
        for (int i = 0; i < count; i++)
        {
            if (i != vertex)
            {
                moved.Add(vertices[i]);
                continue;
            }

            // The one corner becomes the two tangent points, and whatever the
            // segment leaving it was doing carries on from the second.
            moved.Add(new PolyVertex(corner - into * setback, bulge));
            moved.Add(new PolyVertex(corner + outOf * setback, vertices[i].Bulge));
        }

        var replacement = new SPolyline([.. moved], polyline.Closed)
        {
            LayerIndex = polyline.LayerIndex,
            Style = polyline.Style,
            Inherits = polyline.Inherits,
        };

        return EditPlan.Replace(polyline, replacement);
    }

    /// <summary>
    /// Where the two lines would meet if both ran far enough. Returns false
    /// when they are parallel, which has no corner to work on.
    /// </summary>
    public static bool Corner(SLine first, SLine second, out Vec2 corner)
    {
        corner = Vec2.Zero;

        Vec2 r = first.End - first.Start;
        Vec2 s = second.End - second.Start;

        double denominator = Vec2.Cross(r, s);
        if (Math.Abs(denominator) < 1e-18) return false;

        corner = first.Start + r * (Vec2.Cross(second.Start - first.Start, s) / denominator);
        return true;
    }

    /// <summary>The end of a line further from the corner: the end that stays.</summary>
    private static Vec2 FarEnd(SLine line, Vec2 corner) =>
        Vec2.Distance(line.Start, corner) >= Vec2.Distance(line.End, corner) ? line.Start : line.End;

    private static double Angle(Vec2 a, Vec2 b) =>
        Math.Acos(Math.Clamp(Vec2.Dot(a, b), -1.0, 1.0));

    private static EditPlan Join(SLine first, Vec2 firstKeep, Vec2 firstTo,
        SLine second, Vec2 secondKeep, Vec2 secondTo)
    {
        var a = Like(first, firstKeep, firstTo);
        var b = Like(second, secondKeep, secondTo);

        a.SourceHandle = first.SourceHandle;
        b.SourceHandle = second.SourceHandle;

        return new EditPlan([first, second], [a, b]);
    }

    private static SLine Like(SLine original, Vec2 start, Vec2 end) => new(start, end)
    {
        LayerIndex = original.LayerIndex,
        Style = original.Style,
        Inherits = original.Inherits,
    };
}

/// <summary>
/// Moving a circle until it just touches a line.
/// </summary>
/// <remarks>
/// A constraint rather than a transform: the circle keeps its radius and
/// moves along the shortest path, which means straight towards or away from
/// the line. It stays on whichever side it started, because a circle that
/// jumped across the line it was being mated to would be a surprise.
/// </remarks>
public static class Tangency
{
    /// <summary>
    /// The translation that brings <paramref name="circle"/> tangent to the
    /// infinite line through <paramref name="line"/>.
    /// </summary>
    public static Vec2? ToLine(SCircle circle, SLine line)
    {
        Vec2 run = line.End - line.Start;
        if (run.LengthSquared < 1e-24 || circle.Radius <= 0) return null;

        Vec2 normal = run.Perp.Normalized();

        // Signed distance from the centre to the line, so the sign carries
        // which side the circle is already on.
        double signed = Vec2.Dot(circle.Center - line.Start, normal);
        if (Math.Abs(signed) < 1e-12) return null;   // centre on the line: no side to keep

        double wanted = signed > 0 ? circle.Radius : -circle.Radius;
        return normal * (wanted - signed);
    }
}
