using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Tests.Fixtures;
using FreeDwg.Tests.Rendering;

namespace FreeDwg.Tests;

/// <summary>
/// That a selected entity actually paints in the selection colour, through
/// the real canvas and sink.
/// </summary>
/// <remarks>
/// Worth a pixel test rather than a unit test because the interesting case is
/// a block instance: the style a sink is handed comes from the entity, not
/// from the emit context, so highlighting a block had to be done by wrapping
/// the sink. Nothing in the object model would show whether that reaches the
/// children.
/// </remarks>
public sealed class SelectionRenderTests : IClassFixture<BlocksFixture>
{
    private readonly BlocksFixture _fixture;

    public SelectionRenderTests(BlocksFixture fixture) => _fixture = fixture;

    /// <summary>The plain TICK placement at (30,30); its neighbour is at (80,30).</summary>
    private Probe RenderWithFirstTickSelected()
    {
        var first = _fixture.Drawing.Entities.OfType<SInsert>().First();

        return new Probe(StaRenderer.Render(_fixture.Drawing, saveAs: "blocks_selected",
            prepare: canvas => canvas.Selection.Add(first)));
    }

    [Fact]
    public void SelectingABlockRecoloursTheGeometryInsideIt()
    {
        var probe = RenderWithFirstTickSelected();

        // The cross arm of the selected TICK. Its layer colour is grey, so
        // blue here can only have come from the selection.
        var selected = probe.SampleNear(new Vec2(35, 30));
        Assert.NotNull(selected);
        Assert.True(IsSelectionBlue(selected!.Value),
            $"selected block should paint in the selection colour, got {Describe(selected.Value)}");

        // The ring inside the block is on a different layer again, cyan, and
        // must also have been overridden rather than keeping its own colour.
        var ring = probe.SampleNear(new Vec2(32.121, 32.121));
        Assert.NotNull(ring);
        Assert.True(IsSelectionBlue(ring!.Value),
            $"the ring inside the selected block should be highlighted too, got {Describe(ring.Value)}");
    }

    [Fact]
    public void SelectingOneBlockLeavesTheOthersAlone()
    {
        var probe = RenderWithFirstTickSelected();

        // The arm tip of the 2x placement at (80,30), which is not selected.
        var other = probe.SampleNear(new Vec2(90, 30));

        Assert.NotNull(other);
        Assert.False(IsSelectionBlue(other!.Value),
            $"an unselected block must keep its own colour, got {Describe(other.Value)}");
    }

    [Fact]
    public void NothingSelectedPaintsNothingBlue()
    {
        // The base fixture frame, rendered with an empty selection.
        var arm = _fixture.Probe.SampleNear(new Vec2(35, 30));

        Assert.NotNull(arm);
        Assert.False(IsSelectionBlue(arm!.Value),
            $"with nothing selected the arm should keep its layer grey, got {Describe(arm.Value)}");
    }

    /// <summary>
    /// The selection colour is (90,175,255): blue well clear of green, which
    /// separates it from the cyan the ring layer would otherwise paint.
    /// </summary>
    private static bool IsSelectionBlue((byte R, byte G, byte B) c) =>
        c.B > c.G + 30 && c.B > c.R + 60;

    private static string Describe((byte R, byte G, byte B) c) => $"rgb({c.R},{c.G},{c.B})";
}
