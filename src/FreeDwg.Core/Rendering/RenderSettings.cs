using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Rendering;

public sealed class RenderSettings
{
    /// <summary>Model-space background. Dark by default, as CAD tools conventionally are.</summary>
    public Rgb Background { get; set; } = new(33, 40, 48);

    /// <summary>
    /// Whether plot widths are shown on screen. AutoCAD's LWDISPLAY defaults to
    /// off, and so do we: everything strokes as a hairline until asked otherwise.
    /// </summary>
    public bool ShowLineweights { get; set; }

    /// <summary>Device pixels per millimetre used when <see cref="ShowLineweights"/> is on.</summary>
    public double PixelsPerMillimeter { get; set; } = 96.0 / 25.4;

    public double MinLineWidthPixels { get; set; } = 1.0;

    /// <summary>
    /// Text smaller than this on screen is skipped. Below a few pixels it is
    /// an unreadable smudge, and a drawing full of annotation would spend most
    /// of a frame shaping glyphs nobody can read.
    /// </summary>
    public double MinTextHeightPixels { get; set; } = 3.0;

    /// <summary>
    /// Dash patterns shorter than this on screen are drawn solid. A pattern
    /// compressed below a couple of pixels is indistinguishable from a solid
    /// line but far more expensive, and aliases badly while zooming.
    /// </summary>
    public double MinDashPatternPixels { get; set; } = 3.0;

    /// <summary>
    /// Flip pure black and pure white to stay legible against the background.
    /// DWG colour index 7 means "whatever contrasts with the background", and
    /// every CAD viewer honours that; by this point the index is long gone, so
    /// we key off the resolved colour instead.
    /// </summary>
    public bool ContrastMonochrome { get; set; } = true;

    public DisplayStyle Adapt(in DisplayStyle style)
    {
        if (!ContrastMonochrome) return style;

        bool backgroundIsDark = Background.Luminance < 0.5;
        if (backgroundIsDark && style.Color == Rgb.Black) return style with { Color = Rgb.White };
        if (!backgroundIsDark && style.Color == Rgb.White) return style with { Color = Rgb.Black };
        return style;
    }

    /// <summary>Stroke width in device pixels for a resolved style.</summary>
    public double StrokeWidthPixels(in DisplayStyle style)
    {
        if (!ShowLineweights) return MinLineWidthPixels;
        return Math.Max(MinLineWidthPixels, style.Lineweight.Millimeters * PixelsPerMillimeter);
    }
}
