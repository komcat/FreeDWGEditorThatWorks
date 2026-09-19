namespace FreeDwg.Core.Rendering;

/// <summary>Measures the advance width of a string at a given cap height.</summary>
public delegate double TextWidthMeasurer(string text, double height, string? fontFamily, bool bold, bool italic);

/// <summary>
/// How Core estimates text width for bounds and zoom-extents.
/// </summary>
/// <remarks>
/// Core has no font stack, so by default this is a crude per-character
/// estimate -- good enough for culling, but it would put zoom-extents visibly
/// off on a text-heavy drawing. The shell installs a real measurer at startup;
/// this is a process-wide hook rather than a parameter because bounds are
/// computed lazily, far from any rendering call.
/// </remarks>
public static class TextMetrics
{
    /// <summary>Mean glyph advance as a fraction of cap height, for the fallback estimate.</summary>
    public const double AverageAdvanceRatio = 0.62;

    public static TextWidthMeasurer Measure { get; set; } = Estimate;

    public static double Estimate(string text, double height, string? fontFamily, bool bold, bool italic) =>
        text.Length * height * AverageAdvanceRatio;

    /// <summary>Width of the widest line in a block.</summary>
    public static double MaxLineWidth(IReadOnlyList<string> lines, double height, string? fontFamily, bool bold, bool italic)
    {
        double widest = 0;
        foreach (string line in lines)
            widest = Math.Max(widest, Measure(line, height, fontFamily, bold, italic));
        return widest;
    }
}
