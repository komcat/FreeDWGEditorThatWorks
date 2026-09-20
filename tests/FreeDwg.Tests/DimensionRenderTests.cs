using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Tools;
using FreeDwg.Tests.Rendering;
using FreeDWGEditorThatWorks.Controls;
using SceneDrawing = FreeDwg.Core.Scene.Drawing;

namespace FreeDwg.Tests;

/// <summary>
/// A dimension drawn the way the editor draws one, then asked what it put on
/// the screen.
/// </summary>
/// <remarks>
/// A dimension is the first entity here that draws in three different ways at
/// once -- stroked lines, a filled arrowhead and a block of text -- and the
/// object model says nothing about whether all three arrive. The number in
/// particular goes out through the sink's text path with an anchor and a
/// rotation, and getting either wrong leaves it legible, in the wrong place,
/// and passing every other test in this suite.
/// <para>
/// The fixture is a box 80 wide with one dimension below it, so the space
/// between the extension lines is empty except for what the dimension itself
/// draws. Probes there can only be reached by the thing under test.
/// </para>
/// </remarks>
public sealed class DimensionRenderTests
{
    private const double Width = 80;
    private const double Below = -20;

    private static SceneDrawing Fixture()
    {
        var drawing = SceneDrawing.CreateEmpty();
        var canvas = new CadCanvas { Drawing = drawing };

        // Through the canvas, so the dimension is built, sized and put on its
        // layer exactly as a user's click would leave it.
        canvas.UseTool(new RectangleTool());
        canvas.PlaceToolPoint(new Vec2(0, 0));
        canvas.PlaceToolPoint(new Vec2(Width, 40));

        canvas.UseTool(new LinearDimensionTool());
        canvas.PlaceToolPoint(new Vec2(0, 0));
        canvas.PlaceToolPoint(new Vec2(Width, 0));
        canvas.PlaceToolPoint(new Vec2(Width / 2, Below));

        return drawing;
    }

    /// <summary>The whole fixture, framed to its extents.</summary>
    private static Probe Wide() => StaRenderer.OnSta(() =>
        new Probe(StaRenderer.Render(Fixture(), saveAs: "dimension")));

    /// <summary>
    /// The same fixture framed on one corner of itself, for the marks that
    /// are smaller than the three pixels a probe looks around itself.
    /// </summary>
    private static Probe Zoomed(string saveAs, Bounds2 window) => StaRenderer.OnSta(() =>
        new Probe(StaRenderer.Render(Fixture(), saveAs: saveAs,
            prepare: canvas => canvas.Camera.ZoomToFit(window, 0))));

    [Fact]
    public void TheDimensionLineIsDrawnRightAcrossTheSpanItMeasures()
    {
        var probe = Wide();

        // Along the line but clear of the middle, where the number sits.
        probe.AssertInkRatio(new Vec2(6, Below), new Vec2(30, Below), 0.9, 1.0,
            "the dimension line");
        probe.AssertInkRatio(new Vec2(50, Below), new Vec2(74, Below), 0.9, 1.0,
            "the dimension line, past the number");
    }

    [Fact]
    public void TheExtensionLinesReachDownFromTheGeometryToTheLine()
    {
        var probe = Wide();

        // Half way down each one, where nothing else in the fixture goes.
        probe.AssertInk(0, Below / 2, "the first extension line");
        probe.AssertInk(Width, Below / 2, "the second extension line");

        // And the space between them is empty, which is what makes the two
        // probes above mean anything.
        probe.AssertBlank(Width / 2, Below / 2, "between the extension lines");
    }

    [Fact]
    public void TheExtensionLinesStopShortOfTheGeometryTheyMeasure()
    {
        // Framed on the corner of the box, because the gap being checked is
        // 0.625 of a unit and a probe looks three pixels around itself: at
        // the fixture's own zoom the box corner is inside that radius, and
        // the probe would pass or fail on the wrong ink.
        var probe = Zoomed("dimension_gap",
            Bounds2.FromCorners(new Vec2(-2, -3), new Vec2(2, 1)));

        probe.AssertInk(0, 0, "the corner of the box");
        probe.AssertBlank(0, -0.3, "the gap between the object and its extension line");
        probe.AssertInk(0, -1.2, "the extension line, past the gap");
    }

    [Fact]
    public void TheNumberIsPaintedAboveTheDimensionLine()
    {
        var probe = Wide();

        // The text sits a gap above the line, centred on it. Nothing else in
        // the fixture is anywhere near there: the extension lines are 40
        // away on either side.
        probe.AssertInk(Width / 2, Below + 1.9, "the measurement");

        // And not below it, which is where a mis-set anchor would put it.
        probe.AssertBlank(Width / 2, Below - 1.9, "under the dimension line");
    }

    [Fact]
    public void TheArrowheadIsFilledAndTheRightSizeAndShape()
    {
        // Framed on the left arrowhead alone, so that a triangle 2.5 units
        // long is hundreds of pixels across and can be probed across its
        // width rather than along a line it shares with the dimension.
        var probe = Zoomed("dimension_arrowhead",
            Bounds2.FromCorners(new Vec2(-4, Below - 4), new Vec2(6, Below + 4)));

        // The arrow tapers from nothing at its tip to a sixth of its length
        // at the back. At 2.2 along a 2.5 arrow it is 0.367 half-wide.
        probe.AssertInk(2.2, Below - 0.3, "inside the arrowhead, off the line");
        probe.AssertBlank(2.2, Below - 0.6, "outside the arrowhead");

        // Past the back of it there is nothing but the dimension line again.
        probe.AssertBlank(3.2, Below - 0.3, "past the back of the arrowhead");
    }

    // ---- a dimension too small to hold its own marks -----------------------

    /// <summary>
    /// A five-wide dimension, which is exactly two arrowheads across: the
    /// case a small chamfer produces, and the one that drew a solid diamond.
    /// </summary>
    private static SceneDrawing Cramped()
    {
        var drawing = SceneDrawing.CreateEmpty();
        var canvas = new CadCanvas { Drawing = drawing };

        canvas.UseTool(new LinearDimensionTool());
        canvas.PlaceToolPoint(new Vec2(0, 0));
        canvas.PlaceToolPoint(new Vec2(5, 0));
        canvas.PlaceToolPoint(new Vec2(2.5, -8));

        return drawing;
    }

    [Fact]
    public void ADimensionTooSmallForItsArrowsLeavesTheMiddleClear()
    {
        var probe = StaRenderer.OnSta(() =>
            new Probe(StaRenderer.Render(Cramped(), saveAs: "dimension_cramped")));

        // Out past each end, where an arrow turned outwards now sits. There
        // is nothing else in this fixture that reaches there.
        probe.AssertInk(-2.5, -8, "the first arrow, outside");
        probe.AssertInk(7.5, -8, "the second arrow, outside");

        // And the middle of the line is a line rather than the solid diamond
        // two arrowheads meeting head to head used to paint. The probe is
        // off the line by more than half its width and less than half an
        // arrowhead, so it is blank only because the arrows have gone.
        probe.AssertBlank(2.5, -8.6, "the middle, clear of the arrowheads");
        probe.AssertInk(2.5, -8, "the dimension line itself");
    }

    // ---- radius and diameter -----------------------------------------------

    /// <summary>A circle of radius 30, with one radial dimension pulled out of it.</summary>
    private static SceneDrawing Radial(bool diameter)
    {
        var drawing = SceneDrawing.CreateEmpty();
        var canvas = new CadCanvas { Drawing = drawing };

        canvas.UseTool(new CircleTool());
        canvas.PlaceToolPoint(new Vec2(0, 0));
        canvas.PlaceToolPoint(new Vec2(30, 0));

        canvas.UseTool(diameter ? new DiameterDimensionTool() : new RadiusDimensionTool());
        canvas.PickEntityForTool(new Vec2(30, 0));
        canvas.PlaceToolPoint(new Vec2(60, 0));

        return drawing;
    }

    [Fact]
    public void ARadiusLeaderRunsFromTheCentreOutPastTheRim()
    {
        var probe = StaRenderer.OnSta(() =>
            new Probe(StaRenderer.Render(Radial(diameter: false), saveAs: "dimension_radius")));

        // Out along the leader: inside the circle, on the rim, and beyond it
        // to where the number was dropped.
        probe.AssertInk(15, 0, "the leader inside the circle");
        probe.AssertInk(45, 0, "the leader outside the circle");

        // Nothing on the far side: a radius stops at the centre, which is
        // the whole difference from a diameter.
        probe.AssertBlank(-15, 0, "across the centre, where a radius does not go");
    }

    [Fact]
    public void ADiameterLeaderRunsRightAcross()
    {
        var probe = StaRenderer.OnSta(() =>
            new Probe(StaRenderer.Render(Radial(diameter: true), saveAs: "dimension_diameter")));

        probe.AssertInk(15, 0, "the leader on the near side");
        probe.AssertInk(-15, 0, "the leader on the far side");
    }

    [Fact]
    public void ARadialNumberIsPaintedAboveItsLeader()
    {
        var probe = StaRenderer.OnSta(() =>
            new Probe(StaRenderer.Render(Radial(diameter: false), saveAs: "dimension_radius")));

        // Above the leader at the point it was dropped, and clear of the
        // circle -- so only the number can be there.
        probe.AssertInk(60, 1.9, "the R number");
    }

    [Fact]
    public void TheDimensionIsGreenAndTheGeometryIsNot()
    {
        var probe = Wide();

        var dimension = probe.SampleNear(new Vec2(20, Below));
        var box = probe.SampleNear(new Vec2(20, 40));

        Assert.NotNull(dimension);
        Assert.NotNull(box);

        // Its own layer, and that layer's colour: this is the only place the
        // "dimensions go on the dimension layer" rule can actually be seen.
        Assert.True(dimension!.Value.G > dimension.Value.R + 40,
            $"the dimension should be green, got {dimension.Value}");
        Assert.False(box!.Value.G > box.Value.R + 40,
            $"the geometry should not have been recoloured, got {box.Value}");
    }
}
