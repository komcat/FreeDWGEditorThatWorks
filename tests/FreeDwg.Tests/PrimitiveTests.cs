using FreeDwg.Tests.Fixtures;
using FreeDwg.Tests.Rendering;

namespace FreeDwg.Tests;

public sealed class PrimitiveTests : IClassFixture<PrimitivesFixture>
{
    private readonly PrimitivesFixture _fixture;

    public PrimitiveTests(PrimitivesFixture fixture) => _fixture = fixture;

    [Theory]
    // Lines and a circle.
    [InlineData(0, 0, true, "rectangle corner")]
    [InlineData(100, 0, true, "rectangle mid-edge")]
    [InlineData(25, 60, true, "circle rim")]
    [InlineData(50, 70, false, "circle interior")]
    // A quarter arc: drawn where it sweeps, absent where it does not.
    [InlineData(180, 60, true, "arc90 at its start, 0 degrees")]
    [InlineData(150, 90, true, "arc90 at its end, 90 degrees")]
    [InlineData(120, 60, false, "arc90 outside its sweep, 180 degrees")]
    [InlineData(150, 30, false, "arc90 outside its sweep, 270 degrees")]
    // An arc whose end angle wraps past zero must still cover zero.
    [InlineData(188, 60, true, "wrapping arc covers 0 degrees")]
    [InlineData(112, 60, false, "wrapping arc outside its sweep, 180 degrees")]
    // Bulges swing outward past the straight runs, not inward.
    [InlineData(170, 25, true, "stadium right cap apex")]
    [InlineData(30, 25, true, "stadium left cap apex")]
    [InlineData(100, 25, false, "stadium interior")]
    // Sagitta is bulge * chord / 2, so these are exact.
    [InlineData(45, 85.0, true, "bulge +0.6 sags below its chord")]
    [InlineData(95, 115.0, true, "bulge -0.6 rises above its chord")]
    [InlineData(45, 115.0, false, "bulge +0.6 does not rise above its chord")]
    // A layer that is switched off.
    [InlineData(100, 60, false, "entity on a layer that is off")]
    public void DrawsGeometryWhereExpected(double x, double y, bool painted, string what) =>
        _fixture.Probe.AssertPainted(x, y, painted, what);

    [Fact]
    public void SkipsUnsupportedEntitiesAndSaysWhich()
    {
        var unsupported = _fixture.Diagnostics.UnsupportedEntities;
        string seen = string.Join(", ", unsupported.Keys);

        Assert.True(unsupported.ContainsKey("POINT"), $"expected POINT to be reported, got [{seen}]");

        // TEXT is supported, so it must not be tallied. The point of the
        // tally is to rank what is worth implementing next, which it cannot
        // do if it also lists things that already work.
        Assert.False(unsupported.ContainsKey("TEXT"), $"TEXT is supported and should not be reported, got [{seen}]");
        Assert.Equal(1, _fixture.Diagnostics.SkippedCount);
    }

    [Fact]
    public void HidesEntitiesOnLayersThatAreOff() =>
        Assert.Equal(1, _fixture.Frame.Stats.HiddenByLayer);

    [Fact]
    public void ReportsExtentsCoveringEveryEntity()
    {
        var bounds = _fixture.Drawing.Bounds;

        Assert.Equal(0, bounds.MinX, 6);
        Assert.Equal(0, bounds.MinY, 6);
        Assert.Equal(200, bounds.MaxX, 6);
        Assert.Equal(120, bounds.MaxY, 6);
    }

    [Fact]
    public void ResolvesByLayerColourFromTheLayer()
    {
        var walls = _fixture.Drawing.Layers.First(layer => layer.Name == "WALLS");
        var onWalls = _fixture.Drawing.Entities.First(e => _fixture.Drawing.Layers[e.LayerIndex] == walls);

        Assert.Equal(walls.Color, onWalls.Style.Color);
    }
}
