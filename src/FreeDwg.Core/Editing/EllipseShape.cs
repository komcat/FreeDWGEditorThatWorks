using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene.Entities;

namespace FreeDwg.Core.Editing;

/// <summary>
/// An ellipse read as its width and height -- the full distances across it
/// -- rather than a major axis vector and a ratio.
/// </summary>
/// <remarks>
/// The ratio is how DWG stores an ellipse and nobody's idea of how big one
/// is. "Width" is the axis that runs more nearly horizontal, as for a
/// rectangle, so the names match what is on screen whichever axis happens to
/// be the major one.
/// </remarks>
public static class EllipseShape
{
    /// <summary>The two axes as vectors from the centre: the major, and the minor it implies.</summary>
    private static (Vec2 Major, Vec2 Minor) Axes(SEllipse ellipse) =>
        (ellipse.MajorAxis, EllipseMath.MinorAxis(ellipse.MajorAxis, ellipse.Ratio));

    /// <summary>Whether the major axis is the one called the width.</summary>
    private static bool MajorIsWidth(Vec2 major, Vec2 minor) =>
        Math.Abs(major.X) * minor.Length >= Math.Abs(minor.X) * major.Length;

    public static double Width(SEllipse ellipse)
    {
        var (major, minor) = Axes(ellipse);
        return 2 * (MajorIsWidth(major, minor) ? major : minor).Length;
    }

    public static double Height(SEllipse ellipse)
    {
        var (major, minor) = Axes(ellipse);
        return 2 * (MajorIsWidth(major, minor) ? minor : major).Length;
    }

    /// <summary>
    /// Gives the ellipse a new width, height or both, about its centre and
    /// keeping its angle. False for a size that is not a size.
    /// </summary>
    /// <remarks>
    /// Made to cross over -- the minor axis typed longer than the major --
    /// the ellipse is re-expressed with the other axis as its major, because
    /// a ratio above one is something no DWG reader expects. An elliptical
    /// arc's span is measured from the major axis, so it turns back a quarter
    /// to stay on the same stretch of curve.
    /// </remarks>
    public static bool Resize(SEllipse ellipse, double? width, double? height)
    {
        if (width is <= 0 || height is <= 0) return false;

        var (major, minor) = Axes(ellipse);
        if (major.LengthSquared <= 0 || minor.LengthSquared <= 0) return false;

        bool majorIsWidth = MajorIsWidth(major, minor);
        double majorLength = (majorIsWidth ? width : height) is { } m ? m / 2 : major.Length;
        double minorLength = (majorIsWidth ? height : width) is { } n ? n / 2 : minor.Length;

        if (majorLength >= minorLength)
        {
            ellipse.MajorAxis = major.Normalized() * majorLength;
            ellipse.Ratio = minorLength / majorLength;
        }
        else
        {
            ellipse.MajorAxis = minor.Normalized() * minorLength;
            ellipse.Ratio = majorLength / minorLength;
            ellipse.StartParameter -= Math.PI / 2;
        }

        ellipse.InvalidateBounds();
        return true;
    }
}
