using FreeDwg.Core.Commands;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Editing;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Tools;
using FreeDwg.Tests.Rendering;
using SceneDrawing = FreeDwg.Core.Scene.Drawing;

namespace FreeDwg.Tests;

/// <summary>
/// A drawing built the way the editor builds one -- tools producing entities,
/// a command stack putting them in -- and then rendered, to check the whole
/// chain end to end.
/// </summary>
/// <remarks>
/// The unit tests say each tool returns the right entity and the stack holds
/// it. Only pixels say the entity then reaches the screen: an edit that left
/// the layout's bounds or its spatial index stale would pass every one of
/// those tests and draw nothing at all.
/// </remarks>
public sealed class DrawingRenderTests
{
    /// <summary>Builds the fixture by clicking, as a user would.</summary>
    private static SceneDrawing Draw(out CommandStack stack)
    {
        var drawing = SceneDrawing.CreateEmpty();
        stack = new CommandStack(drawing);

        Use(drawing, stack, new LineTool(), new Vec2(10, 10), new Vec2(90, 10));
        Use(drawing, stack, new RectangleTool(), new Vec2(10, 30), new Vec2(90, 70));
        Use(drawing, stack, new CircleTool(), new Vec2(140, 50), new Vec2(165, 50));
        Use(drawing, stack, new ArcTool(), new Vec2(110, 90), new Vec2(140, 110), new Vec2(170, 90));

        return drawing;
    }

    private static void Use(SceneDrawing drawing, CommandStack stack, DrawTool tool, params Vec2[] points)
    {
        foreach (var point in points)
        {
            if (tool.Click(point) is not { } entity) continue;

            // Exactly what the canvas does: stamp the layer, then commit.
            drawing.Place(entity);
            stack.Do(new AddEntities(drawing.ActiveLayout, entity));
        }
    }

    [Theory]
    [InlineData(50, 10, true, "the line, midway along")]
    [InlineData(50, 20, false, "between the line and the rectangle")]
    [InlineData(10, 50, true, "the rectangle's left edge")]
    [InlineData(50, 70, true, "the rectangle's top edge")]
    [InlineData(50, 50, false, "inside the rectangle, which is not filled")]
    [InlineData(165, 50, true, "the circle's rim")]
    [InlineData(140, 50, false, "the circle's centre, which is not filled")]
    [InlineData(140, 110, true, "the apex of the arc")]
    [InlineData(140, 90, false, "under the arc, where its chord would be")]
    public void GeometryDrawnByAToolLandsWhereItWasPicked(double x, double y, bool painted, string what)
    {
        var drawing = Draw(out _);
        var probe = new Probe(StaRenderer.Render(drawing, saveAs: "tools"));

        probe.AssertPainted(x, y, painted, what);
    }

    [Fact]
    public void UndoingEverythingLeavesAnEmptySheet()
    {
        var drawing = Draw(out var stack);
        Assert.Equal(4, drawing.Entities.Count);

        while (stack.CanUndo) stack.Undo();

        Assert.Empty(drawing.Entities);
        Assert.True(drawing.Bounds.IsEmpty);

        // Rendered rather than asserted on the model, because an undo that
        // left the spatial index holding the entity would still paint it.
        var probe = new Probe(StaRenderer.Render(drawing, saveAs: "tools_undone"));

        probe.AssertBlank(50, 10, "the line after undo");
        probe.AssertBlank(10, 50, "the rectangle after undo");
        probe.AssertBlank(165, 50, "the circle after undo");
        probe.AssertBlank(140, 110, "the arc after undo");
    }

    [Fact]
    public void RedoPutsItAllBack()
    {
        var drawing = Draw(out var stack);

        while (stack.CanUndo) stack.Undo();
        while (stack.CanRedo) stack.Redo();

        Assert.Equal(4, drawing.Entities.Count);

        var probe = new Probe(StaRenderer.Render(drawing, saveAs: "tools_redone"));
        probe.AssertInk(50, 10, "the line after redo");
        probe.AssertInk(165, 50, "the circle after redo");
    }

    [Fact]
    public void ErasingOneObjectLeavesTheRestWhereTheyWere()
    {
        var drawing = Draw(out var stack);
        var circle = drawing.Entities.OfType<FreeDwg.Core.Scene.Entities.SCircle>().Single();

        stack.Do(new DeleteEntities(drawing.ActiveLayout, [circle]));

        var probe = new Probe(StaRenderer.Render(drawing, saveAs: "tools_erased"));

        probe.AssertBlank(165, 50, "the erased circle");
        probe.AssertInk(50, 10, "the line, which was not erased");
        probe.AssertInk(140, 110, "the arc, which was not erased");
    }

    [Fact]
    public void TrimmingCutsTheStretchThatWasClicked()
    {
        var drawing = SceneDrawing.CreateEmpty();
        var stack = new CommandStack(drawing);

        // A long rail crossed by two uprights at x = 40 and x = 80.
        Use(drawing, stack, new LineTool(), new Vec2(0, 50), new Vec2(120, 50));
        Use(drawing, stack, new LineTool(), new Vec2(40, 10), new Vec2(40, 90));
        Use(drawing, stack, new LineTool(), new Vec2(80, 10), new Vec2(80, 90));

        var rail = drawing.Entities[0];

        var boundaries = new List<CurvePiece>();
        foreach (var entity in drawing.Entities.Skip(1)) entity.CollectCurves(boundaries, 0.01);

        // Clicked between the uprights, so that is the stretch that goes.
        var plan = Trimming.Trim(rail, boundaries, new Vec2(60, 50));
        stack.Do(new ReplaceEntities(drawing.ActiveLayout, plan, "Trim"));

        var probe = new Probe(StaRenderer.Render(drawing, saveAs: "tools_trimmed"));

        probe.AssertBlank(60, 50, "the stretch between the uprights, cut away");
        probe.AssertInk(20, 50, "the rail to the left of the first upright");
        probe.AssertInk(100, 50, "the rail to the right of the second");
        probe.AssertInk(40, 30, "the uprights, untouched");

        // And it comes back: two pieces out, one line in.
        stack.Undo();
        var after = new Probe(StaRenderer.Render(drawing, saveAs: "tools_untrimmed"));
        after.AssertInk(60, 50, "the rail after undo");
    }

    [Fact]
    public void AMirroredArcBulgesTheRightWay()
    {
        var drawing = SceneDrawing.CreateEmpty();
        var stack = new CommandStack(drawing);

        // An arc from (0,0) through (20,20) to (40,0): it arches upward.
        Use(drawing, stack, new ArcTool(), new Vec2(0, 0), new Vec2(20, 20), new Vec2(40, 0));

        var arc = drawing.Entities[0];
        stack.Do(new CopyEntities(drawing.ActiveLayout, [arc],
            Mat3.Reflection(new Vec2(60, 0), new Vec2(0, 1)), "Mirror"));

        var probe = new Probe(StaRenderer.Render(drawing, saveAs: "tools_mirrored_arc"));

        // Mirroring in a vertical line leaves up as up, so the copy has to
        // arch the same way. This is the one assertion that catches a sweep
        // that was not reversed: the endpoints and the bounds are identical
        // either way, and only the pixels differ.
        probe.AssertInk(100, 20, "the mirrored arc, arching up as the original does");
        probe.AssertBlank(100, -20, "nothing below: an unreversed sweep would put the arc here");

        probe.AssertInk(20, 20, "the original, untouched");
    }

    [Fact]
    public void NewGeometryTakesTheColourOfTheCurrentLayer()
    {
        var drawing = SceneDrawing.CreateEmpty();
        drawing.AddLayer(new Layer("RED") { Color = new FreeDwg.Core.Styling.Rgb(255, 0, 0) });
        drawing.CurrentLayerIndex = 1;

        var stack = new CommandStack(drawing);
        Use(drawing, stack, new LineTool(), new Vec2(0, 0), new Vec2(100, 0));

        var probe = new Probe(StaRenderer.Render(drawing, saveAs: "tools_layer_colour"));
        var sample = probe.SampleNear(new Vec2(50, 0));

        Assert.NotNull(sample);

        // Hue rather than intensity: a hairline that falls between two pixel
        // rows is split across both, so even a pure red line samples dim.
        var (r, g, b) = sample!.Value;
        Assert.True(r > g * 3 && r > b * 3,
            $"a line drawn on a red layer should be red, got rgb({r},{g},{b})");
    }

    /// <summary>
    /// A rectangle with one corner rounded, drawn and filleted through the
    /// canvas as a user would.
    /// </summary>
    private static SceneDrawing FilletedRectangle(bool chamfer)
    {
        var drawing = SceneDrawing.CreateEmpty();
        var canvas = new FreeDWGEditorThatWorks.Controls.CadCanvas { Drawing = drawing };

        canvas.UseTool(new RectangleTool());
        canvas.PlaceToolPoint(new Vec2(0, 0));
        canvas.PlaceToolPoint(new Vec2(100, 60));

        if (chamfer)
        {
            canvas.ChamferDistance = 10;
            canvas.UseTool(new ChamferTool());
        }
        else
        {
            canvas.FilletRadius = 10;
            canvas.UseTool(new FilletTool());
        }

        // The bottom edge and the right edge, which share the corner at
        // (100,0).
        canvas.PickEntityForTool(new Vec2(50, 0));
        canvas.PickEntityForTool(new Vec2(100, 30));

        return drawing;
    }

    [Fact]
    public void AFilletedRectangleCornerIsDrawnRoundedTheRightWay()
    {
        var probe = StaRenderer.OnSta(() =>
            new Probe(StaRenderer.Render(FilletedRectangle(chamfer: false), saveAs: "fillet_polyline")));

        // The middle of the arc, at 45 degrees from a centre at (90,10).
        probe.AssertInk(97.071, 2.929, "the rounded corner");

        // The sharp corner it replaced is gone.
        probe.AssertBlank(100, 0, "the corner the fillet cut off");

        // And the arc did not go the long way round. That is the one thing
        // the object model cannot tell you: the same two endpoints on the
        // same circle, swept the other way, put the arc here instead -- and
        // the bounds and the vertices are identical either way.
        probe.AssertBlank(82.929, 17.071, "where a reversed bulge would have drawn it");
    }

    [Fact]
    public void AChamferedRectangleCornerIsDrawnStraight()
    {
        var probe = StaRenderer.OnSta(() =>
            new Probe(StaRenderer.Render(FilletedRectangle(chamfer: true), saveAs: "chamfer_polyline")));

        // The middle of the cut, which runs from (90,0) to (100,10).
        probe.AssertInk(95, 5, "the chamfered corner");
        probe.AssertBlank(100, 0, "the corner the chamfer cut off");

        // Straight, not bulged: a fillet of the same size would pass through
        // here and a chamfer does not.
        probe.AssertBlank(97.071, 2.929, "where a rounded corner would have gone");
    }
}
