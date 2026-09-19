using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace FreeDWGEditorThatWorks.Rendering;

/// <summary>
/// Font lookup and measurement for the WPF sink, and the measurer Core uses
/// for text bounds.
/// </summary>
public static class WpfText
{
    /// <summary>
    /// Fallback ratio of capital height to em size, for a typeface whose real
    /// metrics cannot be read. DWG sizes text by cap height; fonts are sized
    /// by em, and the two differ by roughly this much for a normal sans.
    /// </summary>
    public const double FallbackCapHeightRatio = 0.72;

    /// <summary>Em size used for cached measurement, scaled afterwards.</summary>
    private const double ReferenceEmSize = 100.0;

    private static readonly Dictionary<(string?, bool, bool), Typeface> TypefaceCache = new();
    private static readonly Dictionary<(string?, bool, bool), double> CapHeightCache = new();
    private static readonly Dictionary<(string, string?, bool, bool), double> WidthCache = new();

    private static readonly object Gate = new();

    public static Typeface GetTypeface(string? family, bool bold, bool italic)
    {
        var key = (family, bold, italic);
        lock (Gate)
        {
            if (TypefaceCache.TryGetValue(key, out var cached)) return cached;

            var typeface = new Typeface(
                new FontFamily(string.IsNullOrWhiteSpace(family) ? "Arial" : family),
                italic ? FontStyles.Italic : FontStyles.Normal,
                bold ? FontWeights.Bold : FontWeights.Normal,
                FontStretches.Normal);

            TypefaceCache[key] = typeface;
            return typeface;
        }
    }

    /// <summary>Capital height as a fraction of em, read from the font where possible.</summary>
    public static double CapHeightRatio(string? family, bool bold, bool italic)
    {
        var key = (family, bold, italic);
        lock (Gate)
        {
            if (CapHeightCache.TryGetValue(key, out double cached)) return cached;

            double ratio = FallbackCapHeightRatio;
            if (GetTypeface(family, bold, italic).TryGetGlyphTypeface(out var glyphs) && glyphs.CapsHeight > 0)
                ratio = glyphs.CapsHeight;

            CapHeightCache[key] = ratio;
            return ratio;
        }
    }

    /// <summary>Em size that yields the requested DWG cap height.</summary>
    public static double EmSizeFor(double capHeight, string? family, bool bold, bool italic) =>
        capHeight / CapHeightRatio(family, bold, italic);

    /// <summary>
    /// Advance width of a string at a given DWG cap height, in the same units.
    /// </summary>
    /// <remarks>
    /// Measured once at a reference em size and scaled, both to cache across
    /// zoom levels and to keep away from the precision floor -- drawings in
    /// metres routinely carry text a few thousandths of a unit high.
    /// </remarks>
    public static double MeasureWidth(string text, double capHeight, string? family, bool bold, bool italic)
    {
        if (string.IsNullOrEmpty(text) || capHeight <= 0) return 0;

        double referenceWidth;
        var key = (text, family, bold, italic);

        lock (Gate)
        {
            if (!WidthCache.TryGetValue(key, out referenceWidth))
            {
                var formatted = new FormattedText(text, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, GetTypeface(family, bold, italic),
                    ReferenceEmSize, Brushes.Black, pixelsPerDip: 1.0);

                referenceWidth = formatted.WidthIncludingTrailingWhitespace;

                // Unbounded growth would be a slow leak on a text-heavy drawing.
                if (WidthCache.Count < 20_000) WidthCache[key] = referenceWidth;
            }
        }

        return referenceWidth * EmSizeFor(capHeight, family, bold, italic) / ReferenceEmSize;
    }
}
