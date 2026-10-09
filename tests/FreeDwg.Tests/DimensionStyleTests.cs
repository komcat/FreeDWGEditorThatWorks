using System.Windows;
using FreeDwg.Core.Commands;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Snapping;
using FreeDwg.Core.Tools;
using FreeDwg.Tests.Rendering;
using FreeDWGEditorThatWorks.Controls;
using FreeDWGEditorThatWorks.ViewModels;
using SceneDrawing = FreeDwg.Core.Scene.Drawing;

namespace FreeDwg.Tests;

/// <summary>
/// The sizes dimensions are drawn with: the drawing's settings for new ones,
/// and the sizes of ones already drawn.
/// </summary>
/// <remarks>
/// The trap here is a setting that silently does nothing: a size changed in
/// the dialog that the dimension tool already in hand never hears about, or
/// an undo that puts the drawing's settings back and leaves the tool on the
/// ones it took away. Both are tested, because both are the kind of bug that
/// looks like the dialog is broken.
/// </remarks>
public sealed class DimensionStyleTests
{
    private static readonly DimensionSettings Big =
        new(new DimensionStyle(5, 4, 1, 2, 1, DrawingUnits.Millimetres, 1), Scale: 10);

    private static T OnCanvas<T>(Func<CadCanvas, SceneDrawing, T> body) => StaRenderer.OnSta(() =>
    {
        var canvas = new CadCanvas { Width = 800, Height = 600 };
        canvas.Measure(new Size(800, 600));
        canvas.Arrange(new Rect(0, 0, 800, 600));

        var drawing = SceneDrawing.CreateEmpty();
        canvas.Drawing = drawing;
        canvas.Snapping.Modes = SnapModes.None;
        canvas.UpdateLayout();

        return body(canvas, drawing);
    });

    private static SDimension Dimension(CadCanvas canvas, SceneDrawing drawing, double y)
    {
        canvas.UseTool(new LinearDimensionTool());
        canvas.PlaceToolPoint(new Vec2(0, y));
        canvas.PlaceToolPoint(new Vec2(80, y));
        canvas.PlaceToolPoint(new Vec2(40, y - 20));

        return drawing.Entities.OfType<SDimension>().Last();
    }

    // ---- the arithmetic ----------------------------------------------------

    [Fact]
    public void ADrawingWithNoSettingsDrawsIsoInItsOwnUnits()
    {
        var drawing = SceneDrawing.CreateEmpty();
        drawing.Units = DrawingUnits.Metres;

        // 2.5 millimetres of text, in a drawing that counts metres.
        Assert.Equal(0.0025, DimensionStyle.For(drawing).TextHeight, 12);
    }

    [Fact]
    public void TheOverallScaleMultipliesEverySizeButNotTheDecimals()
    {
        var drawing = SceneDrawing.CreateEmpty();
        drawing.Dimensions = Big;

        var style = DimensionStyle.For(drawing);

        Assert.Equal(50, style.TextHeight, 9);
        Assert.Equal(40, style.ArrowSize, 9);
        Assert.Equal(10, style.ExtensionOffset, 9);
        Assert.Equal(20, style.ExtensionBeyond, 9);
        Assert.Equal(10, style.TextGap, 9);
        Assert.Equal(1, style.Decimals);
    }

    [Theory]
    [InlineData(0, 2.5, 1)]
    [InlineData(2.5, 0, 1)]
    [InlineData(2.5, 2.5, 0)]
    public void SizesThatCannotBeSeenAreNotSettings(double text, double arrow, double scale)
    {
        var settings = new DimensionSettings(DimensionStyle.Iso with { TextHeight = text, ArrowSize = arrow }, scale);
        Assert.False(settings.IsValid);
    }

    // ---- the command -------------------------------------------------------

    [Fact]
    public void ChangingTheSettingsIsUndoneAndMarksTheDrawingModified()
    {
        var drawing = SceneDrawing.CreateEmpty();
        var stack = new CommandStack(drawing);

        stack.Do(ChangeDimensionSettings.Including(drawing, Big, restyleExisting: false));
        Assert.Equal(Big, drawing.Dimensions);
        Assert.True(stack.IsModified);

        stack.Undo();
        Assert.Null(drawing.Dimensions);
    }

    [Fact]
    public void ExistingDimensionsAreResizedOnlyWhenAskedAndInOneStep()
    {
        var drawing = SceneDrawing.CreateEmpty();
        var stack = new CommandStack(drawing);
        var a = new SDimension(DimensionKind.Aligned, new Vec2(0, 0), new Vec2(50, 0), new Vec2(25, 10));
        var b = new SDimension(DimensionKind.Aligned, new Vec2(0, 30), new Vec2(50, 30), new Vec2(25, 40));
        stack.Do(new AddEntities(drawing.ActiveLayout, a, b));

        stack.Do(ChangeDimensionSettings.Including(drawing, Big, restyleExisting: false));
        Assert.All(drawing.Entities.OfType<SDimension>(), d => Assert.Equal(DimensionStyle.Iso, d.DimensionStyle));

        stack.Do(ChangeDimensionSettings.Including(drawing, Big, restyleExisting: true));
        Assert.All(drawing.Entities.OfType<SDimension>(), d => Assert.Equal(50, d.DimensionStyle.TextHeight, 9));

        // The resized ones are replacements that keep their places, and one
        // undo takes back the settings and the resizing together.
        Assert.Equal(2, drawing.Entities.Count);
        stack.Undo();
        Assert.Same(a, drawing.Entities[0]);
        Assert.Same(b, drawing.Entities[1]);
        Assert.Equal(Big, drawing.Dimensions);
    }

    // ---- the canvas --------------------------------------------------------

    [Fact]
    public void TheDimensionToolInHandHearsAboutTheChangeAndTheUndo()
    {
        var (during, afterUndo) = OnCanvas((canvas, drawing) =>
        {
            // The tool is picked up first, then the size changed under it.
            canvas.UseTool(new LinearDimensionTool());
            canvas.SetDimensionSettings(Big, restyleExisting: false);
            var during = Dimension(canvas, drawing, 0).DimensionStyle.TextHeight;

            canvas.Undo();   // the dimension
            canvas.Undo();   // the settings
            canvas.UseTool(new LinearDimensionTool());
            var after = Dimension(canvas, drawing, 50).DimensionStyle.TextHeight;

            return (during, after);
        });

        Assert.Equal(50, during, 9);
        Assert.Equal(DimensionStyle.Iso.TextHeight, afterUndo, 9);
    }

    [Fact]
    public void ASelectedDimensionsSizesAreRowsInThePropertiesPanel()
    {
        var (shown, resized, undone) = OnCanvas((canvas, drawing) =>
        {
            var dimension = Dimension(canvas, drawing, 0);
            canvas.UseSelect();
            canvas.Selection.Set([dimension]);

            var rows = PropertySource.Build(canvas);
            string shown = rows.Single(row => row.Name == "Text height").Value;

            rows.Single(row => row.Name == "Arrow size").Value = "7";
            var resized = drawing.Entities.OfType<SDimension>().Single().DimensionStyle.ArrowSize;

            canvas.Undo();
            var undone = drawing.Entities.OfType<SDimension>().Single().DimensionStyle.ArrowSize;

            return (shown, resized, undone);
        });

        Assert.Equal("2.5", shown);
        Assert.Equal(7, resized, 9);
        Assert.Equal(DimensionStyle.Iso.ArrowSize, undone, 9);
    }
}
