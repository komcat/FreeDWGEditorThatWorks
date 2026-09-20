using FreeDwg.Core.Editing;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Picking;
using FreeDwg.Core.Snapping;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Scene.Entities;

/// <summary>
/// A block of text. Covers both DWG's TEXT (one line, a width factor and an
/// oblique angle) and MTEXT (several lines, an attachment point and a wrap
/// width), because once alignment is expressed as a nine-way anchor the two
/// only differ in which fields are set.
/// </summary>
public sealed class SText : SceneEntity
{
    public SText(IReadOnlyList<string> lines, Vec2 position, double height)
    {
        Lines = lines;
        Position = position;
        Height = height;
        LineStep = height;
    }

    public IReadOnlyList<string> Lines { get; set; }

    /// <summary>Anchor point; which part of the block sits here is set by the anchors.</summary>
    public Vec2 Position { get; set; }

    /// <summary>Capital-letter height in drawing units, as DWG measures text.</summary>
    public double Height { get; set; }

    public double Rotation { get; set; }
    public double WidthFactor { get; set; } = 1.0;
    public double ObliqueAngle { get; set; }

    /// <summary>Baseline-to-baseline distance in drawing units.</summary>
    public double LineStep { get; set; }

    public TextAnchorX AnchorX { get; set; } = TextAnchorX.Left;
    public TextAnchorY AnchorY { get; set; } = TextAnchorY.Baseline;

    /// <summary>MTEXT's reference rectangle width; zero means no wrapping.</summary>
    public double WrapWidth { get; set; }

    public string? FontFamily { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }

    /// <summary>Descender allowance below the baseline, as a fraction of cap height.</summary>
    private const double DescenderRatio = 0.25;

    /// <summary>
    /// The block's box with the first baseline at y = 0 and no rotation applied.
    /// Bounds is this transformed; picking works in here instead, so that
    /// rotated text is hit on the text and not on the corners of its
    /// axis-aligned bounding box.
    /// </summary>
    private Bounds2 LocalBox()
    {
        double width = TextMetrics.MaxLineWidth(Lines, Height, FontFamily, Bold, Italic) * WidthFactor;

        int lineCount = Lines.Count;
        if (WrapWidth > 0 && width > WrapWidth)
        {
            // Wrapping happens in the sink, which can measure properly; for
            // bounds it is enough to know roughly how many lines it becomes.
            lineCount = 0;
            foreach (string line in Lines)
            {
                double lineWidth = TextMetrics.Measure(line, Height, FontFamily, Bold, Italic) * WidthFactor;
                lineCount += Math.Max(1, (int)Math.Ceiling(lineWidth / WrapWidth));
            }
            width = WrapWidth;
        }

        double blockHeight = Height + (lineCount - 1) * LineStep;

        // Local box with the baseline of the first line at y = 0.
        double left = AnchorX switch
        {
            TextAnchorX.Center => -width / 2,
            TextAnchorX.Right => -width,
            _ => 0,
        };

        double top = AnchorY switch
        {
            TextAnchorY.Top => 0,
            TextAnchorY.Middle => blockHeight / 2,
            TextAnchorY.Bottom => blockHeight,
            _ => Height,   // baseline of the first line
        };

        double bottom = top - blockHeight - Height * DescenderRatio;

        return Bounds2.FromCorners(new Vec2(left, bottom), new Vec2(left + width, top));
    }

    /// <summary>Text-local space to the space this entity lives in.</summary>
    private Mat3 Placement => Mat3.Rotation(Rotation) * Mat3.Translation(Position);

    protected override Bounds2 ComputeBounds()
    {
        if (Lines.Count == 0 || Height <= 0) return Bounds2.FromPoint(Position);
        return Placement.TransformBounds(LocalBox());
    }

    public override void Emit(in EmitContext context, in DisplayStyle style)
    {
        if (Lines.Count == 0 || Height <= 0) return;

        context.Sink.Text(new TextRun
        {
            Lines = Lines,
            Position = Position,
            Height = Height,
            Rotation = Rotation,
            WidthFactor = WidthFactor,
            ObliqueAngle = ObliqueAngle,
            LineStep = LineStep,
            AnchorX = AnchorX,
            AnchorY = AnchorY,
            WrapWidth = WrapWidth,
            FontFamily = FontFamily,
            Bold = Bold,
            Italic = Italic,
        }, style);
    }

    /// <summary>
    /// Text is picked by its box, filled: dropping to the glyph outlines
    /// would mean clicking the hole in an 'o' selected the line behind it.
    /// </summary>
    public override double DistanceTo(Vec2 point, in PickContext context)
    {
        if (Lines.Count == 0 || Height <= 0) return Vec2.Distance(point, Position);
        if (!Placement.TryInvert(out var toLocal)) return double.PositiveInfinity;

        return Distance.PointToRect(toLocal.Transform(point), LocalBox());
    }

    public override bool IntersectsRect(Bounds2 rect, in PickContext context)
    {
        if (Lines.Count == 0 || Height <= 0) return rect.Contains(Position);
        if (!Bounds.Intersects(rect)) return false;

        // Compare in text-local space, where the box is axis aligned and the
        // rectangle is the rotated one.
        if (!Placement.TryInvert(out var toLocal)) return false;

        var box = LocalBox();
        var corners = Distance.Corners(rect);
        for (int i = 0; i < corners.Length; i++) corners[i] = toLocal.Transform(corners[i]);

        if (Intersect.PolylineWithRect(corners, closed: true, box)) return true;

        // No edge crossed: either the rectangle swallows the text or misses it.
        return box.Contains(corners[0]) || Distance.PointInPolygon(box.Center, corners);
    }

    public override void CollectSnapPoints(SnapModes modes, ICollection<SnapCandidate> into)
    {
        // The insertion point, which is the only point on a text object that
        // means anything: the glyph outlines are the font's business.
        if (modes.HasFlag(SnapModes.Endpoint))
            into.Add(new SnapCandidate(Position, SnapKind.Endpoint));
    }

    public override void CollectGrips(ICollection<Grip> into) =>
        // The insertion point, and only that. Height, rotation and the rest
        // are numbers rather than places, and the properties panel edits
        // them where they can be typed exactly.
        into.Add(new Grip(Position, GripRole.Move));

    protected override void TransformGeometry(in Mat3 transform)
    {
        Position = transform.Transform(Position);

        // Height is in drawing units, so it scales; rotation comes from what
        // the transform does to the text's own baseline direction.
        double scale = transform.UniformScale;
        Height *= scale;
        LineStep *= scale;

        Vec2 baseline = transform.TransformVector(new Vec2(Math.Cos(Rotation), Math.Sin(Rotation)));
        if (baseline.LengthSquared > 0) Rotation = baseline.Angle();
    }

    protected override void CloneGeometry() => Lines = Lines.ToArray();

    public override void CollectCurves(ICollection<CurvePiece> into, double tolerance)
    {
        // Nothing. Glyph outlines belong to the font, and trimming a line to
        // the side of a letter is not an operation anyone wants.
    }
}
