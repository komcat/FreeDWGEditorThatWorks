using FreeDwg.Core.Commands;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;
using SceneDrawing = FreeDwg.Core.Scene.Drawing;

namespace FreeDwg.Tests;

/// <summary>
/// The undo stack, and the change log a save will be built from.
/// </summary>
/// <remarks>
/// The property worth pinning down is that undo restores the drawing
/// <em>exactly</em>, entity order included: order is painting order, so an
/// entity that comes back on top of what it used to sit under has not really
/// been restored.
/// </remarks>
public sealed class CommandTests
{
    private static SceneDrawing Scene(out SLine a, out SLine b, out SLine c)
    {
        var drawing = SceneDrawing.CreateEmpty();

        a = new SLine(new Vec2(0, 0), new Vec2(10, 0));
        b = new SLine(new Vec2(0, 5), new Vec2(10, 5));
        c = new SLine(new Vec2(0, 10), new Vec2(10, 10));

        drawing.Add(a);
        drawing.Add(b);
        drawing.Add(c);
        return drawing;
    }

    [Fact]
    public void DrawingAnEntityIsUndoneAndRedone()
    {
        var drawing = SceneDrawing.CreateEmpty();
        var stack = new CommandStack(drawing);
        var line = new SLine(new Vec2(0, 0), new Vec2(10, 10));

        stack.Do(new AddEntities(drawing.ActiveLayout, line));
        Assert.Single(drawing.Entities);
        Assert.True(stack.CanUndo);

        Assert.True(stack.Undo());
        Assert.Empty(drawing.Entities);

        Assert.True(stack.Redo());
        Assert.Same(line, Assert.Single(drawing.Entities));
    }

    [Fact]
    public void UndoingADeleteRestoresPaintingOrder()
    {
        var drawing = Scene(out var a, out var b, out var c);
        var stack = new CommandStack(drawing);

        // Take the first and last, leaving a hole at either end.
        stack.Do(new DeleteEntities(drawing.ActiveLayout, [c, a]));
        Assert.Same(b, Assert.Single(drawing.Entities));

        stack.Undo();

        Assert.Equal<object>([a, b, c], drawing.Entities);
    }

    [Fact]
    public void AnEditAfterAnUndoAbandonsWhatWasUndone()
    {
        var drawing = SceneDrawing.CreateEmpty();
        var stack = new CommandStack(drawing);

        stack.Do(new AddEntities(drawing.ActiveLayout, new SLine(Vec2.Zero, new Vec2(1, 0))));
        stack.Undo();
        Assert.True(stack.CanRedo);

        stack.Do(new AddEntities(drawing.ActiveLayout, new SCircle(Vec2.Zero, 5)));

        Assert.False(stack.CanRedo);
        Assert.IsType<SCircle>(Assert.Single(drawing.Entities));
    }

    [Fact]
    public void TheUndoStackNamesWhatItWillTakeBack()
    {
        var drawing = SceneDrawing.CreateEmpty();
        var stack = new CommandStack(drawing);

        Assert.Null(stack.UndoName);

        stack.Do(new AddEntities(drawing.ActiveLayout, new SCircle(Vec2.Zero, 5)));
        Assert.Equal("Draw circle", stack.UndoName);

        stack.Undo();
        Assert.Equal("Draw circle", stack.RedoName);
    }

    // ---- the change log --------------------------------------------------

    [Fact]
    public void AnEntityFromTheFileIsRecordedAsDeletedByItsHandle()
    {
        var drawing = SceneDrawing.CreateEmpty();
        var fromFile = new SLine(Vec2.Zero, new Vec2(10, 0)) { SourceHandle = 0x2A };
        drawing.Add(fromFile);

        var stack = new CommandStack(drawing);
        stack.Do(new DeleteEntities(drawing.ActiveLayout, [fromFile]));

        var log = stack.Summarize();
        Assert.Equal(0x2AUL, Assert.Single(log.Deleted));
        Assert.Empty(log.Created);
    }

    [Fact]
    public void AnEntityDrawnAndThenErasedLeavesNothingToWrite()
    {
        var drawing = SceneDrawing.CreateEmpty();
        var stack = new CommandStack(drawing);
        var line = new SLine(Vec2.Zero, new Vec2(10, 0));

        stack.Do(new AddEntities(drawing.ActiveLayout, line));
        stack.Do(new DeleteEntities(drawing.ActiveLayout, [line]));

        // It never reached the file, so there is no handle to delete and
        // nothing to create: the two cancel out entirely.
        Assert.True(stack.Summarize().IsEmpty);
    }

    [Fact]
    public void UndoingAnEditTakesItOutOfTheChangeLog()
    {
        var drawing = SceneDrawing.CreateEmpty();
        var stack = new CommandStack(drawing);

        stack.Do(new AddEntities(drawing.ActiveLayout, new SLine(Vec2.Zero, new Vec2(10, 0))));
        Assert.Single(stack.Summarize().Created);

        stack.Undo();

        // The log is replayed from the stack, so this cannot drift.
        Assert.True(stack.Summarize().IsEmpty);
    }

    [Fact]
    public void TheDrawingIsModifiedUntilItIsSaved()
    {
        var drawing = SceneDrawing.CreateEmpty();
        var stack = new CommandStack(drawing);

        Assert.False(stack.IsModified);

        stack.Do(new AddEntities(drawing.ActiveLayout, new SCircle(Vec2.Zero, 5)));
        Assert.True(stack.IsModified);

        stack.Undo();
        Assert.False(stack.IsModified);

        stack.Redo();
        stack.MarkSaved();
        Assert.False(stack.IsModified);

        stack.Undo();
        Assert.True(stack.IsModified);
    }

    // ---- the caches an edit has to invalidate ---------------------------

    [Fact]
    public void AddingAndRemovingKeepsBoundsAndTheIndexHonest()
    {
        var drawing = SceneDrawing.CreateEmpty();
        var stack = new CommandStack(drawing);
        var layout = drawing.ActiveLayout;

        Assert.True(layout.Bounds.IsEmpty);

        var circle = new SCircle(new Vec2(50, 50), 10);
        stack.Do(new AddEntities(layout, circle));

        // Bounds and the spatial index are both cached, and an edit that left
        // either stale would cull the new entity or fail to pick it.
        Assert.Equal(40, layout.Bounds.MinX, 9);
        Assert.Equal(1, layout.Index.Count);

        stack.Undo();

        Assert.True(layout.Bounds.IsEmpty);
        Assert.Equal(0, layout.Index.Count);
    }

    [Fact]
    public void ANewDrawingHasTheLayerEveryDwgHas()
    {
        var drawing = SceneDrawing.CreateEmpty();

        var layer = Assert.Single(drawing.Layers);
        Assert.Equal("0", layer.Name);
        Assert.Same(layer, drawing.CurrentLayer);
        Assert.Empty(drawing.Entities);
    }

    [Fact]
    public void PlacingAnEntityStampsItWithTheCurrentLayerAndItsStyle()
    {
        var drawing = SceneDrawing.CreateEmpty();
        drawing.AddLayer(new Layer("RED") { Color = new FreeDwg.Core.Styling.Rgb(255, 0, 0) });
        drawing.CurrentLayerIndex = 1;

        var line = drawing.Place(new SLine(Vec2.Zero, new Vec2(10, 0)));

        // Styles are resolved at import, so a new entity cannot say ByLayer:
        // it has to be given the layer's colour outright.
        Assert.Equal(1, line.LayerIndex);
        Assert.Equal(new FreeDwg.Core.Styling.Rgb(255, 0, 0), line.Style.Color);
    }
}
