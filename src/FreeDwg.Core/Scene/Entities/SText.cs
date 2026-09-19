using FreeDwg.Core.Geometry;
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

    protected override Bounds2 ComputeBounds()
    {
        if (Lines.Count == 0 || Height <= 0) return Bounds2.FromPoint(Position);

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

        var local = Bounds2.FromCorners(new Vec2(left, bottom), new Vec2(left + width, top));
        var placement = Mat3.Rotation(Rotation) * Mat3.Translation(Position);
        return placement.TransformBounds(local);
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
}
