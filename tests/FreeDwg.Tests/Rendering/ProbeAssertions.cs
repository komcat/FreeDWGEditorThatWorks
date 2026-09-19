using FreeDwg.Core.Geometry;

namespace FreeDwg.Tests.Rendering;

/// <summary>
/// Assertions over a rendered frame, phrased in drawing coordinates.
/// </summary>
public static class ProbeAssertions
{
    public static void AssertInk(this Probe probe, double x, double y, string what)
    {
        var point = new Vec2(x, y);
        Assert.True(probe.HasInk(point), $"{what}: expected ink at {point}, found none.");
    }

    public static void AssertBlank(this Probe probe, double x, double y, string what)
    {
        var point = new Vec2(x, y);
        Assert.False(probe.HasInk(point), $"{what}: expected nothing at {point}, found ink.");
    }

    /// <summary>Asserts ink or its absence, so a theory can carry both cases.</summary>
    public static void AssertPainted(this Probe probe, double x, double y, bool painted, string what)
    {
        if (painted) probe.AssertInk(x, y, what);
        else probe.AssertBlank(x, y, what);
    }

    /// <summary>
    /// Asserts how much of a run is inked. Distinguishes a solid line from a
    /// dashed one without depending on where the dashes happen to fall.
    /// </summary>
    public static void AssertInkRatio(this Probe probe, Vec2 from, Vec2 to, double low, double high, string what)
    {
        double ratio = probe.InkRatioAlong(from, to);
        Assert.True(ratio >= low && ratio <= high,
            $"{what}: ink ratio along {from}..{to} was {ratio:0.000}, expected {low:0.00}..{high:0.00}.");
    }
}
