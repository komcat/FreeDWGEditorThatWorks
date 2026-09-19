using FreeDwg.Core.Geometry;

namespace FreeDwg.Core.Rendering;

/// <summary>
/// Maps world coordinates to device pixels. World is Y-up, device is Y-down.
/// The transform lives here and nowhere else -- geometry in the scene is never
/// rewritten by panning or zooming.
/// </summary>
public sealed class Camera
{
    /// <summary>World point displayed at the centre of the viewport.</summary>
    public Vec2 Center { get; set; } = Vec2.Zero;

    /// <summary>Device pixels per world unit. Always positive.</summary>
    public double Scale { get; set; } = 1.0;

    public double ViewportWidth { get; set; }
    public double ViewportHeight { get; set; }

    public const double MinScale = 1e-9;
    public const double MaxScale = 1e9;

    public Vec2 WorldToScreen(Vec2 world) => new(
        (world.X - Center.X) * Scale + ViewportWidth * 0.5,
        ViewportHeight * 0.5 - (world.Y - Center.Y) * Scale);

    public Vec2 ScreenToWorld(Vec2 screen) => new(
        Center.X + (screen.X - ViewportWidth * 0.5) / Scale,
        Center.Y - (screen.Y - ViewportHeight * 0.5) / Scale);

    /// <summary>World-space transform to device space, for sinks that want a matrix.</summary>
    public Mat3 WorldToScreenMatrix =>
        Mat3.Translation(-Center)
        * Mat3.Scaling(Scale, -Scale)
        * Mat3.Translation(ViewportWidth * 0.5, ViewportHeight * 0.5);

    /// <summary>The world rectangle currently on screen, used for culling.</summary>
    public Bounds2 VisibleWorldBounds => Bounds2.FromCorners(
        ScreenToWorld(new Vec2(0, ViewportHeight)),
        ScreenToWorld(new Vec2(ViewportWidth, 0)));

    /// <summary>Frames <paramref name="bounds"/> with a little breathing room.</summary>
    public void ZoomToFit(Bounds2 bounds, double paddingFraction = 0.04)
    {
        if (ViewportWidth <= 0 || ViewportHeight <= 0) return;

        if (bounds.IsEmpty)
        {
            Center = Vec2.Zero;
            Scale = 1.0;
            return;
        }

        Center = bounds.Center;

        double pad = 1.0 + 2.0 * paddingFraction;
        double w = bounds.Width * pad;
        double h = bounds.Height * pad;

        // A drawing can legitimately be a single point, or a perfectly
        // horizontal line with zero height; fall back on the other axis.
        double scaleX = w > 0 ? ViewportWidth / w : double.PositiveInfinity;
        double scaleY = h > 0 ? ViewportHeight / h : double.PositiveInfinity;
        double scale = Math.Min(scaleX, scaleY);

        Scale = double.IsInfinity(scale) ? 1.0 : Math.Clamp(scale, MinScale, MaxScale);
    }

    /// <summary>Zooms while keeping the world point under the cursor pinned.</summary>
    public void ZoomAtScreenPoint(Vec2 screen, double factor)
    {
        double target = Math.Clamp(Scale * factor, MinScale, MaxScale);
        if (target == Scale) return;

        Vec2 before = ScreenToWorld(screen);
        Scale = target;
        Vec2 after = ScreenToWorld(screen);
        Center += before - after;
    }

    public void PanByScreenDelta(double dxPixels, double dyPixels)
    {
        Center = new Vec2(Center.X - dxPixels / Scale, Center.Y + dyPixels / Scale);
    }
}
