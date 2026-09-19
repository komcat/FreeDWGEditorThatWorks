using System.Windows;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Tools;
using FreeDwg.Tests.Rendering;
using FreeDWGEditorThatWorks.Controls;
using SceneDrawing = FreeDwg.Core.Scene.Drawing;

namespace FreeDwg.Tests;

/// <summary>
/// The canvas as the shell's editing surface: picking a tool, feeding it
/// points, selecting, erasing, undoing.
/// </summary>
/// <remarks>
/// Driven through the canvas's own world-coordinate entry points rather than
/// through synthesised mouse messages. WPF reads the pointer from the live
/// mouse device, so a fake click is either ignored or lands wherever the real
/// cursor happens to be -- and a test that moves the cursor is a test that
/// clicks on whatever the person at the machine is doing.
/// </remarks>
public sealed class CanvasTests
{
    /// <summary>Runs a body against a laid-out canvas on an STA thread.</summary>
    private static T OnCanvas<T>(Func<CadCanvas, SceneDrawing, T> body) => StaRenderer.OnSta(() =>
    {
        var canvas = new CadCanvas { Width = 800, Height = 600 };
        canvas.Measure(new Size(800, 600));
        canvas.Arrange(new Rect(0, 0, 800, 600));

        var drawing = SceneDrawing.CreateEmpty();
        canvas.Drawing = drawing;
        canvas.UpdateLayout();

        return body(canvas, drawing);
    });

    [Fact]
    public void ANewCanvasHasAnEmptyDrawingAndAnEmptyHistory()
    {
        var (entities, canUndo, hasTool) =
            OnCanvas((canvas, drawing) => (drawing.Entities.Count, canvas.Commands!.CanUndo, canvas.Tool is not null));

        Assert.Equal(0, entities);
        Assert.False(canUndo);
        Assert.False(hasTool);
    }

    [Fact]
    public void PickingPointsWithAToolCommitsAnEntity()
    {
        var (count, isRectangle, undoName) = OnCanvas((canvas, drawing) =>
        {
            canvas.Tool = new RectangleTool();
            canvas.PlaceToolPoint(new Vec2(10, 10));

            // One corner is not a rectangle yet.
            Assert.Empty(drawing.Entities);

            canvas.PlaceToolPoint(new Vec2(60, 40));

            return (drawing.Entities.Count, drawing.Entities[0] is SPolyline, canvas.Commands!.UndoName);
        });

        Assert.Equal(1, count);
        Assert.True(isRectangle);
        Assert.Equal("Draw polyline", undoName);
    }

    [Fact]
    public void AToolStaysActiveSoSeveralObjectsCanBeDrawnInARow()
    {
        int count = OnCanvas((canvas, drawing) =>
        {
            canvas.Tool = new LineTool();

            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(10, 0));
            canvas.PlaceToolPoint(new Vec2(0, 5));
            canvas.PlaceToolPoint(new Vec2(10, 5));

            return drawing.Entities.Count;
        });

        Assert.Equal(2, count);
    }

    [Fact]
    public void AnOpenEndedToolIsCommittedByFinishing()
    {
        var (count, vertices) = OnCanvas((canvas, drawing) =>
        {
            canvas.Tool = new PolylineTool();
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(10, 0));
            canvas.PlaceToolPoint(new Vec2(10, 10));

            Assert.Empty(drawing.Entities);
            canvas.FinishTool();

            return (drawing.Entities.Count, (drawing.Entities.FirstOrDefault() as SPolyline)?.Vertices.Length ?? 0);
        });

        Assert.Equal(1, count);
        Assert.Equal(3, vertices);
    }

    [Fact]
    public void SwitchingToolsMidPickThrowsAwayTheHalfFinishedObject()
    {
        int count = OnCanvas((canvas, drawing) =>
        {
            canvas.Tool = new LineTool();
            canvas.PlaceToolPoint(new Vec2(0, 0));

            // Half a line, and then the user changes their mind.
            canvas.Tool = new CircleTool();
            canvas.PlaceToolPoint(new Vec2(50, 50));
            canvas.PlaceToolPoint(new Vec2(60, 50));

            return drawing.Entities.Count;
        });

        // The circle, and no line built from a stray leftover point.
        Assert.Equal(1, count);
    }

    [Fact]
    public void EverythingDrawnCanBeUndoneAndRedone()
    {
        var (afterUndo, afterRedo) = OnCanvas((canvas, drawing) =>
        {
            canvas.Tool = new LineTool();
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(100, 0));

            canvas.Undo();
            int undone = drawing.Entities.Count;

            canvas.Redo();
            return (undone, drawing.Entities.Count);
        });

        Assert.Equal(0, afterUndo);
        Assert.Equal(1, afterRedo);
    }

    // ---- selection and erasing ------------------------------------------

    [Fact]
    public void SomethingDrawnCanThenBePickedAndErased()
    {
        var (selected, afterErase, restored) = OnCanvas((canvas, drawing) =>
        {
            canvas.Tool = new LineTool();
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(100, 0));

            canvas.Tool = null;
            canvas.PickAt(new Vec2(50, 0));
            int picked = canvas.Selection.Count;

            canvas.EraseSelection();
            int erased = drawing.Entities.Count;

            canvas.Undo();
            return (picked, erased, drawing.Entities.Count);
        });

        Assert.Equal(1, selected);
        Assert.Equal(0, afterErase);
        Assert.Equal(1, restored);
    }

    [Fact]
    public void UndoingADrawDropsItFromTheSelection()
    {
        var (before, after) = OnCanvas((canvas, drawing) =>
        {
            canvas.Tool = new CircleTool();
            canvas.PlaceToolPoint(new Vec2(50, 50));
            canvas.PlaceToolPoint(new Vec2(80, 50));

            canvas.Tool = null;
            canvas.PickAt(new Vec2(80, 50));
            int picked = canvas.Selection.Count;

            // Highlighting an entity that is no longer in the drawing would
            // paint it back onto the screen after it had been taken away.
            canvas.Undo();
            return (picked, canvas.Selection.Count);
        });

        Assert.Equal(1, before);
        Assert.Equal(0, after);
    }

    [Fact]
    public void PickingATargetlessPointWithNoToolClearsTheSelection()
    {
        int count = OnCanvas((canvas, drawing) =>
        {
            canvas.Tool = new LineTool();
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(100, 0));

            canvas.Tool = null;
            canvas.PickAt(new Vec2(50, 0));
            canvas.PickAt(new Vec2(50, 500));

            return canvas.Selection.Count;
        });

        Assert.Equal(0, count);
    }

    [Fact]
    public void StartingAToolClearsWhateverWasSelected()
    {
        int count = OnCanvas((canvas, canvasDrawing) =>
        {
            canvas.Tool = new LineTool();
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(100, 0));

            canvas.Tool = null;
            canvas.PickAt(new Vec2(50, 0));
            Assert.Equal(1, canvas.Selection.Count);

            // Drawing and selecting are different modes; carrying a selection
            // into a draw tool only makes the next Delete a surprise.
            canvas.Tool = new CircleTool();
            return canvas.Selection.Count;
        });

        Assert.Equal(0, count);
    }

    [Fact]
    public void NewGeometryLandsOnTheCurrentLayer()
    {
        var (layerIndex, red) = OnCanvas((canvas, drawing) =>
        {
            drawing.AddLayer(new FreeDwg.Core.Scene.Layer("RED")
            {
                Color = new FreeDwg.Core.Styling.Rgb(255, 0, 0),
            });
            drawing.CurrentLayerIndex = 1;

            canvas.Tool = new LineTool();
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(100, 0));

            var entity = drawing.Entities[0];
            return (entity.LayerIndex, entity.Style.Color);
        });

        Assert.Equal(1, layerIndex);
        Assert.Equal(new FreeDwg.Core.Styling.Rgb(255, 0, 0), red);
    }
}
