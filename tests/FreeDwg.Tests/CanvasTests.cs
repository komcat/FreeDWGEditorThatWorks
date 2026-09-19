using System.Windows;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Snapping;
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
            OnCanvas((canvas, drawing) => (drawing.Entities.Count, canvas.Commands!.CanUndo, canvas.Mode != CanvasMode.Select));

        Assert.Equal(0, entities);
        Assert.False(canUndo);
        Assert.False(hasTool);
    }

    [Fact]
    public void PickingPointsWithAToolCommitsAnEntity()
    {
        var (count, isRectangle, undoName) = OnCanvas((canvas, drawing) =>
        {
            canvas.UseTool(new RectangleTool());
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
            canvas.UseTool(new LineTool());

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
            canvas.UseTool(new PolylineTool());
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
            canvas.UseTool(new LineTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));

            // Half a line, and then the user changes their mind.
            canvas.UseTool(new CircleTool());
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
            canvas.UseTool(new LineTool());
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

    // ---- snapping, through the canvas -----------------------------------

    [Fact]
    public void ANewLineStartsExactlyOnTheEndOfAnOldOne()
    {
        var (start, kind) = OnCanvas((canvas, drawing) =>
        {
            canvas.Snapping.Modes = SnapModes.Endpoint;

            canvas.UseTool(new LineTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(100, 0));

            // Aim near the end of the first line, as a hand would.
            var aimed = new Vec2(100.4, 0.6);
            var resolved = canvas.ResolvePoint(aimed);

            canvas.PlaceToolPoint(resolved);
            canvas.PlaceToolPoint(new Vec2(100, 80));

            var second = (SLine)drawing.Entities[1];
            return (second.Start, canvas.ActiveSnap.Kind);
        });

        // Exactly, not nearly: two lines that meet to within a pixel are two
        // lines that do not meet, and everything downstream depends on this.
        Assert.Equal(new Vec2(100, 0), start);
        Assert.Equal(SnapKind.Endpoint, kind);
    }

    [Fact]
    public void OrthoSquaresTheNextPointWithTheLastOne()
    {
        var end = OnCanvas((canvas, drawing) =>
        {
            canvas.Snapping.Modes = SnapModes.None;
            canvas.Snapping.Ortho = true;

            canvas.UseTool(new LineTool());
            canvas.PlaceToolPoint(new Vec2(10, 10));

            // Nearly horizontal, and a long way from anything to snap to.
            canvas.PlaceToolPoint(canvas.ResolvePoint(new Vec2(90, 13)));

            return ((SLine)drawing.Entities[0]).End;
        });

        Assert.Equal(new Vec2(90, 10), end);
    }

    [Fact]
    public void WithNothingNearTheCursorThePointIsLeftWhereItWas()
    {
        var (point, found) = OnCanvas((canvas, drawing) =>
        {
            canvas.Snapping.Modes = SnapModes.Objects;

            var aimed = new Vec2(37.3, 91.7);
            return (canvas.ResolvePoint(aimed), canvas.ActiveSnap.Found);
        });

        Assert.Equal(new Vec2(37.3, 91.7), point);
        Assert.False(found);
    }

    // ---- one mode at a time ---------------------------------------------

    [Fact]
    public void ArmingTheZoomWindowPutsTheActiveToolAway()
    {
        var (mode, tool, entities) = OnCanvas((canvas, drawing) =>
        {
            canvas.UseTool(new CircleTool());
            canvas.UseZoomWindow();

            // The bug this replaces: the tool and the zoom window were
            // separate flags, so both buttons stayed lit while only one of
            // them decided what the next click did.
            canvas.PlaceToolPoint(new Vec2(10, 10));
            canvas.PlaceToolPoint(new Vec2(20, 10));

            return (canvas.Mode, canvas.Tool, drawing.Entities.Count);
        });

        Assert.Equal(CanvasMode.ZoomWindow, mode);
        Assert.Null(tool);
        Assert.Equal(0, entities);
    }

    [Fact]
    public void StartingAToolCancelsAnArmedZoomWindow()
    {
        var (mode, isCircle) = OnCanvas((canvas, drawing) =>
        {
            canvas.UseZoomWindow();
            canvas.UseTool(new CircleTool());

            return (canvas.Mode, canvas.Tool is CircleTool);
        });

        Assert.Equal(CanvasMode.Draw, mode);
        Assert.True(isCircle);
    }

    [Fact]
    public void EveryModeSwitchIsAnnouncedExactlyOnce()
    {
        int changes = OnCanvas((canvas, drawing) =>
        {
            int count = 0;
            canvas.ModeChanged += (_, _) => count++;

            canvas.UseTool(new LineTool());
            canvas.UseZoomWindow();
            canvas.UseSelect();

            // The toolbar is redrawn from this event, so a mode change that
            // does not raise it leaves a button lying about what is active.
            return count;
        });

        Assert.Equal(3, changes);
    }

    // ---- selection and erasing ------------------------------------------

    [Fact]
    public void SomethingDrawnCanThenBePickedAndErased()
    {
        var (selected, afterErase, restored) = OnCanvas((canvas, drawing) =>
        {
            canvas.UseTool(new LineTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(100, 0));

            canvas.UseSelect();
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
            canvas.UseTool(new CircleTool());
            canvas.PlaceToolPoint(new Vec2(50, 50));
            canvas.PlaceToolPoint(new Vec2(80, 50));

            canvas.UseSelect();
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
            canvas.UseTool(new LineTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(100, 0));

            canvas.UseSelect();
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
            canvas.UseTool(new LineTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(100, 0));

            canvas.UseSelect();
            canvas.PickAt(new Vec2(50, 0));
            Assert.Equal(1, canvas.Selection.Count);

            // Drawing and selecting are different modes; carrying a selection
            // into a draw tool only makes the next Delete a surprise.
            canvas.UseTool(new CircleTool());
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

            canvas.UseTool(new LineTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(100, 0));

            var entity = drawing.Entities[0];
            return (entity.LayerIndex, entity.Style.Color);
        });

        Assert.Equal(1, layerIndex);
        Assert.Equal(new FreeDwg.Core.Styling.Rgb(255, 0, 0), red);
    }
}
