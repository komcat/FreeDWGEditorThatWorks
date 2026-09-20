using FreeDwg.Core.Scene;

namespace FreeDwg.Core.Geometry;

/// <summary>
/// Everything a linear dimension is made of, worked out from its three
/// points: where the dimension line runs, where the extension lines go, how
/// long the thing being measured is, and where the number sits.
/// </summary>
public readonly record struct DimensionGeometry(
    Vec2 LineStart,
    Vec2 LineEnd,
    Vec2 FirstExtensionStart,
    Vec2 FirstExtensionEnd,
    Vec2 SecondExtensionStart,
    Vec2 SecondExtensionEnd,
    Vec2 TextAnchor,
    double TextRotation,
    double Measurement)
{
    /// <summary>Along the dimension line, from the first arrow to the second.</summary>
    public Vec2 Direction =>
        (LineEnd - LineStart).LengthSquared > 0 ? (LineEnd - LineStart).Normalized() : new Vec2(1, 0);
}

/// <summary>
/// Whether the marks fit between the extension lines, and where they go when
/// they do not.
/// </summary>
/// <param name="StrokeStart">
/// The dimension line as <em>drawn</em>, which runs past the measured ends
/// when the arrows have been turned out so that they have a line to sit on.
/// The measured ends themselves never move: they are what the number is of.
/// </param>
public readonly record struct DimensionFit(
    bool ArrowsOutside,
    bool TextOutside,
    Vec2 TextAnchor,
    Vec2 StrokeStart,
    Vec2 StrokeEnd);

/// <summary>
/// The layout of a linear or aligned dimension.
/// </summary>
/// <remarks>
/// Arithmetic with no sink and no entity anywhere near it, for the reason
/// <c>ArcMath</c> is: the drawn result is a dozen coincidences that all have
/// to agree -- the extension lines have to reach the dimension line exactly,
/// the arrows have to sit on it, and the number has to be the distance the
/// arrows actually span -- and each of those is a statement a test can make.
/// </remarks>
public static class DimensionMath
{
    /// <summary>
    /// Solves a dimension measuring from <paramref name="first"/> to
    /// <paramref name="second"/>, with its line running through
    /// <paramref name="through"/> at <paramref name="rotation"/>.
    /// </summary>
    /// <remarks>
    /// The measurement is the separation of the two origins <em>along the
    /// dimension line</em>, not the distance between them. That is the whole
    /// difference between a linear dimension and an aligned one: a horizontal
    /// dimension across two points at different heights measures the
    /// horizontal gap, and the extension lines are what make up the rest.
    /// </remarks>
    public static DimensionGeometry Solve(Vec2 first, Vec2 second, Vec2 through,
        double rotation, in DimensionStyle style)
    {
        Vec2 direction = new(Math.Cos(rotation), Math.Sin(rotation));
        Vec2 across = direction.Perp;

        // Both origins projected onto the line through the picked point: the
        // ends of the dimension line, and where the extension lines stop.
        Vec2 lineStart = first + across * Vec2.Dot(through - first, across);
        Vec2 lineEnd = second + across * Vec2.Dot(through - second, across);

        var (firstStart, firstEnd) = Extension(first, lineStart, across, style);
        var (secondStart, secondEnd) = Extension(second, lineEnd, across, style);

        // The number reads left to right whichever way round the two points
        // were picked, since a dimension picked right to left is not an
        // upside-down dimension.
        Vec2 along = direction;
        if (along.X < 0 || (along.X == 0 && along.Y < 0)) along = -along;

        // And it sits above its own line, on the side it is read from --
        // unconditionally, which is what ISO says and what every drawing
        // shows. Not on the far side from the geometry: a dimension under a
        // part still carries its number on top of its line.
        return new DimensionGeometry(
            lineStart, lineEnd,
            firstStart, firstEnd,
            secondStart, secondEnd,
            Vec2.Lerp(lineStart, lineEnd, 0.5) + along.Perp * style.TextGap,
            along.Angle(),
            Vec2.Distance(lineStart, lineEnd));
    }

    /// <summary>
    /// One extension line: it starts a gap clear of the point it came from,
    /// so the dimension does not touch the geometry, and runs through its end
    /// of the dimension line and a little past.
    /// </summary>
    /// <remarks>
    /// The direction is taken per line rather than from the side the
    /// dimension as a whole sits on, because the dimension line can lie
    /// <em>between</em> the two origins -- one point above it and one below.
    /// Then one extension line has to travel the other way to reach it, and
    /// a shared direction leaves that one hanging in mid air, short of the
    /// line it exists to meet.
    /// </remarks>
    private static (Vec2 Start, Vec2 End) Extension(Vec2 origin, Vec2 at, Vec2 across,
        in DimensionStyle style)
    {
        double reach = Vec2.Dot(at - origin, across);
        Vec2 towards = reach < 0 ? -across : across;

        Vec2 end = at + towards * style.ExtensionBeyond;

        // Pulled in close, the gap would be longer than the line and the
        // extension would run backwards. Then it starts at the origin, which
        // is what a dimension squeezed against its geometry has to look like.
        if (Vec2.Dot(end - origin, towards) <= style.ExtensionOffset) return (origin, end);

        return (origin + towards * style.ExtensionOffset, end);
    }

    /// <summary>
    /// Decides whether the arrowheads and the number fit in the space being
    /// measured, and moves whichever does not to the outside.
    /// </summary>
    /// <remarks>
    /// Two independent questions, because they are two different collisions.
    /// The arrows are on the line and run out of room along it; the number
    /// sits above the line and runs out of room across the extension lines.
    /// A small feature usually needs the arrows turned out and the number
    /// left where it is, which is exactly the common case.
    /// <para>
    /// Two arrows that meet exactly tip to tail read as one solid diamond
    /// rather than as a measurement, so "fits" asks for a little clear line
    /// between them and not merely for room.
    /// </para>
    /// </remarks>
    public static DimensionFit Fit(in DimensionGeometry geometry, double textWidth,
        in DimensionStyle style)
    {
        double span = geometry.Measurement;
        Vec2 along = geometry.Direction;

        bool arrowsOutside = span < style.ArrowSize * 2 + style.TextGap;
        bool textOutside = textWidth + style.TextGap * 2 > span;

        // How far past each end an outside arrow needs, and how far past the
        // whole thing an outside number then has to start.
        double reach = style.ArrowSize * 2;

        Vec2 start = arrowsOutside ? geometry.LineStart - along * reach : geometry.LineStart;
        Vec2 end = arrowsOutside ? geometry.LineEnd + along * reach : geometry.LineEnd;

        return new DimensionFit(arrowsOutside, textOutside,
            textOutside ? Beyond(geometry, textWidth, reach, style) : geometry.TextAnchor,
            start, end);
    }

    /// <summary>
    /// Where the number goes when it will not fit between the extension
    /// lines: past the end it reads towards, and still above the line.
    /// </summary>
    private static Vec2 Beyond(in DimensionGeometry geometry, double textWidth, double reach,
        in DimensionStyle style)
    {
        Vec2 reading = new(Math.Cos(geometry.TextRotation), Math.Sin(geometry.TextRotation));

        Vec2 far = Vec2.Dot(geometry.LineEnd - geometry.LineStart, reading) >= 0
            ? geometry.LineEnd
            : geometry.LineStart;

        return far
             + reading * (reach + style.TextGap + textWidth / 2)
             + reading.Perp * style.TextGap;
    }

    /// <summary>
    /// Solves a radius or diameter dimension: a leader out from the centre of
    /// a circle, with the arrow on the rim and the number at the far end.
    /// </summary>
    /// <remarks>
    /// The leader aims at <paramref name="at"/>, so the arrow slides round
    /// the rim as the text is dragged and always points along the line it is
    /// on -- outward when the text is pulled inside the circle, inward when
    /// it is pulled outside. One layout covers both, and neither is a special
    /// case anybody has to remember.
    /// <para>
    /// A diameter runs right across, so it gets the far side of the circle
    /// and a second arrow; a radius stops at the centre. That is the only
    /// difference between the two, which is as it should be.
    /// </para>
    /// </remarks>
    public static DimensionGeometry Solve(Vec2 center, double radius, Vec2 at, bool across,
        in DimensionStyle style)
    {
        Vec2 arm = at - center;
        Vec2 direction = arm.LengthSquared > 1e-24 ? arm.Normalized() : new Vec2(1, 0);

        Vec2 rim = center + direction * radius;
        Vec2 far = across ? center - direction * radius : center;

        // The number reads left to right, as it does on every other kind.
        Vec2 along = direction;
        if (along.X < 0 || (along.X == 0 && along.Y < 0)) along = -along;

        return new DimensionGeometry(
            far, rim,
            // No extension lines: a circle is its own.
            far, far,
            rim, rim,
            at + along.Perp * style.TextGap,
            along.Angle(),
            across ? radius * 2 : radius);
    }

    /// <summary>
    /// The arrowheads for a radius or diameter: one on the rim, and a second
    /// on the far side when the line runs right across.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<Vec2>> LeaderArrowheads(in DimensionGeometry geometry,
        bool across, in DimensionStyle style)
    {
        double size = style.ArrowSize;
        if (size <= 0) return [];

        // Along the leader towards the rim, which is the end the arrow marks.
        Vec2 run = geometry.LineEnd - geometry.LineStart;
        if (run.LengthSquared < 1e-24) return [];

        Vec2 along = run.Normalized();
        var rim = Arrowhead(geometry.LineEnd, -along, size);

        return across ? [rim, Arrowhead(geometry.LineStart, along, size)] : [rim];
    }

    /// <summary>
    /// The two arrowheads, as closed triangles ready to be filled. The tip of
    /// each sits exactly on the end of the dimension line, because that is
    /// the point the measurement is of -- inside or outside.
    /// </summary>
    /// <remarks>
    /// Turned out, they sit beyond the ends and point back in at them, which
    /// is what a drawing does for a feature too small to hold them. The tips
    /// do not move: only which way the bodies go.
    /// </remarks>
    public static IReadOnlyList<IReadOnlyList<Vec2>> Arrowheads(in DimensionGeometry geometry,
        in DimensionFit fit, in DimensionStyle style)
    {
        double size = style.ArrowSize;
        if (size <= 0 || geometry.Measurement <= 0) return [];

        Vec2 along = geometry.Direction;
        Vec2 body = fit.ArrowsOutside ? -along : along;

        return [Arrowhead(geometry.LineStart, body, size), Arrowhead(geometry.LineEnd, -body, size)];
    }

    private static Vec2[] Arrowhead(Vec2 tip, Vec2 inwards, double size)
    {
        Vec2 back = tip + inwards * size;
        Vec2 half = inwards.Perp * (size / 6);

        return [tip, back + half, back - half];
    }
}
