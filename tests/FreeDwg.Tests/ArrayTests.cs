using System.IO;
using System.Windows;
using FreeDwg.Core.Commands;
using FreeDwg.Core.Editing;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Snapping;
using FreeDwg.Interop.Acad;
using FreeDwg.Tests.Rendering;
using FreeDWGEditorThatWorks.Controls;
using SceneDrawing = FreeDwg.Core.Scene.Drawing;

namespace FreeDwg.Tests;

/// <summary>
/// Arrays: where the copies go, that they are one step on the undo stack,
/// and that a polar array's centre can be picked off the drawing.
/// </summary>
/// <remarks>
/// The polar spacing is the case worth reading. A whole turn spaces n items
/// by 360/n, because the last would otherwise land on the first; anything
/// less spaces them by fill/(n-1), so the last sits exactly at the end of the
/// fill. Using one rule for both gets one of them wrong by an item.
/// </remarks>
public sealed class ArrayTests
{
    private static readonly Bounds2 Unit = Bounds2.FromCorners(new Vec2(0, 0), new Vec2(1, 1));

    // ---- where the copies go -----------------------------------------------

    [Fact]
    public void ARectangularArrayIsEveryCellButTheOriginal()
    {
        var copies = new RectangularArray(3, 2, 10, 20).Copies(Unit);

        Assert.Equal(5, copies.Count);
        var offsets = copies.Select(m => m.Transform(Vec2.Zero)).ToHashSet();
        Assert.Equal(new HashSet<Vec2> { new(10, 0), new(20, 0), new(0, 20), new(10, 20), new(20, 20) }, offsets);
    }

    [Fact]
    public void NegativeSpacingRunsLeftAndDown_AndTheAngleTurnsTheGrid()
    {
        var left = new RectangularArray(2, 1, -10, 0).Copies(Unit).Single();
        Assert.Equal(new Vec2(-10, 0), left.Transform(Vec2.Zero));

        var turned = new RectangularArray(2, 1, 10, 0, Math.PI / 2).Copies(Unit).Single();
        var at = turned.Transform(Vec2.Zero);
        Assert.Equal(0, at.X, 9);
        Assert.Equal(10, at.Y, 9);
    }

    [Fact]
    public void AFullTurnSpacesItemsEvenlyWithNoneOnTheOriginal()
    {
        var array = new PolarArray(Vec2.Zero, 4, ArcMath.TwoPi);
        var copies = array.Copies(Unit);

        Assert.Equal(3, copies.Count);
        Assert.Equal(Math.PI / 2, array.Step, 12);

        var last = copies[^1].Transform(new Vec2(10, 0));
        Assert.Equal(0, last.X, 9);
        Assert.Equal(-10, last.Y, 9);
    }

    [Fact]
    public void APartFillPutsTheLastItemAtTheEndOfIt()
    {
        var copies = new PolarArray(Vec2.Zero, 3, Math.PI).Copies(Unit);

        var last = copies[^1].Transform(new Vec2(10, 0));
        Assert.Equal(-10, last.X, 9);
        Assert.Equal(0, last.Y, 9);
    }

    [Fact]
    public void ANegativeFillGoesClockwise()
    {
        var first = new PolarArray(Vec2.Zero, 2, -Math.PI / 2).Copies(Unit).Single();
        var at = first.Transform(new Vec2(10, 0));

        Assert.Equal(0, at.X, 9);
        Assert.Equal(-10, at.Y, 9);
    }

    [Fact]
    public void ItemsThatDoNotRotateOnlyMove()
    {
        var copy = new PolarArray(new Vec2(0, -10), 4, ArcMath.TwoPi, RotateItems: false).Copies(Unit)[0];

        // A translation: directions come through unchanged.
        Assert.Equal(new Vec2(1, 0), copy.TransformVector(new Vec2(1, 0)));
        Assert.Equal(new Vec2(0, 1), copy.TransformVector(new Vec2(0, 1)));

        // And the selection's centre has gone a quarter of the way round.
        var moved = copy.Transform(Unit.Center);
        Assert.Equal(-10.5, moved.X, 9);
        Assert.Equal(-9.5, moved.Y, 9);
    }

    [Theory]
    [InlineData(1, 1, 10.0, 10.0)]      // just the original
    [InlineData(2, 1, 0.0, 10.0)]       // every copy on top of the last
    [InlineData(200, 200, 10.0, 10.0)]  // forty thousand
    public void ArraysThatAreMistakesAreRefused(int columns, int rows, double columnSpacing, double rowSpacing)
    {
        Assert.False(new RectangularArray(columns, rows, columnSpacing, rowSpacing).IsValid);
    }

    // ---- the command -------------------------------------------------------

    [Fact]
    public void AnArrayIsOneStepAndItsCopiesAreNewObjects()
    {
        var drawing = SceneDrawing.CreateEmpty();
        var stack = new CommandStack(drawing);
        var line = new SLine(new Vec2(0, 0), new Vec2(5, 0)) { SourceHandle = 0x2A };
        drawing.Add(line);

        var copies = new RectangularArray(3, 3, 10, 10).Copies(line.Bounds);
        stack.Do(new ArrayEntities(drawing.ActiveLayout, [line], copies, "Array 3 x 3"));

        Assert.Equal(9, drawing.Entities.Count);
        Assert.All(drawing.Entities.Skip(1), copy =>
        {
            Assert.Equal(0ul, copy.SourceHandle);
            Assert.Equal(0x2Aul, copy.CopiedFrom);
        });

        stack.Undo();
        Assert.Same(line, Assert.Single(drawing.Entities));
    }

    // ---- the canvas --------------------------------------------------------

    private static T OnCanvas<T>(Func<CadCanvas, SceneDrawing, T> body) => StaRenderer.OnSta(() =>
    {
        var canvas = new CadCanvas { Width = 800, Height = 600 };
        canvas.Measure(new Size(800, 600));
        canvas.Arrange(new Rect(0, 0, 800, 600));

        var drawing = SceneDrawing.CreateEmpty();
        canvas.Drawing = drawing;
        canvas.Snapping.Modes = SnapModes.None;
        return body(canvas, drawing);
    });

    [Fact]
    public void ArrayingTheSelectionLeavesTheWholeArraySelected()
    {
        var (entities, selected, afterUndo) = OnCanvas((canvas, drawing) =>
        {
            var circle = new SCircle(Vec2.Zero, 2);
            canvas.Commands!.Do(new AddEntities(drawing.ActiveLayout, circle));
            canvas.Selection.Set([circle]);

            canvas.ArraySelection(new PolarArray(new Vec2(0, -20), 6, ArcMath.TwoPi));
            int entities = drawing.Entities.Count, selected = canvas.Selection.Count;

            canvas.Undo();
            return (entities, selected, drawing.Entities.Count);
        });

        Assert.Equal(6, entities);
        Assert.Equal(6, selected);
        Assert.Equal(1, afterUndo);
    }

    [Fact]
    public void APickedPointComesBackSnappedAndTheSelectionStays()
    {
        var (point, mode, kept, abandoned) = OnCanvas((canvas, drawing) =>
        {
            var line = new SLine(new Vec2(0, 0), new Vec2(40, 0));
            canvas.Commands!.Do(new AddEntities(drawing.ActiveLayout, line));
            canvas.Selection.Set([line]);
            canvas.Snapping.Modes = SnapModes.Endpoint;

            // Near the end of the line: the endpoint snap has the last word.
            var pick = canvas.PickPointAsync("centre");
            canvas.SupplyPoint(new Vec2(39.9, 0.05));

            // A second request, abandoned with Escape's route back to select.
            var abandoned = canvas.PickPointAsync("centre");
            canvas.UseSelect();

            return (pick.Result, canvas.Mode, canvas.Selection.Count, abandoned.Result);
        });

        Assert.Equal(new Vec2(40, 0), point);
        Assert.Equal(CanvasMode.Select, mode);
        Assert.Equal(1, kept);
        Assert.Null(abandoned);
    }

    // ---- saved -------------------------------------------------------------

    [Fact]
    public void ASavedArrayReadsBackAsEveryCopy()
    {
        var session = DwgSession.CreateNew();
        var drawing = session.Drawing;
        var stack = new CommandStack(drawing);

        var line = drawing.Place(new SLine(new Vec2(0, 0), new Vec2(5, 5)));
        stack.Do(new AddEntities(drawing.ActiveLayout, line));
        stack.Do(new ArrayEntities(drawing.ActiveLayout, [line],
            new RectangularArray(4, 3, 10, 10).Copies(line.Bounds), "Array 4 x 3"));

        string file = Path.Combine(Path.GetTempPath(), $"freedwg-array-{Guid.NewGuid():N}.dwg");
        try
        {
            session.Save(stack, file);
            var back = DwgLoader.Load(file);

            Assert.Equal(12, back.ModelSpace.Entities.OfType<SLine>().Count());
            var bounds = back.ModelSpace.Bounds;
            Assert.Equal(0, bounds.MinX, 9);
            Assert.Equal(0, bounds.MinY, 9);
            Assert.Equal(35, bounds.MaxX, 9);
            Assert.Equal(25, bounds.MaxY, 9);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
