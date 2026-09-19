using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Tests.Fixtures;
using FreeDwg.Tests.Rendering;

namespace FreeDwg.Tests;

public sealed class CurveTests : IClassFixture<CurvesFixture>
{
    private readonly CurvesFixture _fixture;

    public CurveTests(CurvesFixture fixture) => _fixture = fixture;

    [Theory]
    // Axis-aligned ellipse about (30,30), rx 20 ry 8.
    [InlineData(50, 30, true, "ellipse: end of the major axis")]
    [InlineData(30, 38, true, "ellipse: end of the minor axis")]
    [InlineData(38, 30, false, "ellipse: interior")]
    [InlineData(30, 30, false, "ellipse: centre")]
    // Major axis along +Y about (90,30), so rx 7.5 ry 15.
    [InlineData(90, 45, true, "standing ellipse: tall axis")]
    [InlineData(82.5, 30, true, "standing ellipse: short axis")]
    [InlineData(90, 37.5, false, "standing ellipse: not a circle")]
    public void DrawsEllipses(double x, double y, bool painted, string what) =>
        _fixture.Probe.AssertPainted(x, y, painted, what);

    /// <summary>
    /// A clamped cubic with four control points is a plain Bezier, so its
    /// midpoint is known exactly: (P0 + 3*P1 + 3*P2 + P3) / 8 = (30, 82.5).
    /// Requiring the curve to pass through it while missing both interior
    /// control points is what separates real NURBS evaluation from quietly
    /// falling back to drawing the control polygon.
    /// </summary>
    [Theory]
    [InlineData(10, 60, true, "spline: starts at the first control point")]
    [InlineData(50, 60, true, "spline: ends at the last control point")]
    [InlineData(30, 82.5, true, "spline: passes through B(0.5)")]
    [InlineData(20, 90, false, "spline: misses control point P1")]
    [InlineData(40, 90, false, "spline: misses control point P2")]
    [InlineData(30, 60, false, "spline: is not the straight chord")]
    public void EvaluatesSplines(double x, double y, bool painted, string what) =>
        _fixture.Probe.AssertPainted(x, y, painted, what);

    [Theory]
    [InlineData(75, 80, true, "solid hatch: filled between the rings")]
    [InlineData(90, 65, true, "solid hatch: filled below the island")]
    [InlineData(90, 80, false, "solid hatch: the island is a hole")]
    [InlineData(115, 80, false, "solid hatch: nothing outside the boundary")]
    public void FillsHatchesWithIslands(double x, double y, bool painted, string what) =>
        _fixture.Probe.AssertPainted(x, y, painted, what);

    [Fact]
    public void StripesAPatternHatchWithinItsBoundary()
    {
        _fixture.Probe.AssertInkRatio(new Vec2(132, 80), new Vec2(168, 80), 0.05, 0.75,
            "a pattern hatch is striped, neither solid nor empty");

        _fixture.Probe.AssertInkRatio(new Vec2(175, 80), new Vec2(200, 80), 0.0, 0.02,
            "a pattern hatch does not spill past its boundary");
    }

    [Fact]
    public void DrawsDimensionsFromTheirAnonymousBlock()
    {
        var ink = _fixture.Probe.InkExtentIn(new Vec2(0, 105), new Vec2(80, 140));

        Assert.False(ink.IsEmpty, "the dimension drew nothing");
        Assert.True(ink.MinX < 30 && ink.MaxX > 40,
            $"the dimension should span its measurement, ink covers {ink.MinX:0.#}..{ink.MaxX:0.#}");
    }

    /// <summary>
    /// An arrowhead is a SOLID. Dimensions rendered without arrows until
    /// SOLID was supported, and nothing but this notices.
    /// </summary>
    [Fact]
    public void DimensionBlocksCarryFilledArrowheads()
    {
        var block = _fixture.Drawing.Blocks.FirstOrDefault(b => b.IsAnonymous || b.Name.StartsWith("*D"));

        Assert.NotNull(block);
        int arrowheads = block!.Entities.OfType<SHatch>().Count(hatch => hatch.IsSolid);
        Assert.True(arrowheads >= 2, $"expected two filled arrowheads, found {arrowheads}");
    }
}
