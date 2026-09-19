using FreeDwg.Core.Geometry;

namespace FreeDwg.Core.Snapping;

/// <summary>
/// The drawing grid: what is drawn behind the scene, and what the cursor
/// snaps to when nothing better is under it.
/// </summary>
/// <remarks>
/// AutoCAD keeps GRID and SNAP apart, so the dots you see and the points you
/// land on can have different spacings. That is a pair of settings to explain
/// and to get wrong; here there is one spacing and you snap to the grid you
/// can see. The only subtlety is that the grid you see is not always the
/// grid you set: zoom out far enough and drawing every line would be a
/// million lines and a grey screen, so the spacing steps up by decades.
/// </remarks>
public sealed class Grid
{
    /// <summary>Spacing in drawing units at 1:1. Steps up by decades as you zoom out.</summary>
    public double Spacing { get; set; } = 10.0;

    public bool IsVisible { get; set; }

    /// <summary>Closest two grid lines may come on screen before the grid thins out.</summary>
    public double MinPixels { get; set; } = 8.0;

    /// <summary>A hard cap on lines per axis, for the frame where the maths goes wrong.</summary>
    public const int MaxLinesPerAxis = 400;

    /// <summary>
    /// The spacing actually drawn and snapped to at this zoom: the base
    /// spacing multiplied by whatever power of ten keeps the lines at least
    /// <see cref="MinPixels"/> apart.
    /// </summary>
    public double EffectiveSpacing(double pixelsPerUnit)
    {
        if (Spacing <= 0 || pixelsPerUnit <= 0 || double.IsNaN(pixelsPerUnit)) return Spacing;

        double spacing = Spacing;

        // Decades rather than doubling, so the numbers on the grid stay round.
        while (spacing * pixelsPerUnit < MinPixels && spacing < 1e18) spacing *= 10;
        while (spacing * pixelsPerUnit > MinPixels * 100 && spacing > 1e-18) spacing /= 10;

        return spacing;
    }

    /// <summary>The nearest grid intersection to a world point.</summary>
    public static Vec2 Nearest(Vec2 world, double spacing) => spacing <= 0
        ? world
        : new Vec2(Math.Round(world.X / spacing) * spacing, Math.Round(world.Y / spacing) * spacing);

    /// <summary>
    /// The grid coordinates crossing a range, for drawing. Returns nothing
    /// rather than a runaway list when the spacing cannot cover the range.
    /// </summary>
    public static IEnumerable<double> LinesAcross(double min, double max, double spacing)
    {
        if (spacing <= 0 || max <= min) yield break;
        if ((max - min) / spacing > MaxLinesPerAxis) yield break;

        double first = Math.Ceiling(min / spacing) * spacing;
        for (double at = first; at <= max; at += spacing) yield return at;
    }
}

/// <summary>
/// Constrains a point to lie square with the one before it -- AutoCAD's
/// ORTHO, and the reason most drawings are made of horizontal and vertical
/// lines rather than lines that are nearly horizontal and nearly vertical.
/// </summary>
public static class Ortho
{
    /// <summary>
    /// Drops <paramref name="to"/> onto whichever axis through
    /// <paramref name="from"/> it is already closer to.
    /// </summary>
    public static Vec2 Constrain(Vec2 from, Vec2 to)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;

        return Math.Abs(dx) >= Math.Abs(dy)
            ? new Vec2(to.X, from.Y)
            : new Vec2(from.X, to.Y);
    }
}
