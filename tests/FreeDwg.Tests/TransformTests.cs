using FreeDwg.Core.Commands;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Tools;
using SceneDrawing = FreeDwg.Core.Scene.Drawing;

namespace FreeDwg.Tests;

/// <summary>
/// Moving entities: what each one does under a transform, what the modify
/// tools make of their picked points, and whether undo puts it all back.
/// </summary>
/// <remarks>
/// The recurring hazard here is the one the reader already paid for twice:
/// an arc's winding is invisible in its endpoints and its bounds, so a
/// mirror that forgets to reverse the sweep produces geometry that is
/// exactly right everywhere except in what it looks like.
/// </remarks>
public sealed class TransformTests
{
    // ---- entities under a transform --------------------------------------

    [Fact]
    public void MovingALineMovesBothEnds()
    {
        var line = new SLine(new Vec2(0, 0), new Vec2(10, 0));
        line.Transform(Mat3.Translation(new Vec2(5, 3)));

        Assert.Equal(new Vec2(5, 3), line.Start);
        Assert.Equal(new Vec2(15, 3), line.End);
    }

    [Fact]
    public void ScalingACircleScalesItsRadius()
    {
        var circle = new SCircle(new Vec2(10, 10), 5);
        circle.Transform(Mat3.ScalingAbout(3, new Vec2(10, 10)));

        Assert.Equal(new Vec2(10, 10), circle.Center);
        Assert.Equal(15, circle.Radius, 9);
    }

    [Fact]
    public void MovingAnEntityDropsItsCachedBounds()
    {
        var circle = new SCircle(new Vec2(0, 0), 5);

        // Touch the cache first, so a transform that forgot to clear it would
        // leave the old answer behind.
        Assert.Equal(-5, circle.Bounds.MinX, 9);

        circle.Transform(Mat3.Translation(new Vec2(100, 0)));

        Assert.Equal(95, circle.Bounds.MinX, 9);
    }

    [Fact]
    public void AMirroredArcKeepsItsEndsAndReversesItsSweep()
    {
        // A quarter arc from +X round to +Y, mirrored in the vertical axis.
        var arc = new SArc(Vec2.Zero, 10, 0, Math.PI / 2);
        arc.Transform(Mat3.Reflection(Vec2.Zero, new Vec2(0, 1)));

        // The ends land where a mirror puts them...
        Assert.Equal(-10, arc.StartPoint.X, 6);
        Assert.Equal(0, arc.StartPoint.Y, 6);
        Assert.Equal(0, arc.EndPoint.X, 6);
        Assert.Equal(10, arc.EndPoint.Y, 6);

        // ...and the sweep turns over with the plane. Endpoints and bounds
        // are identical either way, so only this says the arc is not inside
        // out, and a bulge drawn the wrong way round is the classic symptom.
        Assert.True(arc.Sweep < 0, "a mirrored arc must sweep the other way");
    }

    [Fact]
    public void AMirroredBulgeTurnsOverWithIt()
    {
        var polyline = new SPolyline(
            [new PolyVertex(new Vec2(0, 0), 1), new PolyVertex(new Vec2(10, 0))], closed: false);

        polyline.Transform(Mat3.Reflection(Vec2.Zero, new Vec2(1, 0)));

        // Reflected in the horizontal axis, the arc that dipped must rise.
        Assert.Equal(-1, polyline.Vertices[0].Bulge, 9);
        Assert.Equal(0.0, polyline.DistanceTo(new Vec2(5, 5), tolerance: 0.01), 6);
    }

    [Fact]
    public void MovingABlockInstanceLeavesTheDefinitionAlone()
    {
        var block = new BlockDefinition("RING");
        block.Entities.Add(new SCircle(Vec2.Zero, 5));

        var first = new SInsert(block, Mat3.Identity);
        var second = new SInsert(block, Mat3.Identity);

        first.Transform(Mat3.Translation(new Vec2(100, 0)));

        // Instancing is the whole point: moving one placement must not move
        // the other nine hundred.
        Assert.Equal(100, first.Bounds.Center.X, 6);
        Assert.Equal(0, second.Bounds.Center.X, 6);
        Assert.Equal(0, block.Bounds.Center.X, 6);
    }

    // ---- cloning ---------------------------------------------------------

    [Fact]
    public void ACopyIsIndependentOfItsOriginal()
    {
        var original = new SPolyline(
            [new PolyVertex(new Vec2(0, 0)), new PolyVertex(new Vec2(10, 0))], closed: false);

        var copy = (SPolyline)original.Clone();
        copy.Transform(Mat3.Translation(new Vec2(0, 50)));

        // The vertex array is shared by a shallow copy, so this is the
        // assertion that catches a missing deep copy.
        Assert.Equal(new Vec2(0, 0), original.Vertices[0].Point);
        Assert.Equal(new Vec2(0, 50), copy.Vertices[0].Point);
    }

    [Fact]
    public void ACopyCarriesNoHandleFromTheFile()
    {
        var original = new SLine(Vec2.Zero, new Vec2(10, 0)) { SourceHandle = 0x2A, LayerIndex = 3 };
        var copy = original.Clone();

        // Sharing the handle would have a save overwrite the original with
        // the copy; the layer and style are properties of the copy and stay.
        Assert.Equal(0UL, copy.SourceHandle);
        Assert.Equal(3, copy.LayerIndex);
    }

    [Fact]
    public void ACopiedBlockInstanceStillSharesItsDefinition()
    {
        var block = new BlockDefinition("RING");
        block.Entities.Add(new SCircle(Vec2.Zero, 5));

        var copy = (SInsert)new SInsert(block, Mat3.Identity).Clone();

        Assert.Same(block, copy.Block);
    }

    // ---- the commands -----------------------------------------------------

    private static SceneDrawing Scene(out SLine line)
    {
        var drawing = SceneDrawing.CreateEmpty();
        line = new SLine(new Vec2(0, 0), new Vec2(10, 0));
        drawing.Add(line);
        return drawing;
    }

    [Fact]
    public void UndoingATransformPutsTheGeometryBack()
    {
        var drawing = Scene(out var line);
        var stack = new CommandStack(drawing);

        stack.Do(new TransformEntities(drawing.ActiveLayout, [line],
            Mat3.RotationAbout(Math.PI / 3, new Vec2(4, 7)), "Rotate"));

        stack.Undo();

        // The inverse, not a snapshot, so this is where rounding would show.
        Assert.Equal(0, line.Start.X, 9);
        Assert.Equal(0, line.Start.Y, 9);
        Assert.Equal(10, line.End.X, 9);
        Assert.Equal(0, line.End.Y, 9);
    }

    [Fact]
    public void ATransformInvalidatesTheLayoutBoundsAndIndex()
    {
        var drawing = Scene(out var line);
        var layout = drawing.ActiveLayout;
        var stack = new CommandStack(drawing);

        Assert.Equal(0, layout.Bounds.MinX, 9);
        Assert.Equal(1, layout.Index.Count);

        stack.Do(new TransformEntities(layout, [line], Mat3.Translation(new Vec2(100, 0)), "Move"));

        // Each entity drops its own bounds; the layout's are a level up and
        // would otherwise cull the entity where it used to be.
        Assert.Equal(100, layout.Bounds.MinX, 9);
    }

    [Fact]
    public void CopyingLeavesTheOriginalWhereItWas()
    {
        var drawing = Scene(out var line);
        var stack = new CommandStack(drawing);

        var copy = new CopyEntities(drawing.ActiveLayout, [line], Mat3.Translation(new Vec2(0, 20)), "Copy");
        stack.Do(copy);

        Assert.Equal(2, drawing.Entities.Count);
        Assert.Equal(new Vec2(0, 0), line.Start);
        Assert.Equal(new Vec2(0, 20), ((SLine)copy.Copies[0]).Start);

        stack.Undo();
        Assert.Single(drawing.Entities);
    }

    [Fact]
    public void AnEditedEntityFromTheFileIsLoggedAsModified()
    {
        var drawing = SceneDrawing.CreateEmpty();
        var line = new SLine(Vec2.Zero, new Vec2(10, 0)) { SourceHandle = 0x2A };
        drawing.Add(line);

        var stack = new CommandStack(drawing);
        stack.Do(new TransformEntities(drawing.ActiveLayout, [line], Mat3.Translation(new Vec2(1, 1)), "Move"));

        var log = stack.Summarize();

        // A save rewrites these in place, rather than creating or deleting.
        Assert.Equal(0x2AUL, Assert.Single(log.Modified));
        Assert.Empty(log.Created);
        Assert.Empty(log.Deleted);
    }

    [Fact]
    public void ATransformThatCannotBeInvertedIsRefused()
    {
        var drawing = Scene(out var line);

        // Collapsing everything to a point cannot be undone, so it is not
        // allowed to happen in the first place.
        Assert.Throws<ArgumentException>(() =>
            new TransformEntities(drawing.ActiveLayout, [line], Mat3.Scaling(0, 0), "Scale"));
    }

    // ---- the tools --------------------------------------------------------

    [Fact]
    public void MoveReadsTheOffsetBetweenItsTwoPoints()
    {
        var tool = new MoveTool();

        Assert.Null(tool.Click(new Vec2(10, 10)));
        var transform = Assert.NotNull(tool.Click(new Vec2(40, 30)));

        Assert.Equal(new Vec2(30, 20), transform.Transform(Vec2.Zero));
        Assert.False(tool.Duplicates);
    }

    [Fact]
    public void CopyIsAMoveThatKeepsTheOriginal()
    {
        var tool = new CopyTool();
        tool.Click(new Vec2(0, 0));

        Assert.NotNull(tool.Click(new Vec2(5, 0)));
        Assert.True(tool.Duplicates);
    }

    [Fact]
    public void RotateTakesItsAngleFromTheSecondPoint()
    {
        var tool = new RotateTool();
        tool.Click(new Vec2(0, 0));

        // Straight up from the centre is a quarter turn counter-clockwise.
        var transform = Assert.NotNull(tool.Click(new Vec2(0, 10)));
        var moved = transform.Transform(new Vec2(10, 0));

        Assert.Equal(0, moved.X, 9);
        Assert.Equal(10, moved.Y, 9);
    }

    [Fact]
    public void ScaleIsTheRatioOfTheTwoDistancesFromTheBase()
    {
        var tool = new ScaleTool();

        // Three points: nothing is decided until the last of them.
        Assert.Null(tool.Click(new Vec2(0, 0)));
        Assert.Null(tool.Click(new Vec2(10, 0)));

        var transform = Assert.NotNull(tool.Click(new Vec2(25, 0)));

        // The reference was 10 from the base and now reaches 25, so 2.5x.
        Assert.Equal(new Vec2(5, 0), transform.Transform(new Vec2(2, 0)));
    }

    [Fact]
    public void MirrorReflectsInTheLineBetweenItsPoints()
    {
        var tool = new MirrorTool();
        tool.Click(new Vec2(0, 0));

        // The vertical axis.
        var transform = Assert.NotNull(tool.Click(new Vec2(0, 10)));
        var moved = transform.Transform(new Vec2(7, 3));

        Assert.Equal(-7, moved.X, 9);
        Assert.Equal(3, moved.Y, 9);

        // Mirror keeps the original, which is what mirroring is nearly
        // always for, and matches AutoCAD's default answer.
        Assert.True(tool.Duplicates);
    }

    [Fact]
    public void EveryModifyToolNeedsSomethingSelected()
    {
        ModifyTool[] tools = [new MoveTool(), new CopyTool(), new RotateTool(),
                              new ScaleTool(), new MirrorTool()];

        Assert.All(tools, tool => Assert.True(tool.NeedsSelection, $"{tool.Name} acts on a selection"));
        Assert.All(tools, tool => Assert.False(string.IsNullOrWhiteSpace(tool.Prompt)));
    }
}
