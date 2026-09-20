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

/// <summary>
/// Rays at regular angles out of a point, and where they cross.
/// </summary>
/// <remarks>
/// The generalisation of ortho: ortho is these rays at ninety degrees, with
/// the point forced onto one. Polar attracts rather than forces, so the
/// cursor is free between the angles and only jumps when it is close to one.
/// </remarks>
public static class Polar
{
    /// <summary>Forty-five degrees: the diagonals as well as the axes.</summary>
    public const double DefaultIncrement = 45;

    /// <summary>
    /// The smallest increment offered. Below this the rays are closer
    /// together than the pick tolerance at any useful zoom, so every one of
    /// them would be in reach at once and none would mean anything.
    /// </summary>
    public const double MinIncrement = 5;

    public static int Count(double increment) =>
        (int)Math.Round(360.0 / Math.Max(increment, MinIncrement));

    public static Vec2 Direction(double increment, int step)
    {
        double radians = step * Math.Max(increment, MinIncrement) * Math.PI / 180.0;

        // Cleaned on the axes. cos(pi/2) is 6e-17 rather than nought, and a
        // vertical ray carrying that drifts sideways by a rounding step for
        // every unit of its length. Snapping exists to make points land
        // exactly, and the axes are the case people lean on hardest.
        double x = Math.Cos(radians);
        double y = Math.Sin(radians);

        return new Vec2(Math.Abs(x) < 1e-12 ? 0 : x, Math.Abs(y) < 1e-12 ? 0 : y);
    }

    /// <summary>
    /// The point on the ray from <paramref name="origin"/> nearest the
    /// cursor, when the cursor is within <paramref name="tolerance"/> of it.
    /// </summary>
    /// <remarks>
    /// Only forwards along the ray. Backwards is a different angle, and with
    /// an increment that divides 360 it is already one of the other rays, so
    /// allowing it would offer the same line twice.
    /// </remarks>
    public static bool Project(Vec2 origin, Vec2 direction, Vec2 cursor, double tolerance,
        out Vec2 point, out double offset)
    {
        point = origin;
        offset = double.PositiveInfinity;

        Vec2 reach = cursor - origin;
        double along = Vec2.Dot(reach, direction);
        if (along <= 0) return false;

        point = origin + direction * along;
        offset = Vec2.Distance(point, cursor);

        return offset <= tolerance;
    }

    /// <summary>Where two rays cross, if they do and it is ahead on both.</summary>
    public static bool Cross(Vec2 originA, Vec2 directionA, Vec2 originB, Vec2 directionB, out Vec2 point)
    {
        point = originA;

        double denominator = Vec2.Cross(directionA, directionB);
        if (Math.Abs(denominator) < 1e-12) return false;

        Vec2 gap = originB - originA;
        double alongA = Vec2.Cross(gap, directionB) / denominator;
        double alongB = Vec2.Cross(gap, directionA) / denominator;

        if (alongA <= 0 || alongB <= 0) return false;

        point = originA + directionA * alongA;
        return true;
    }
}
