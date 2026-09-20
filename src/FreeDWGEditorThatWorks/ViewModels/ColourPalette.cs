using System.Collections.Generic;
using FreeDwg.Core.Styling;

namespace FreeDWGEditorThatWorks.ViewModels;

/// <summary>
/// The grid of colours the picker offers.
/// </summary>
/// <remarks>
/// Generated from hue and lightness rather than typed out, so the rows are
/// even and no value is a transcription error. AutoCAD's own 255-entry colour
/// index would be the authentic choice, but Core resolves indices to 24-bit
/// colour at import and has nowhere to put one back, so reproducing that
/// table from memory would risk being subtly wrong about colours nobody could
/// then correct.
/// <para>
/// The middle lightness row is fully saturated, which is what puts pure red,
/// yellow, green, cyan, blue and magenta in the palette -- the same colours
/// the dropdown names, so the two agree.
/// </para>
/// </remarks>
public static class ColourPalette
{
    public const int Columns = 12;

    /// <summary>Lightness bands, darkest first. The middle one is the pure hue.</summary>
    private static readonly double[] Lightness = [0.25, 0.375, 0.5, 0.65, 0.8];

    /// <summary>A neutral ramp, with mid grey and both ends exact.</summary>
    private static readonly byte[] Greys = [0, 32, 64, 96, 128, 160, 176, 192, 208, 224, 240, 255];

    public static IReadOnlyList<Rgb> Swatches { get; } = Build();

    private static Rgb[] Build()
    {
        var swatches = new List<Rgb>(Lightness.Length * Columns + Greys.Length);

        foreach (double lightness in Lightness)
            for (int column = 0; column < Columns; column++)
                swatches.Add(FromHsl(column * 360.0 / Columns, 1.0, lightness));

        foreach (byte grey in Greys) swatches.Add(new Rgb(grey, grey, grey));

        return swatches.ToArray();
    }

    /// <summary>
    /// Hue in degrees, saturation and lightness in 0..1, to 24-bit colour.
    /// </summary>
    public static Rgb FromHsl(double hue, double saturation, double lightness)
    {
        double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double sector = ((hue % 360) + 360) % 360 / 60.0;
        double second = chroma * (1 - Math.Abs(sector % 2 - 1));
        double offset = lightness - chroma / 2;

        (double r, double g, double b) = (int)sector switch
        {
            0 => (chroma, second, 0.0),
            1 => (second, chroma, 0.0),
            2 => (0.0, chroma, second),
            3 => (0.0, second, chroma),
            4 => (second, 0.0, chroma),
            _ => (chroma, 0.0, second),
        };

        return new Rgb(Channel(r + offset), Channel(g + offset), Channel(b + offset));
    }

    private static byte Channel(double value) =>
        (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
}
