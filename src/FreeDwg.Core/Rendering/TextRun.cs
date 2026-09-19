using FreeDwg.Core.Geometry;

namespace FreeDwg.Core.Rendering;

/// <summary>Which point of the text block sits on the anchor, horizontally.</summary>
public enum TextAnchorX { Left, Center, Right }

/// <summary>Which point of the text block sits on the anchor, vertically.</summary>
public enum TextAnchorY { Baseline, Bottom, Middle, Top }

/// <summary>
/// A block of text handed to the sink for shaping and drawing. Core does not
/// own a font stack, so measurement and glyph layout belong to whichever sink
/// is rendering -- the scene only says what to draw and where to hang it.
/// </summary>
public readonly struct TextRun
{
    public required IReadOnlyList<string> Lines { get; init; }

    /// <summary>Anchor point in the current (possibly block-local) space.</summary>
    public required Vec2 Position { get; init; }

    /// <summary>Capital-letter height in drawing units, as DWG measures text.</summary>
    public required double Height { get; init; }

    public double Rotation { get; init; }

    /// <summary>Horizontal stretch. DWG's TEXT width factor; always 1 for MTEXT.</summary>
    public double WidthFactor { get; init; } = 1.0;

    /// <summary>Slant in radians; DWG's oblique angle.</summary>
    public double ObliqueAngle { get; init; }

    /// <summary>Baseline-to-baseline distance in drawing units.</summary>
    public double LineStep { get; init; }

    public TextAnchorX AnchorX { get; init; } = TextAnchorX.Left;
    public TextAnchorY AnchorY { get; init; } = TextAnchorY.Baseline;

    /// <summary>Wrap width in drawing units; zero means no wrapping.</summary>
    public double WrapWidth { get; init; }

    public string? FontFamily { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }

    public TextRun() { }
}
