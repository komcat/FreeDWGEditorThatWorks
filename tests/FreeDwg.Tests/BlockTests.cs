using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Tests.Fixtures;
using FreeDwg.Tests.Rendering;

namespace FreeDwg.Tests;

/// <summary>
/// The TICK block is a cross on the axes plus a ring, an asymmetric flag on
/// the +X arm and a quarter arc. Every probe aimed at the ring is taken
/// off-axis: the cross arms lie exactly on the block's axes, so an on-axis
/// probe would pass on the cross and say nothing about the ring.
/// </summary>
public sealed class BlockTests : IClassFixture<BlocksFixture>
{
    /// <summary>cos 45 degrees, for taking ring probes off the axes.</summary>
    private const double Diagonal = 0.70710678;

    private readonly BlocksFixture _fixture;

    public BlockTests(BlocksFixture fixture) => _fixture = fixture;

    [Theory]
    // Plain placement at (30,30).
    [InlineData(35, 30, true, "plain: cross arm tip")]
    [InlineData(32.121, 32.121, true, "plain: ring rim, off-axis")]
    [InlineData(33.536, 33.536, false, "plain: clear between ring and arc")]
    [InlineData(35, 35, true, "plain: asymmetric flag")]
    [InlineData(25, 35, false, "plain: no flag on the -X side")]
    // Uniform scale x2 at (80,30).
    [InlineData(90, 30, true, "scaled: cross arm tip at 2x")]
    [InlineData(84.243, 34.243, true, "scaled: ring rim at 2x")]
    [InlineData(82.121, 32.121, false, "scaled: nothing left at the unscaled radius")]
    // Rotated 45 degrees at (130,30).
    [InlineData(133.536, 33.536, true, "rotated: arm tip on the diagonal")]
    [InlineData(135, 30, false, "rotated: nothing left on the old axis")]
    // Mirrored in X at (180,30): the flag and the arc both change sides.
    [InlineData(175, 35, true, "mirrored: flag now on the -X side")]
    [InlineData(185, 35, false, "mirrored: no flag on the +X side")]
    [InlineData(172, 30, true, "mirrored: arc now upper-left")]
    [InlineData(188, 30, false, "mirrored: arc gone from upper-right")]
    // Non-uniform 3x1 at (30,80): the ring becomes an ellipse rx 9, ry 3.
    // These stay clear of y=80 and x=30, where the cross arms lie.
    [InlineData(34, 82.687, true, "non-uniform: on the ellipse at x=34")]
    [InlineData(36.364, 82.121, true, "non-uniform: on the ellipse at x=36.4")]
    [InlineData(32.598, 81.5, false, "non-uniform: a circle of r=3 would be here")]
    // Nested block at (100,80) holding two TICKs, 20 apart.
    [InlineData(102.121, 82.121, true, "nested: first tick ring")]
    [InlineData(122.121, 82.121, true, "nested: second tick ring")]
    [InlineData(112, 80, false, "nested: gap between them")]
    // A block whose base point is not its origin.
    [InlineData(64, 110, true, "base point: geometry centred on the insert point")]
    [InlineData(74, 120, false, "base point: not displaced by the base point")]
    public void PlacesBlockGeometry(double x, double y, bool painted, string what) =>
        _fixture.Probe.AssertPainted(x, y, painted, what);

    [Fact]
    public void ByBlockColourComesFromTheInsert()
    {
        // The insert at (180,80) is red; the TICK's arc is ByBlock and its
        // cross is not, so they must differ.
        var arc = _fixture.Probe.SampleNear(new Vec2(188, 80));
        var cross = _fixture.Probe.SampleNear(new Vec2(185, 80));

        Assert.NotNull(arc);
        Assert.True(arc!.Value is { R: > 200, G: < 80, B: < 80 },
            $"ByBlock arc should take the insert's red, got {Describe(arc)}");

        Assert.NotNull(cross);
        Assert.True(Math.Abs(cross!.Value.R - cross.Value.G) < 24 && Math.Abs(cross.Value.G - cross.Value.B) < 24,
            $"non-ByBlock cross should keep its layer's grey, got {Describe(cross)}");
    }

    [Theory]
    [InlineData(32.121, 32.121, false, "ring inside a block hides with its layer")]
    [InlineData(35, 30, true, "the cross on another layer is still drawn")]
    public void LayerVisibilityReachesInsideBlocks(double x, double y, bool painted, string what) =>
        _fixture.RingsOff.AssertPainted(x, y, painted, what);

    [Fact]
    public void StoresBlockGeometryOnceAndInstancesIt()
    {
        var tick = _fixture.Drawing.Blocks.First(block => block.Name == "TICK");
        var instances = _fixture.Drawing.Entities.OfType<SInsert>().Where(i => i.Block == tick).ToList();

        Assert.True(instances.Count >= 5, $"expected several TICK instances, found {instances.Count}");
        Assert.All(instances, insert => Assert.Same(tick, insert.Block));
    }

    [Fact]
    public void NestedBlocksResolveToTheirDefinition()
    {
        var pair = _fixture.Drawing.Blocks.First(block => block.Name == "PAIR");
        var nested = pair.Entities.OfType<SInsert>().ToList();

        Assert.Equal(2, nested.Count);
        Assert.All(nested, insert => Assert.Equal("TICK", insert.Block.Name));
    }

    private static string Describe((byte R, byte G, byte B)? c) =>
        c is null ? "background" : $"rgb({c.Value.R},{c.Value.G},{c.Value.B})";
}
