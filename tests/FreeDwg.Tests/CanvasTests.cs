using System.Windows;
using FreeDwg.Core.Commands;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Styling;
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

    // ---- modify tools ----------------------------------------------------

    /// <summary>Draws a line, selects it, and leaves it ready to be modified.</summary>
    private static SLine DrawAndSelect(CadCanvas canvas, SceneDrawing drawing)
    {
        canvas.Snapping.Modes = SnapModes.None;

        canvas.UseTool(new LineTool());
        canvas.PlaceToolPoint(new Vec2(0, 0));
        canvas.PlaceToolPoint(new Vec2(100, 0));

        canvas.UseSelect();
        canvas.PickAt(new Vec2(50, 0));

        return (SLine)drawing.Entities[0];
    }

    [Fact]
    public void MovingTheSelectionMovesItAndNothingElse()
    {
        var (count, start, end) = OnCanvas((canvas, drawing) =>
        {
            var line = DrawAndSelect(canvas, drawing);

            canvas.UseTool(new MoveTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(10, 40));

            return (drawing.Entities.Count, line.Start, line.End);
        });

        // Moved, not copied.
        Assert.Equal(1, count);
        Assert.Equal(new Vec2(10, 40), start);
        Assert.Equal(new Vec2(110, 40), end);
    }

    [Fact]
    public void AModifyToolKeepsTheSelectionItActsOn()
    {
        int selected = OnCanvas((canvas, drawing) =>
        {
            DrawAndSelect(canvas, drawing);

            // Every other tool clears the selection on the way in; a modify
            // tool cannot, because the selection is its subject.
            canvas.UseTool(new MoveTool());
            return canvas.Selection.Count;
        });

        Assert.Equal(1, selected);
    }

    [Fact]
    public void CopyingLeavesTheOriginalAndSelectsTheCopy()
    {
        var (count, originalStart, selectedIsCopy) = OnCanvas((canvas, drawing) =>
        {
            var line = DrawAndSelect(canvas, drawing);

            canvas.UseTool(new CopyTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(0, 50));

            return (drawing.Entities.Count, line.Start,
                    canvas.Selection.Ordered.Count == 1 && !ReferenceEquals(canvas.Selection.Ordered[0], line));
        });

        Assert.Equal(2, count);
        Assert.Equal(new Vec2(0, 0), originalStart);

        // Selecting the copy rather than the original is what stops a second
        // copy silently duplicating the wrong thing.
        Assert.True(selectedIsCopy);
    }

    [Fact]
    public void UndoingAMoveTakesTheGeometryBack()
    {
        var start = OnCanvas((canvas, drawing) =>
        {
            var line = DrawAndSelect(canvas, drawing);

            canvas.UseTool(new MoveTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(70, 70));

            canvas.Undo();
            return line.Start;
        });

        Assert.Equal(new Vec2(0, 0), start);
    }

    [Fact]
    public void AModifyToolWithNothingSelectedChangesNothing()
    {
        var (count, mode) = OnCanvas((canvas, drawing) =>
        {
            canvas.UseTool(new LineTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(100, 0));

            // Nothing picked, so there is nothing for a move to act on. The
            // clicks have to fall through harmlessly rather than throw.
            canvas.UseSelect();
            canvas.UseTool(new MoveTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(50, 50));

            return (drawing.Entities.Count, canvas.Mode);
        });

        Assert.Equal(1, count);
        Assert.Equal(CanvasMode.Draw, mode);
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

    // ---- entity tools ----------------------------------------------------

    [Fact]
    public void AFilletThroughTheCanvasRoundsTheCorner()
    {
        var (count, radius) = OnCanvas((canvas, drawing) =>
        {
            canvas.Snapping.Modes = SnapModes.None;

            canvas.UseTool(new LineTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(100, 0));
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(0, 100));

            // Through the canvas's setting, which is the only one that
            // counts: a radius put on the tool itself is overwritten.
            canvas.FilletRadius = 10;
            canvas.UseTool(new FilletTool());

            canvas.PickEntityForTool(new Vec2(90, 0));
            canvas.PickEntityForTool(new Vec2(0, 90));

            var arc = drawing.Entities.OfType<SArc>().SingleOrDefault();
            return (drawing.Entities.Count, arc?.Radius ?? -1);
        });

        // Two trimmed lines and the arc between them.
        Assert.Equal(3, count);
        Assert.Equal(10, radius, 9);
    }

    [Fact]
    public void TheFilletRadiusReachesAToolPickedAfterItWasSet()
    {
        double radius = OnCanvas((canvas, drawing) =>
        {
            // Set the size first, the way someone types it before reaching
            // for the tool.
            canvas.FilletRadius = 12;
            canvas.UseTool(new FilletTool());

            return ((FilletTool)canvas.Tool!).Radius;
        });

        Assert.Equal(12, radius, 9);
    }

    [Fact]
    public void TheFilletRadiusReachesAToolAlreadyRunning()
    {
        double radius = OnCanvas((canvas, drawing) =>
        {
            canvas.UseTool(new FilletTool());

            // And the other order: tool first, then the size, which is what
            // anyone does the first time they meet the box.
            canvas.FilletRadius = 8;

            return ((FilletTool)canvas.Tool!).Radius;
        });

        Assert.Equal(8, radius, 9);
    }

    [Fact]
    public void HoveringACircleWhileDrawingGivesATangentPoint()
    {
        var (kind, tangent, from) = OnCanvas((canvas, drawing) =>
        {
            canvas.Snapping.Modes = SnapModes.Tangent;

            canvas.UseTool(new CircleTool());
            canvas.PlaceToolPoint(new Vec2(200, 0));
            canvas.PlaceToolPoint(new Vec2(250, 0));

            // Start a line well to the left, then hover the right of the rim.
            canvas.UseTool(new LineTool());
            var start = new Vec2(0, 0);
            canvas.PlaceToolPoint(start);

            var resolved = canvas.ResolvePoint(new Vec2(250, 0));
            return (canvas.ActiveSnap.Kind, resolved, start);
        });

        Assert.Equal(SnapKind.Tangent, kind);

        // Through the canvas, end to end: the point is on the rim and the
        // radius there is square to the line coming in.
        Assert.Equal(50, Vec2.Distance(tangent, new Vec2(200, 0)), 6);
        Assert.Equal(0.0, Vec2.Dot((tangent - new Vec2(200, 0)).Normalized(),
                                   (from - tangent).Normalized()), 6);
    }

    // ---- typed lengths ---------------------------------------------------

    [Fact]
    public void ATypedLengthRunsTheDistanceAskedForInTheCursorsDirection()
    {
        var end = OnCanvas((canvas, drawing) =>
        {
            canvas.Snapping.Modes = SnapModes.None;

            canvas.UseTool(new LineTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));

            // Aimed up and to the right, roughly but not exactly at 45.
            canvas.ResolvePoint(new Vec2(83, 91));
            canvas.PlaceTypedLength(100);

            return ((SLine)drawing.Entities[0]).End;
        });

        // The length is exactly what was asked for, and the direction is the
        // one the cursor was pointing in.
        Assert.Equal(100, Vec2.Distance(Vec2.Zero, end), 9);
        Assert.Equal(new Vec2(83, 91).Angle(), end.Angle(), 9);
    }

    [Fact]
    public void ATypedLengthFollowsWhateverSnappingSetTheAngleTo()
    {
        var end = OnCanvas((canvas, drawing) =>
        {
            canvas.Snapping.Modes = SnapModes.Polar;
            canvas.Snapping.PolarAngle = 45;
            canvas.Snapping.PolarRelative = false;

            canvas.UseTool(new LineTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));

            // Near the forty-five degree ray, so polar pins the angle and
            // the keyboard gives the length. That pairing is the whole point.
            canvas.ResolvePoint(new Vec2(60, 59));
            canvas.PlaceTypedLength(100);

            return ((SLine)drawing.Entities[0]).End;
        });

        Assert.Equal(100, Vec2.Distance(Vec2.Zero, end), 9);
        Assert.Equal(45.0, end.Angle() * 180 / Math.PI, 6);
    }

    [Fact]
    public void ATypedLengthNeedsADirectionToRunIn()
    {
        var (placed, count) = OnCanvas((canvas, drawing) =>
        {
            canvas.Snapping.Modes = SnapModes.None;

            canvas.UseTool(new LineTool());
            canvas.PlaceToolPoint(new Vec2(10, 10));

            // Cursor still sitting on the point it would run from: there is
            // no direction, and no sensible answer to give.
            canvas.ResolvePoint(new Vec2(10, 10));

            return (canvas.PlaceTypedLength(50), drawing.Entities.Count);
        });

        Assert.False(placed);
        Assert.Equal(0, count);
    }

    [Fact]
    public void ThereIsNoPendingLengthUntilAToolHasAPoint()
    {
        var (idle, drawing_) = OnCanvas((canvas, drawing) =>
        {
            bool none = canvas.PendingLength is null;

            canvas.UseTool(new LineTool());
            bool stillNone = canvas.PendingLength is null;

            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.ResolvePoint(new Vec2(30, 40));

            return (none && stillNone, canvas.PendingLength);
        });

        Assert.True(idle);
        Assert.Equal(50, drawing_!.Value, 9);
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

    // ---- grips -------------------------------------------------------------

    /// <summary>A line from (0,0) to (100,0), drawn and then selected.</summary>
    private static SLine SelectALine(CadCanvas canvas, SceneDrawing drawing)
    {
        canvas.UseTool(new LineTool());
        canvas.PlaceToolPoint(new Vec2(0, 0));
        canvas.PlaceToolPoint(new Vec2(100, 0));

        canvas.UseSelect();
        canvas.PickAt(new Vec2(50, 0));

        return (SLine)drawing.Entities[0];
    }

    [Fact]
    public void SelectingSomethingPutsHandlesOnIt()
    {
        var points = OnCanvas((canvas, drawing) =>
        {
            Assert.Empty(canvas.Grips);

            SelectALine(canvas, drawing);
            var grips = canvas.Grips.Select(grip => grip.Point).ToArray();

            canvas.Selection.Clear();
            Assert.Empty(canvas.Grips);

            return grips;
        });

        Assert.Equal([new Vec2(0, 0), new Vec2(100, 0), new Vec2(50, 0)], points);
    }

    [Fact]
    public void ADrawToolTakesTheHandlesAway()
    {
        int grips = OnCanvas((canvas, drawing) =>
        {
            SelectALine(canvas, drawing);
            Assert.NotEmpty(canvas.Grips);

            // A tool acts on the selection as a whole, so handles on its
            // parts would offer an edit the next click is not going to make.
            canvas.UseTool(new MoveTool());
            return canvas.Grips.Count;
        });

        Assert.Equal(0, grips);
    }

    [Fact]
    public void DraggingAnEndGripStretchesTheLineAndUndoPutsItBack()
    {
        var (end, undoName, afterUndo, count) = OnCanvas((canvas, drawing) =>
        {
            SelectALine(canvas, drawing);

            Assert.True(canvas.BeginGripDrag(new Vec2(100, 0)));
            canvas.DragGripTo(new Vec2(100, 40));
            Assert.True(canvas.EndGripDrag(new Vec2(100, 40)));

            var stretched = ((SLine)drawing.Entities[0]).End;
            string? name = canvas.Commands!.UndoName;

            canvas.Undo();

            return (stretched, name, ((SLine)drawing.Entities[0]).End, drawing.Entities.Count);
        });

        Assert.Equal(new Vec2(100, 40), end);
        Assert.Equal("Stretch", undoName);
        Assert.Equal(new Vec2(100, 0), afterUndo);

        // Swapped for an edited copy, not added alongside the original.
        Assert.Equal(1, count);
    }

    [Fact]
    public void AStretchedEntityKeepsTheHandleTheFileKnowsItBy()
    {
        var (handle, selected) = OnCanvas((canvas, drawing) =>
        {
            var line = SelectALine(canvas, drawing);
            line.SourceHandle = 0x2F;

            canvas.BeginGripDrag(new Vec2(0, 0));
            canvas.EndGripDrag(new Vec2(-20, 0));

            var replacement = drawing.Entities[0];

            // The replacement, not the original: a grip drag that left the
            // old object selected would leave grips on geometry that is no
            // longer in the drawing.
            return (replacement.SourceHandle, ReferenceEquals(canvas.Selection.Ordered[0], replacement));
        });

        Assert.Equal(0x2FUL, handle);
        Assert.True(selected);
    }

    [Fact]
    public void DraggingACentreGripMovesTheWholeObject()
    {
        var (center, radius, undoName) = OnCanvas((canvas, drawing) =>
        {
            canvas.UseTool(new CircleTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(30, 0));

            canvas.UseSelect();
            canvas.PickAt(new Vec2(30, 0));

            Assert.True(canvas.BeginGripDrag(new Vec2(0, 0)));
            Assert.True(canvas.EndGripDrag(new Vec2(60, 80)));

            var circle = (SCircle)drawing.Entities[0];
            return (circle.Center, circle.Radius, canvas.Commands!.UndoName);
        });

        Assert.Equal(new Vec2(60, 80), center);
        Assert.Equal(30, radius);

        // A move is a transform of the whole thing, undone by its inverse,
        // rather than a swap for an edited copy.
        Assert.Equal("Move", undoName);
    }

    [Fact]
    public void HandlesFollowTheGeometryBackThroughAnUndo()
    {
        var (moved, restored) = OnCanvas((canvas, drawing) =>
        {
            canvas.UseTool(new CircleTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(30, 0));

            canvas.UseSelect();
            canvas.PickAt(new Vec2(30, 0));

            canvas.BeginGripDrag(new Vec2(0, 0));
            canvas.EndGripDrag(new Vec2(60, 80));
            var after = canvas.Grips[0].Point;

            canvas.Undo();

            // A square left where the geometry used to be is an edit offered
            // on something that is not there, which is the whole reason the
            // handles are rebuilt rather than kept.
            return (after, canvas.Grips[0].Point);
        });

        Assert.Equal(new Vec2(60, 80), moved);
        Assert.Equal(new Vec2(0, 0), restored);
    }

    [Fact]
    public void AGripGrabbedAndDroppedWhereItWasIsNotAnEdit()
    {
        bool canUndo = OnCanvas((canvas, drawing) =>
        {
            SelectALine(canvas, drawing);
            while (canvas.Commands!.CanUndo) canvas.Undo();

            canvas.UseTool(new LineTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(100, 0));
            canvas.UseSelect();
            canvas.PickAt(new Vec2(50, 0));

            var before = canvas.Commands.UndoName;

            Assert.True(canvas.BeginGripDrag(new Vec2(100, 0)));
            Assert.False(canvas.EndGripDrag(new Vec2(100, 0)));

            // Still the draw, not a do-nothing stretch sitting on top of it.
            Assert.Equal(before, canvas.Commands.UndoName);
            return canvas.Commands.UndoName == "Draw line";
        });

        Assert.True(canUndo);
    }

    [Fact]
    public void ClickingClearOfEveryHandleIsNotAGrab()
    {
        var (near, far) = OnCanvas((canvas, drawing) =>
        {
            SelectALine(canvas, drawing);

            bool caught = canvas.BeginGripDrag(new Vec2(100, canvas.GripTolerance / 2));
            canvas.CancelGripDrag();

            // Clear of every handle, so the click is free to select instead.
            return (caught, canvas.BeginGripDrag(new Vec2(100, canvas.GripTolerance * 3)));
        });

        Assert.True(near);
        Assert.False(far);
    }

    [Fact]
    public void LettingGoOfAGripWithEscapeChangesNothing()
    {
        var end = OnCanvas((canvas, drawing) =>
        {
            SelectALine(canvas, drawing);

            canvas.BeginGripDrag(new Vec2(100, 0));
            canvas.DragGripTo(new Vec2(100, 40));
            canvas.CancelGripDrag();

            Assert.False(canvas.IsDraggingGrip);
            Assert.False(canvas.EndGripDrag(new Vec2(100, 40)));

            return ((SLine)drawing.Entities[0]).End;
        });

        Assert.Equal(new Vec2(100, 0), end);
    }

    [Fact]
    public void ATypedLengthFinishesAGripDragAtThatDistance()
    {
        var end = OnCanvas((canvas, drawing) =>
        {
            SelectALine(canvas, drawing);

            canvas.BeginGripDrag(new Vec2(100, 0));

            // The cursor gives the direction and the keyboard the distance,
            // exactly as it does for a point being picked.
            canvas.DragGripTo(new Vec2(150, 0));
            Assert.True(canvas.PlaceTypedLength(25));

            return ((SLine)drawing.Entities[0]).End;
        });

        Assert.Equal(new Vec2(125, 0), end);
    }

    [Fact]
    public void AGripDragLeavesTheRestOfTheSelectionAlone()
    {
        var (count, stretched) = OnCanvas((canvas, drawing) =>
        {
            canvas.UseTool(new LineTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(100, 0));
            canvas.PlaceToolPoint(new Vec2(0, 50));
            canvas.PlaceToolPoint(new Vec2(100, 50));

            canvas.UseSelect();
            canvas.PickAt(new Vec2(50, 0));
            canvas.PickAt(new Vec2(50, 50), extend: true);
            Assert.Equal(2, canvas.Selection.Count);

            canvas.BeginGripDrag(new Vec2(100, 50));
            canvas.EndGripDrag(new Vec2(140, 50));

            return (canvas.Selection.Count, ((SLine)drawing.Entities[1]).End);
        });

        // Both still selected, with the replacement swapped in for the one
        // that was edited.
        Assert.Equal(2, count);
        Assert.Equal(new Vec2(140, 50), stretched);
    }

    // ---- dimensions and their layer ----------------------------------------

    /// <summary>Draws one dimension across the bottom of an 80 by 40 box.</summary>
    private static void DrawADimension(CadCanvas canvas)
    {
        canvas.UseTool(new LinearDimensionTool());
        canvas.PlaceToolPoint(new Vec2(0, 0));
        canvas.PlaceToolPoint(new Vec2(80, 0));
        canvas.PlaceToolPoint(new Vec2(40, -20));
    }

    [Fact]
    public void ADimensionIsDrawnFromThreePointsAndMeasuresTheFirstTwo()
    {
        var (count, measurement, text, undoName) = OnCanvas((canvas, drawing) =>
        {
            canvas.UseTool(new LinearDimensionTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(80, 0));

            // Two points is a span, not yet a dimension: where the line goes
            // is what says which of the two distances is being measured.
            Assert.Empty(drawing.Entities);

            canvas.PlaceToolPoint(new Vec2(40, -20));

            var dimension = (SDimension)drawing.Entities[0];
            return (drawing.Entities.Count, dimension.Solve().Measurement,
                    dimension.MeasurementText, canvas.Commands!.UndoName);
        });

        Assert.Equal(1, count);
        Assert.Equal(80, measurement, 9);
        Assert.Equal("80", text);
        Assert.Equal("Draw dimension", undoName);
    }

    [Fact]
    public void TheFirstDimensionMakesTheDimensionLayerAndLandsOnItInGreen()
    {
        var (layers, name, onIt, green) = OnCanvas((canvas, drawing) =>
        {
            Assert.Single(drawing.Layers);

            DrawADimension(canvas);

            var dimension = drawing.Entities[0];
            var layer = drawing.Layers[dimension.LayerIndex];

            return (drawing.Layers.Count, layer.Name, layer.Name == CadCanvas.DimensionLayerName,
                    dimension.Style.Color.G > dimension.Style.Color.R);
        });

        Assert.Equal(2, layers);
        Assert.Equal("Dimensions", name);
        Assert.True(onIt, "a dimension belongs on the dimension layer");
        Assert.True(green, "the dimension layer is green, as every template has it");
    }

    [Fact]
    public void ADimensionGoesOnTheDimensionLayerWhateverLayerIsCurrent()
    {
        var (layerName, current) = OnCanvas((canvas, drawing) =>
        {
            drawing.AddLayer(new FreeDwg.Core.Scene.Layer("WALLS"));
            drawing.CurrentLayerIndex = 1;

            DrawADimension(canvas);

            // Annotation is not geometry: it belongs where it can be turned
            // off in one go, not on whatever was being drawn on at the time.
            return (drawing.Layers[drawing.Entities[0].LayerIndex].Name, drawing.CurrentLayerIndex);
        });

        Assert.Equal("Dimensions", layerName);
        Assert.Equal(1, current);
    }

    [Fact]
    public void MakingTheLayerAndTheDimensionIsOneUndoStep()
    {
        var (layers, entities) = OnCanvas((canvas, drawing) =>
        {
            DrawADimension(canvas);
            canvas.Undo();

            // An undo that took the dimension back and left its layer behind
            // would be telling the truth about the wrong half.
            return (drawing.Layers.Count, drawing.Entities.Count);
        });

        Assert.Equal(1, layers);
        Assert.Equal(0, entities);
    }

    [Fact]
    public void TheSecondDimensionReusesTheLayerTheFirstOneMade()
    {
        var (layers, sameLayer) = OnCanvas((canvas, drawing) =>
        {
            DrawADimension(canvas);

            canvas.PlaceToolPoint(new Vec2(0, 40));
            canvas.PlaceToolPoint(new Vec2(80, 40));
            canvas.PlaceToolPoint(new Vec2(40, 60));

            Assert.Equal(2, drawing.Entities.Count);
            return (drawing.Layers.Count,
                    drawing.Entities[0].LayerIndex == drawing.Entities[1].LayerIndex);
        });

        Assert.Equal(2, layers);
        Assert.True(sameLayer);
    }

    [Fact]
    public void ADimensionTakesItsSizesFromTheDocumentsUnits()
    {
        double height = OnCanvas((canvas, drawing) =>
        {
            drawing.Units = DrawingUnits.Metres;

            // Picked after the units changed, so the tool is built with them.
            canvas.UseTool(new LinearDimensionTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(2, 0));
            canvas.PlaceToolPoint(new Vec2(1, -0.5));

            return ((SDimension)drawing.Entities[0]).DimensionStyle.TextHeight;
        });

        // ISO's 2.5 mm of text, in metres.
        Assert.Equal(0.0025, height, 12);
    }

    [Fact]
    public void ChangingTheUnitsUnderAToolChangesTheSizesItWillDrawWith()
    {
        double height = OnCanvas((canvas, drawing) =>
        {
            canvas.UseTool(new LinearDimensionTool());

            // The other way round: the tool was in hand first. Two copies of
            // a setting drift, and the copy that is forgotten is the one that
            // silently does nothing.
            drawing.Units = DrawingUnits.Metres;
            canvas.RefreshToolSettings();

            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(2, 0));
            canvas.PlaceToolPoint(new Vec2(1, -0.5));

            return ((SDimension)drawing.Entities[0]).DimensionStyle.TextHeight;
        });

        Assert.Equal(0.0025, height, 12);
    }

    [Fact]
    public void ADimensionCanBeSelectedByItsLineAndStretchedByAGrip()
    {
        var (picked, measurement) = OnCanvas((canvas, drawing) =>
        {
            DrawADimension(canvas);

            canvas.UseSelect();
            canvas.PickAt(new Vec2(40, -20));

            bool hit = canvas.Selection.Count == 1;

            // The second origin, dragged out: a dimension re-measures when it
            // is stretched, which is the whole reason it is an entity rather
            // than a picture of one.
            Assert.True(canvas.BeginGripDrag(new Vec2(80, 0)));
            Assert.True(canvas.EndGripDrag(new Vec2(120, 0)));

            return (hit, ((SDimension)drawing.Entities[0]).MeasurementText);
        });

        Assert.True(picked);
        Assert.Equal("120", measurement);
    }

    // ---- the layer panel's side of it --------------------------------------

    [Fact]
    public void ALayerAddedThroughTheCanvasIsUndoable()
    {
        var (count, name, afterUndo) = OnCanvas((canvas, drawing) =>
        {
            var layer = canvas.AddLayer(new LayerProperties("Walls", new Rgb(255, 0, 0),
                Lineweight.Default, null, true, false, false));

            Assert.NotNull(layer);

            int made = drawing.Layers.Count;
            canvas.Undo();

            return (made, layer!.Name, drawing.Layers.Count);
        });

        Assert.Equal(2, count);
        Assert.Equal("Walls", name);
        Assert.Equal(1, afterUndo);
    }

    [Fact]
    public void TwoLayersCannotEndUpWithTheSameName()
    {
        string second = OnCanvas((canvas, drawing) =>
        {
            canvas.AddLayer(new LayerProperties("Walls", Rgb.White, Lineweight.Default, null, true, false, false));
            var again = canvas.AddLayer(new LayerProperties("Walls", Rgb.White, Lineweight.Default, null, true, false, false));

            return again!.Name;
        });

        Assert.Equal("Walls 1", second);
    }

    [Fact]
    public void TheLayerADimensionIsOnCannotBeDeletedWhileTheDimensionIsThere()
    {
        var (why, gone, afterErase) = OnCanvas((canvas, drawing) =>
        {
            DrawADimension(canvas);

            string? refused = canvas.WhyLayerCannotBeDeleted(1);
            bool deleted = canvas.DeleteLayer(1);

            // Erase the dimension and the layer is free to go.
            canvas.UseSelect();
            canvas.PickAt(new Vec2(40, -20));
            canvas.EraseSelection();

            return (refused, deleted, canvas.DeleteLayer(1));
        });

        Assert.NotNull(why);
        Assert.False(gone);
        Assert.True(afterErase);
    }

    [Fact]
    public void LayerZeroIsRefusedWithAReason()
    {
        string? why = OnCanvas((canvas, _) => canvas.WhyLayerCannotBeDeleted(0));

        Assert.NotNull(why);
        Assert.Contains("Layer 0", why);
    }

    [Fact]
    public void RecolouringALayerThroughTheCanvasRecoloursWhatWasOnIt()
    {
        var (colour, afterUndo) = OnCanvas((canvas, drawing) =>
        {
            canvas.UseTool(new LineTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(100, 0));

            var was = drawing.Entities[0].Style.Color;

            canvas.EditLayer(0, LayerProperties.Of(drawing.Layers[0]) with { Color = new Rgb(255, 0, 0) });
            var now = drawing.Entities[0].Style.Color;

            canvas.Undo();

            Assert.NotEqual(was, now);
            return (now, drawing.Entities[0].Style.Color == was);
        });

        Assert.Equal(new Rgb(255, 0, 0), colour);
        Assert.True(afterUndo, "undo puts the geometry back in the colour it was");
    }

    // ---- fillet and chamfer on what the rectangle tool draws ---------------

    /// <summary>Draws a 100 by 60 rectangle and returns it.</summary>
    private static SPolyline DrawARectangle(CadCanvas canvas, SceneDrawing drawing)
    {
        canvas.UseTool(new RectangleTool());
        canvas.PlaceToolPoint(new Vec2(0, 0));
        canvas.PlaceToolPoint(new Vec2(100, 60));

        return (SPolyline)drawing.Entities[0];
    }

    [Fact]
    public void AFilletRoundsTheCornerOfADrawnRectangle()
    {
        var (vertices, bulge, undoName) = OnCanvas((canvas, drawing) =>
        {
            DrawARectangle(canvas, drawing);

            canvas.FilletRadius = 10;
            canvas.UseTool(new FilletTool());

            // The bottom edge and the right edge. This did nothing at all
            // until a polyline corner could be edited in place.
            canvas.PickEntityForTool(new Vec2(50, 0));
            canvas.PickEntityForTool(new Vec2(100, 30));

            var result = (SPolyline)drawing.Entities[0];
            return (result.Vertices.Length, result.Vertices[1].Bulge, canvas.Commands!.UndoName);
        });

        Assert.Equal(5, vertices);
        Assert.Equal(Math.Tan(Math.PI / 8), bulge, 9);
        Assert.Equal("Fillet", undoName);
    }

    [Fact]
    public void AChamferCutsTheCornerOfADrawnRectangle()
    {
        var (vertices, bulge) = OnCanvas((canvas, drawing) =>
        {
            DrawARectangle(canvas, drawing);

            canvas.ChamferDistance = 15;
            canvas.UseTool(new ChamferTool());

            canvas.PickEntityForTool(new Vec2(50, 0));
            canvas.PickEntityForTool(new Vec2(100, 30));

            var result = (SPolyline)drawing.Entities[0];
            return (result.Vertices.Length, result.Vertices[1].Bulge);
        });

        Assert.Equal(5, vertices);
        Assert.Equal(0, bulge);
    }

    [Fact]
    public void TheFilletRadiusAndTheChamferDistanceAreSeparateNumbers()
    {
        var (radius, distance) = OnCanvas((canvas, _) =>
        {
            canvas.FilletRadius = 4;
            canvas.ChamferDistance = 9;

            // One number for both meant setting a chamfer quietly changed
            // every fillet after it.
            return (canvas.FilletRadius, canvas.ChamferDistance);
        });

        Assert.Equal(4, radius);
        Assert.Equal(9, distance);
    }

    [Fact]
    public void EachCornerToolIsGivenItsOwnNumber()
    {
        var (fillet, chamfer) = OnCanvas((canvas, _) =>
        {
            canvas.FilletRadius = 4;
            canvas.ChamferDistance = 9;

            canvas.UseTool(new FilletTool());
            double toFillet = ((FilletTool)canvas.Tool!).Radius;

            canvas.UseTool(new ChamferTool());
            double toChamfer = ((FilletTool)canvas.Tool!).Radius;

            return (toFillet, toChamfer);
        });

        Assert.Equal(4, fillet);
        Assert.Equal(9, chamfer);
    }

    // ---- the box beside the cursor -----------------------------------------

    [Fact]
    public void TheCursorBoxEditsWhateverTheToolInHandIsWorkingTo()
    {
        var (idle, fillet, chamfer, drawing) = OnCanvas((canvas, _) =>
        {
            canvas.FilletRadius = 4;
            canvas.ChamferDistance = 9;

            var nothing = canvas.Entry;

            canvas.UseTool(new FilletTool());
            var rounding = (canvas.Entry, canvas.EntryValue);

            canvas.UseTool(new ChamferTool());
            var cutting = (canvas.Entry, canvas.EntryValue);

            canvas.UseTool(new LineTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.DragGripTo(new Vec2(30, 0));

            return (nothing, rounding, cutting, canvas.Entry);
        });

        Assert.Equal(CursorEntry.None, idle);
        Assert.Equal((CursorEntry.Radius, (double?)4), fillet);
        Assert.Equal((CursorEntry.Distance, (double?)9), chamfer);

        // A point being placed beats a tool setting: it is the thing the
        // next click is about to do.
        Assert.Equal(CursorEntry.Length, drawing);
    }

    [Fact]
    public void ANumberTypedIntoTheBoxGoesWhereTheToolNeedsIt()
    {
        var (radius, distance) = OnCanvas((canvas, _) =>
        {
            canvas.UseTool(new FilletTool());
            Assert.True(canvas.ApplyEntry(7.5));

            canvas.UseTool(new ChamferTool());
            Assert.True(canvas.ApplyEntry(3));

            return (canvas.FilletRadius, canvas.ChamferDistance);
        });

        Assert.Equal(7.5, radius);
        Assert.Equal(3, distance);
    }

    [Fact]
    public void ZeroIsAGoodAnswerForACornerAndNotForALength()
    {
        bool corner = OnCanvas((canvas, _) =>
        {
            canvas.FilletRadius = 5;
            canvas.UseTool(new FilletTool());

            // A sharp corner is what a fillet of zero means, and it is one
            // of the most used things in CAD.
            return canvas.ApplyEntry(0) && canvas.FilletRadius == 0;
        });

        Assert.True(corner);
    }

    // ---- radius and diameter dimensions ------------------------------------

    [Fact]
    public void ARadiusDimensionTakesACircleAndThenAPoint()
    {
        var (count, text, layer) = OnCanvas((canvas, drawing) =>
        {
            canvas.UseTool(new CircleTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(25, 0));

            canvas.UseTool(new RadiusDimensionTool());

            // The first click points at the circle, the second places the
            // number. The canvas asks the tool which kind each one is.
            canvas.PickEntityForTool(new Vec2(25, 0));
            Assert.Empty(drawing.Entities.OfType<SDimension>());

            canvas.PlaceToolPoint(new Vec2(40, 40));

            var dimension = drawing.Entities.OfType<SDimension>().Single();
            return (drawing.Entities.Count, dimension.MeasurementText,
                    drawing.Layers[dimension.LayerIndex].Name);
        });

        Assert.Equal(2, count);
        Assert.Equal("R25", text);
        Assert.Equal("Dimensions", layer);
    }

    [Fact]
    public void ADiameterDimensionMeasuresTwiceTheRadius()
    {
        string text = OnCanvas((canvas, drawing) =>
        {
            canvas.UseTool(new CircleTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(25, 0));

            canvas.UseTool(new DiameterDimensionTool());
            canvas.PickEntityForTool(new Vec2(25, 0));
            canvas.PlaceToolPoint(new Vec2(40, 40));

            return drawing.Entities.OfType<SDimension>().Single().MeasurementText;
        });

        Assert.Equal("\u00d850", text);
    }

    [Fact]
    public void ClickingSomethingWithoutARadiusLeavesTheToolWaiting()
    {
        int dimensions = OnCanvas((canvas, drawing) =>
        {
            canvas.UseTool(new LineTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(100, 0));

            canvas.UseTool(new RadiusDimensionTool());
            canvas.PickEntityForTool(new Vec2(50, 0));

            // Still asking for a circle, so the next click is another pick
            // rather than a point that would place a dimension on nothing.
            Assert.True(canvas.Tool!.WantsEntity);
            canvas.PickEntityForTool(new Vec2(50, 0));

            return drawing.Entities.OfType<SDimension>().Count();
        });

        Assert.Equal(0, dimensions);
    }
}
