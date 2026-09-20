using FreeDwg.Core.Editing;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Picking;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Snapping;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Scene.Entities;

/// <summary>How a dimension decides which way its dimension line runs.</summary>
public enum DimensionKind
{
    /// <summary>
    /// Along a fixed axis, measuring the separation of the two origins along
    /// it. Horizontal and vertical dimensions are both this, at 0 and 90.
    /// </summary>
    Linear,

    /// <summary>
    /// Along the line between the two origins, so it measures the true
    /// distance and stays parallel to whatever it was put against.
    /// </summary>
    Aligned,

    /// <summary>
    /// Out from the centre of a circle or arc to its rim. <c>First</c> is the
    /// centre and <c>Second</c> is a point on the rim, which is what carries
    /// the size; <c>Through</c> is where the number sits.
    /// </summary>
    Radius,

    /// <summary>The same, right across the circle, and twice the number.</summary>
    Diameter,
}

/// <summary>
/// A linear or aligned dimension: two extension lines, a dimension line with
/// an arrowhead at each end, and the measurement written above it.
/// </summary>
/// <remarks>
/// A real entity rather than a block of line work, which is the difference
/// between a dimension and a picture of one. It holds its three points and
/// works the rest out, so moving a grip re-measures and the number follows.
/// <para>
/// Imported DIMENSION objects do <em>not</em> come through here yet: the
/// reader still draws them by exploding their anonymous block, which is
/// exactly right for showing a file and no use for editing one. Dimensions
/// drawn in the editor are these. Reconciling the two is E5's problem, since
/// it is the same problem as writing one back.
/// </para>
/// </remarks>
public sealed class SDimension : SceneEntity
{
    public SDimension(DimensionKind kind, Vec2 first, Vec2 second, Vec2 through, double rotation = 0)
    {
        Kind = kind;
        First = first;
        Second = second;
        Through = through;
        Rotation = rotation;
    }

    public DimensionKind Kind { get; set; }

    /// <summary>Where the first extension line comes from.</summary>
    public Vec2 First { get; set; }

    /// <summary>Where the second extension line comes from.</summary>
    public Vec2 Second { get; set; }

    /// <summary>A point the dimension line passes through: how far out it sits.</summary>
    public Vec2 Through { get; set; }

    /// <summary>
    /// Direction of the dimension line for a <see cref="DimensionKind.Linear"/>
    /// one. Zero measures horizontally, a right angle vertically.
    /// </summary>
    public double Rotation { get; set; }

    /// <summary>
    /// Sizes and number format. Not called Style, because
    /// <see cref="SceneEntity.Style"/> is the colour and width every entity
    /// has and a second property of that name would quietly hide it.
    /// </summary>
    public DimensionStyle DimensionStyle { get; set; } = DimensionStyle.Iso;

    /// <summary>Text to show instead of the measurement, as DWG allows.</summary>
    public string? TextOverride { get; set; }

    /// <summary>
    /// Which way the dimension line actually runs. An aligned dimension reads
    /// it off its own two origins, so dragging one of them keeps it aligned
    /// rather than leaving it measuring an axis it no longer lies on.
    /// </summary>
    public double Direction => Kind == DimensionKind.Aligned
        ? (Second - First).Angle()
        : Rotation;

    /// <summary>Whether this measures a circle rather than a span between two points.</summary>
    public bool IsRadial => Kind is DimensionKind.Radius or DimensionKind.Diameter;

    /// <summary>The size of the circle a radial dimension was put against.</summary>
    public double CircleRadius => Vec2.Distance(First, Second);

    public DimensionGeometry Solve() => IsRadial
        ? DimensionMath.Solve(First, CircleRadius, Through, Kind == DimensionKind.Diameter, DimensionStyle)
        : DimensionMath.Solve(First, Second, Through, Direction, DimensionStyle);

    /// <summary>
    /// What is written above the line, with the mark that says what kind of
    /// measurement it is. A bare number against a circle could be either of
    /// two things, and a drawing that leaves that open is one that gets made
    /// twice.
    /// </summary>
    public string MeasurementText => TextOverride ?? Kind switch
    {
        DimensionKind.Radius => "R" + Number,
        DimensionKind.Diameter => DiameterSign + Number,
        _ => Number,
    };

    /// <summary>
    /// U+00D8, not U+2300. The proper diameter sign is missing from a good
    /// many fonts and arrives as a box; this one is in every Latin font
    /// there is and is what CAD has always drawn in practice.
    /// </summary>
    private const string DiameterSign = "\u00d8";

    private string Number =>
        Units.Format(Solve().Measurement, DimensionStyle.DisplayUnits, DimensionStyle.Decimals);

    // ---- drawing -----------------------------------------------------------

    /// <summary>
    /// The shape of this dimension and what became of its marks, in one
    /// pass. Everything that draws, bounds or picks it starts here, so there
    /// is one answer to where the number ended up.
    /// </summary>
    public (DimensionGeometry Geometry, DimensionFit Fit) Layout()
    {
        var geometry = Solve();

        // A radial dimension is a leader with one arrow on it and nothing to
        // be squeezed between, so nothing ever has to move.
        return (geometry, IsRadial
            ? new DimensionFit(false, false, geometry.TextAnchor, geometry.LineStart, geometry.LineEnd)
            : DimensionMath.Fit(geometry, TextWidth, DimensionStyle));
    }

    /// <summary>How wide the number is, which is what decides whether it fits.</summary>
    private double TextWidth =>
        TextMetrics.Measure(MeasurementText, DimensionStyle.TextHeight, null, false, false);

    public override void Emit(in EmitContext context, in DisplayStyle style)
    {
        var (geometry, fit) = Layout();
        var sink = context.Sink;

        if (!IsRadial)
        {
            Stroke(sink, geometry.FirstExtensionStart, geometry.FirstExtensionEnd, style);
            Stroke(sink, geometry.SecondExtensionStart, geometry.SecondExtensionEnd, style);
        }
        else if (Vec2.Distance(Through, geometry.LineEnd) > 1e-9)
        {
            // The leader carries on out to wherever the number was dropped,
            // which for a small circle is outside it.
            Stroke(sink, geometry.LineEnd, Through, style);
        }

        Stroke(sink, fit.StrokeStart, fit.StrokeEnd, style);

        var arrows = Arrowheads(geometry, fit);
        if (arrows.Count > 0) sink.FillLoops(arrows, style);

        sink.Text(TextRun(geometry, fit.TextAnchor), style);
    }

    private IReadOnlyList<IReadOnlyList<Vec2>> Arrowheads(in DimensionGeometry geometry,
        in DimensionFit fit) => IsRadial
        ? DimensionMath.LeaderArrowheads(geometry, Kind == DimensionKind.Diameter, DimensionStyle)
        : DimensionMath.Arrowheads(geometry, fit, DimensionStyle);

    private static void Stroke(IDrawingSink sink, Vec2 from, Vec2 to, in DisplayStyle style)
    {
        if (Vec2.Distance(from, to) < 1e-12) return;

        sink.BeginFigure(from, closed: false, style);
        sink.LineTo(to);
        sink.EndFigure();
    }

    private TextRun TextRun(in DimensionGeometry geometry, Vec2 at) => new()
    {
        Lines = [MeasurementText],
        Position = at,
        Height = DimensionStyle.TextHeight,
        LineStep = DimensionStyle.TextHeight,
        Rotation = geometry.TextRotation,
        AnchorX = TextAnchorX.Center,

        // The block stands on its anchor, which is a gap above the line.
        AnchorY = TextAnchorY.Bottom,
    };

    // ---- measurement -------------------------------------------------------

    /// <summary>
    /// The box the number occupies, as a segment through its middle and a
    /// half-height either side. Enough to pick it and to bound it, without
    /// Core needing to know what a glyph is.
    /// </summary>
    private (Vec2 From, Vec2 To, double HalfHeight) TextExtent(in DimensionGeometry geometry, Vec2 at)
    {
        double height = DimensionStyle.TextHeight;
        double width = TextWidth;

        Vec2 along = new(Math.Cos(geometry.TextRotation), Math.Sin(geometry.TextRotation));

        // The anchor is the foot of the block, so its middle is half a line
        // further up.
        Vec2 middle = at + along.Perp * (height / 2);

        return (middle - along * (width / 2), middle + along * (width / 2), height / 2);
    }

    protected override Bounds2 ComputeBounds()
    {
        var (geometry, fit) = Layout();

        var bounds = Bounds2.Empty
            .Union(geometry.FirstExtensionStart).Union(geometry.FirstExtensionEnd)
            .Union(geometry.SecondExtensionStart).Union(geometry.SecondExtensionEnd)
            .Union(fit.StrokeStart).Union(fit.StrokeEnd)
            .Union(Through);

        // The corners of the number's box, rather than padding the whole
        // dimension by half a line: a bound that is bigger than what is drawn
        // is a bound that keeps the entity on screen after it has left.
        var (from, to, half) = TextExtent(geometry, fit.TextAnchor);
        Vec2 up = (to - from).LengthSquared > 0
            ? (to - from).Normalized().Perp * half
            : new Vec2(0, half);

        return bounds.Union(from + up).Union(from - up).Union(to + up).Union(to - up);
    }

    public override double DistanceTo(Vec2 point, in PickContext context)
    {
        var (geometry, fit) = Layout();

        double best = Math.Min(
            Distance.PointToSegment(point, fit.StrokeStart, fit.StrokeEnd),
            IsRadial
                ? Distance.PointToSegment(point, geometry.LineEnd, Through)
                : Math.Min(
                    Distance.PointToSegment(point, geometry.FirstExtensionStart, geometry.FirstExtensionEnd),
                    Distance.PointToSegment(point, geometry.SecondExtensionStart, geometry.SecondExtensionEnd)));

        // The number is part of the dimension and is often the only part
        // with any room around it, so clicking it has to pick the object.
        var (from, to, half) = TextExtent(geometry, fit.TextAnchor);
        double toText = Distance.PointToSegment(point, from, to) - half;

        return Math.Min(best, Math.Max(toText, 0));
    }

    public override bool IntersectsRect(Bounds2 rect, in PickContext context)
    {
        var (geometry, fit) = Layout();

        if (Intersect.SegmentWithRect(fit.StrokeStart, fit.StrokeEnd, rect)) return true;

        if (IsRadial)
        {
            if (Intersect.SegmentWithRect(geometry.LineEnd, Through, rect)) return true;
        }
        else
        {
            if (Intersect.SegmentWithRect(geometry.FirstExtensionStart, geometry.FirstExtensionEnd, rect)) return true;
            if (Intersect.SegmentWithRect(geometry.SecondExtensionStart, geometry.SecondExtensionEnd, rect)) return true;
        }

        var (from, to, _) = TextExtent(geometry, fit.TextAnchor);
        return Intersect.SegmentWithRect(from, to, rect);
    }

    public override void CollectSnapPoints(SnapModes modes, ICollection<SnapCandidate> into)
    {
        var geometry = Solve();

        if (IsRadial)
        {
            if (modes.HasFlag(SnapModes.Center)) into.Add(new SnapCandidate(First, SnapKind.Center));
            if (modes.HasFlag(SnapModes.Endpoint)) into.Add(new SnapCandidate(geometry.LineEnd, SnapKind.Endpoint));
            return;
        }

        if (modes.HasFlag(SnapModes.Endpoint))
        {
            // The origins first: they are the points the dimension was put
            // against, and the ones worth lining another one up with.
            into.Add(new SnapCandidate(First, SnapKind.Endpoint));
            into.Add(new SnapCandidate(Second, SnapKind.Endpoint));
            into.Add(new SnapCandidate(geometry.LineStart, SnapKind.Endpoint));
            into.Add(new SnapCandidate(geometry.LineEnd, SnapKind.Endpoint));
        }

        if (modes.HasFlag(SnapModes.Midpoint))
            into.Add(new SnapCandidate(Vec2.Lerp(geometry.LineStart, geometry.LineEnd, 0.5), SnapKind.Midpoint));
    }

    public override void CollectCurves(ICollection<CurvePiece> into, double tolerance)
    {
        var geometry = Solve();

        // The lines as drawn, so an intersection snap works against a
        // dimension. Trimming one is another matter: nothing here knows how
        // to rebuild a dimension from a piece of its line work, and it would
        // not be a dimension afterwards if it did.
        into.Add(CurvePiece.Segment(geometry.LineStart, geometry.LineEnd));

        if (IsRadial)
        {
            into.Add(CurvePiece.Segment(geometry.LineEnd, Through));
            return;
        }

        into.Add(CurvePiece.Segment(geometry.FirstExtensionStart, geometry.FirstExtensionEnd));
        into.Add(CurvePiece.Segment(geometry.SecondExtensionStart, geometry.SecondExtensionEnd));
    }

    public override void CollectGrips(ICollection<Grip> into)
    {
        if (IsRadial)
        {
            // The centre moves the whole thing, and the text point swings
            // the leader round the rim. The rim itself is not offered: it is
            // the size of the circle being measured, and a dimension is not
            // where you go to resize the thing it measures.
            into.Add(new Grip(First, GripRole.Move));
            into.Add(new Grip(Through, GripRole.Shape, 2));
            return;
        }

        // The three points it is made of, which is exactly what AutoCAD
        // offers: the two origins re-measure it, and the third slides the
        // line in or out.
        into.Add(new Grip(First, GripRole.Shape, 0));
        into.Add(new Grip(Second, GripRole.Shape, 1));

        var geometry = Solve();
        into.Add(new Grip(Vec2.Lerp(geometry.LineStart, geometry.LineEnd, 0.5), GripRole.Shape, 2));
    }

    protected override bool MoveGripGeometry(in Grip grip, Vec2 to)
    {
        switch (grip.Index)
        {
            case 0: First = to; return true;
            case 1: Second = to; return true;

            // The dimension line is defined by a point it passes through, so
            // sliding it is simply saying where that point now is.
            case 2: Through = to; return true;

            default: return false;
        }
    }

    protected override void TransformGeometry(in Mat3 transform)
    {
        // The direction goes across as a vector so that a rotation carries
        // the axis of a linear dimension with it, and a mirror turns it
        // over. An aligned one reads its direction off the origins and needs
        // none of this.
        Vec2 axis = transform.TransformVector(new Vec2(Math.Cos(Rotation), Math.Sin(Rotation)));

        First = transform.Transform(First);
        Second = transform.Transform(Second);
        Through = transform.Transform(Through);

        if (axis.LengthSquared > 0) Rotation = axis.Angle();

        // Text and arrows are lengths in drawing units like any other
        // geometry, so a scaled dimension is drawn at the scaled size rather
        // than keeping marks sized for the drawing it came from.
        DimensionStyle = DimensionStyle.Scaled(transform.UniformScale);
    }
}
