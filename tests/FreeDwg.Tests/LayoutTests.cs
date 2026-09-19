using FreeDwg.Tests.Fixtures;
using FreeDwg.Tests.Rendering;

namespace FreeDwg.Tests;

/// <summary>
/// The sheet's viewport is 200x120 paper units showing 60 model units of
/// height, so the scale is exactly 2: the 100x60 model rectangle lands on the
/// viewport frame and a radius-10 circle becomes radius 20. Every probe below
/// follows from that, which is why the numbers are checkable rather than
/// merely plausible.
/// </summary>
public sealed class LayoutTests : IClassFixture<LayoutFixture>
{
    private readonly LayoutFixture _fixture;

    public LayoutTests(LayoutFixture fixture) => _fixture = fixture;

    [Fact]
    public void ImportsModelSpaceAndEachSheet()
    {
        Assert.False(_fixture.Drawing.ModelSpace.IsPaperSpace);
        Assert.Same(_fixture.Drawing.Layouts[0], _fixture.Drawing.ModelSpace);
        Assert.NotNull(_fixture.Sheet);
        Assert.True(_fixture.Sheet!.IsPaperSpace);
    }

    [Theory]
    [InlineData(0, 0, true, "model: rectangle corner")]
    [InlineData(60, 30, true, "model: circle rim")]
    [InlineData(-150, 50, true, "model: the long line runs past the drawing")]
    public void DrawsModelSpace(double x, double y, bool painted, string what) =>
        _fixture.Model.AssertPainted(x, y, painted, what);

    [Theory]
    [InlineData(0, 0, true, "sheet border corner")]
    [InlineData(297, 210, true, "sheet border far corner")]
    [InlineData(50, 50, true, "viewport frame corner")]
    // Model geometry, seen through the window at scale 2.
    [InlineData(170, 110, true, "viewport: the model circle, at twice its size")]
    [InlineData(160, 110, false, "viewport: inside that circle")]
    // The model line spans x -200..300, far past the window on both sides.
    [InlineData(60, 150, true, "viewport: the clipped line, inside the frame")]
    [InlineData(40, 150, false, "viewport: nothing spills left of the frame")]
    [InlineData(260, 150, false, "viewport: nothing spills right of the frame")]
    public void DrawsPaperSpaceThroughItsViewport(double x, double y, bool painted, string what)
    {
        Assert.NotNull(_fixture.Paper);
        _fixture.Paper!.AssertPainted(x, y, painted, what);
    }

    [Theory]
    [InlineData(170, 110, false, "a layer switched off hides geometry inside a viewport")]
    [InlineData(60, 150, true, "other layers are still drawn through the viewport")]
    [InlineData(0, 0, true, "the sheet border is unaffected")]
    public void LayerVisibilityReachesThroughAViewport(double x, double y, bool painted, string what)
    {
        Assert.NotNull(_fixture.PaperLayerOff);
        _fixture.PaperLayerOff!.AssertPainted(x, y, painted, what);
    }

    /// <summary>
    /// Every sheet carries a pseudo-viewport standing for the sheet itself.
    /// It is not something we failed to draw, so it must not appear in the
    /// unsupported tally.
    /// </summary>
    [Fact]
    public void DoesNotReportTheSheetPseudoViewportAsUnsupported()
    {
        var unsupported = _fixture.Diagnostics.UnsupportedEntities;

        Assert.False(unsupported.ContainsKey("VIEWPORT"),
            $"the sheet pseudo-viewport should be skipped quietly, got [{string.Join(", ", unsupported.Keys)}]");
    }
}
