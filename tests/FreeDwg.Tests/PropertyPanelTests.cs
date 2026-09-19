using System.Windows;
using System.Windows.Media;
using FreeDwg.Core.Styling;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Snapping;
using FreeDwg.Core.Tools;
using FreeDwg.Tests.Rendering;
using FreeDWGEditorThatWorks.Controls;
using FreeDWGEditorThatWorks.ViewModels;
using SceneDrawing = FreeDwg.Core.Scene.Drawing;

namespace FreeDwg.Tests;

/// <summary>
/// The properties panel: what it offers, and what happens when a value in it
/// is edited.
/// </summary>
/// <remarks>
/// The panel writes to the drawing, so the thing worth pinning down is that
/// it writes the same way everything else does -- through the command stack,
/// undoably. A panel that edited entities directly would be invisible to the
/// writer and to undo, and would be the one hole in the rule.
/// </remarks>
public sealed class PropertyPanelTests
{
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

    private static PropertyRow Row(List<PropertyRow> rows, string name) =>
        rows.Single(row => row.Name == name);

    /// <summary>Draws a line and selects it, as clicking one would.</summary>
    private static SLine DrawAndSelect(CadCanvas canvas, SceneDrawing drawing)
    {
        canvas.UseTool(new LineTool());
        canvas.PlaceToolPoint(new Vec2(0, 0));
        canvas.PlaceToolPoint(new Vec2(100, 0));

        canvas.UseSelect();
        canvas.PickAt(new Vec2(50, 0));

        return (SLine)drawing.Entities[0];
    }

    // ---- what is offered ---------------------------------------------------

    [Fact]
    public void TheEditorSettingsAreThereWithNothingSelected()
    {
        var names = OnCanvas((canvas, _) =>
            PropertySource.Build(canvas).Select(row => row.Name).ToList());

        // These used to be loose controls on the toolbar; the panel is where
        // they live now, so they have to be in it whatever is selected.
        Assert.Contains("Fillet radius", names);
        Assert.Contains("Ortho", names);
        Assert.Contains("Grid", names);
        Assert.Contains("Grid spacing", names);
    }

    [Fact]
    public void ASelectedLineOffersItsEndsAndItsMeasurements()
    {
        var rows = OnCanvas((canvas, drawing) =>
        {
            DrawAndSelect(canvas, drawing);
            return PropertySource.Build(canvas);
        });

        Assert.Equal("Line", Row(rows, "Type").Value);
        Assert.Equal("0", Row(rows, "Start X").Value);
        Assert.Equal("100", Row(rows, "End X").Value);
        Assert.Equal("100", Row(rows, "Length").Value);

        // Derived figures are shown but cannot be typed into: a length is a
        // consequence of the two ends, not a third thing to set.
        Assert.True(Row(rows, "Length").IsReadOnly);
        Assert.False(Row(rows, "Start X").IsReadOnly);
    }

    [Fact]
    public void SeveralObjectsSelectedOffersACountRatherThanGeometry()
    {
        var rows = OnCanvas((canvas, drawing) =>
        {
            canvas.UseTool(new LineTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(100, 0));
            canvas.PlaceToolPoint(new Vec2(0, 10));
            canvas.PlaceToolPoint(new Vec2(100, 10));

            canvas.UseSelect();
            canvas.PickAt(new Vec2(50, 0));
            canvas.PickAt(new Vec2(50, 10), extend: true);

            return PropertySource.Build(canvas);
        });

        Assert.Equal("2 objects", Row(rows, "Selection").Value);
        Assert.DoesNotContain(rows, row => row.Name == "Start X");
    }

    // ---- editing settings --------------------------------------------------

    [Fact]
    public void TypingAFilletRadiusReachesTheCanvas()
    {
        double radius = OnCanvas((canvas, _) =>
        {
            Row(PropertySource.Build(canvas), "Fillet radius").Value = "12.5";
            return canvas.CornerRadius;
        });

        Assert.Equal(12.5, radius, 9);
    }

    [Fact]
    public void SwitchingTheGridOnAlsoSwitchesOnSnappingToIt()
    {
        var (visible, snaps) = OnCanvas((canvas, _) =>
        {
            Row(PropertySource.Build(canvas), "Grid").Value = "Yes";
            return (canvas.Snapping.Grid.IsVisible, canvas.Snapping.Modes.HasFlag(SnapModes.Grid));
        });

        // One switch doing both jobs is the point: a visible grid that did
        // not snap would be the question everyone asks.
        Assert.True(visible);
        Assert.True(snaps);
    }

    [Fact]
    public void ARejectedValueLeavesTheSettingAlone()
    {
        var (radius, shown) = OnCanvas((canvas, _) =>
        {
            canvas.CornerRadius = 5;

            var row = Row(PropertySource.Build(canvas), "Fillet radius");
            row.Value = "not a number";

            return (canvas.CornerRadius, row.Value);
        });

        // And the row keeps showing the value that is actually in force,
        // rather than the text that was refused.
        Assert.Equal(5, radius, 9);
        Assert.Equal("5", shown);
    }

    [Fact]
    public void ANegativeRadiusIsRefused()
    {
        double radius = OnCanvas((canvas, _) =>
        {
            Row(PropertySource.Build(canvas), "Fillet radius").Value = "-3";
            return canvas.CornerRadius;
        });

        Assert.Equal(0, radius, 9);
    }

    // ---- editing geometry --------------------------------------------------

    [Fact]
    public void MovingAnEndThroughThePanelGoesOnTheUndoStack()
    {
        var (moved, undone, canUndo) = OnCanvas((canvas, drawing) =>
        {
            DrawAndSelect(canvas, drawing);

            Row(PropertySource.Build(canvas), "End X").Value = "250";

            var after = ((SLine)drawing.Entities[0]).End;
            bool undoable = canvas.Commands!.CanUndo;

            canvas.Undo();
            return (after, ((SLine)drawing.Entities[0]).End, undoable);
        });

        Assert.Equal(new Vec2(250, 0), moved);
        Assert.True(canUndo);
        Assert.Equal(new Vec2(100, 0), undone);
    }

    [Fact]
    public void AnEditedEntityStaysSelectedSoThePanelKeepsShowingIt()
    {
        var (count, endX) = OnCanvas((canvas, drawing) =>
        {
            DrawAndSelect(canvas, drawing);
            Row(PropertySource.Build(canvas), "End X").Value = "250";

            // The edit swaps the entity for a copy, so the selection has to
            // follow it or the panel empties itself on every keystroke.
            var rows = PropertySource.Build(canvas);
            return (canvas.Selection.Count, Row(rows, "End X").Value);
        });

        Assert.Equal(1, count);
        Assert.Equal("250", endX);
    }

    [Fact]
    public void AnEditedEntityKeepsTheHandleTheFileKnowsIt()
    {
        ulong handle = OnCanvas((canvas, drawing) =>
        {
            var line = DrawAndSelect(canvas, drawing);
            line.SourceHandle = 0x2A;

            Row(PropertySource.Build(canvas), "End X").Value = "250";

            return drawing.Entities[0].SourceHandle;
        });

        // A save has to rewrite the original object rather than delete it and
        // invent another one in its place.
        Assert.Equal(0x2AUL, handle);
    }

    [Fact]
    public void AZeroRadiusCircleIsRefused()
    {
        double radius = OnCanvas((canvas, drawing) =>
        {
            canvas.UseTool(new CircleTool());
            canvas.PlaceToolPoint(new Vec2(50, 50));
            canvas.PlaceToolPoint(new Vec2(70, 50));

            canvas.UseSelect();
            canvas.PickAt(new Vec2(70, 50));

            Row(PropertySource.Build(canvas), "Radius").Value = "0";

            return ((SCircle)drawing.Entities[0]).Radius;
        });

        Assert.Equal(20, radius, 9);
    }

    // ---- colour, lineweight and the layer list ---------------------------

    [Fact]
    public void TheFieldsWithAFixedSetOfAnswersOfferThem()
    {
        var rows = OnCanvas((canvas, drawing) =>
        {
            drawing.AddLayer(new FreeDwg.Core.Scene.Layer("WALLS"));
            DrawAndSelect(canvas, drawing);
            return PropertySource.Build(canvas);
        });

        // A field with a fixed set of answers should not have to be spelled
        // from memory; misspelling one is how an edit gets quietly refused.
        Assert.Equal<object>(["0", "WALLS"], Row(rows, "Layer").Choices!);
        Assert.True(Row(rows, "Colour").HasChoices);
        Assert.True(Row(rows, "Lineweight").HasChoices);

        // And a length is still typed, because no list could hold one.
        Assert.False(Row(rows, "Start X").HasChoices);
    }

    [Fact]
    public void PickingAColourByNameSetsIt()
    {
        var colour = OnCanvas((canvas, drawing) =>
        {
            DrawAndSelect(canvas, drawing);
            Row(PropertySource.Build(canvas), "Colour").Value = "Red";

            return drawing.Entities[0].Style.Color;
        });

        Assert.Equal(new Rgb(255, 0, 0), colour);
    }

    [Fact]
    public void AColourOutsideTheListIsShownAsHexAndReadBack()
    {
        var (shown, colour) = OnCanvas((canvas, drawing) =>
        {
            DrawAndSelect(canvas, drawing);
            Row(PropertySource.Build(canvas), "Colour").Value = "#3C78D8";

            // A drawing full of hand-mixed colours has to show the colour it
            // has rather than snap to the nearest name in the list.
            var rows = PropertySource.Build(canvas);
            return (Row(rows, "Colour").Value, drawing.Entities[0].Style.Color);
        });

        Assert.Equal(new Rgb(0x3C, 0x78, 0xD8), colour);
        Assert.Equal("#3C78D8", shown);
    }

    [Fact]
    public void AColourRowCarriesAChipOfTheColourItShows()
    {
        var row = OnCanvas((canvas, drawing) =>
        {
            DrawAndSelect(canvas, drawing);
            Row(PropertySource.Build(canvas), "Colour").Value = "Green";

            return Row(PropertySource.Build(canvas), "Colour");
        });

        Assert.True(row.HasSwatch);
        Assert.Equal(Colors.Lime, ((SolidColorBrush)row.Swatch!).Color);
    }

    [Fact]
    public void ByLayerCopiesTheLayersColour()
    {
        var colour = OnCanvas((canvas, drawing) =>
        {
            drawing.AddLayer(new FreeDwg.Core.Scene.Layer("WALLS") { Color = new Rgb(12, 34, 56) });

            DrawAndSelect(canvas, drawing);
            Row(PropertySource.Build(canvas), "Layer").Value = "WALLS";
            Row(PropertySource.Build(canvas), "Colour").Value = "By layer";

            return drawing.Entities[0].Style.Color;
        });

        // A copy, not a link: ByLayer is resolved when a file is read, so
        // there is nowhere in the scene to record "keep following the layer".
        Assert.Equal(new Rgb(12, 34, 56), colour);
    }

    [Fact]
    public void ANonsenseColourIsRefused()
    {
        var colour = OnCanvas((canvas, drawing) =>
        {
            var line = DrawAndSelect(canvas, drawing);
            var before = line.Style.Color;

            Row(PropertySource.Build(canvas), "Colour").Value = "burnt sienna";

            Assert.Equal(before, drawing.Entities[0].Style.Color);
            return drawing.Entities[0].Style.Color;
        });

        Assert.Equal(Rgb.White, colour);
    }

    [Theory]
    [InlineData("0.50 mm", 50)]
    [InlineData("0.25 mm", 25)]
    [InlineData("2.11 mm", 211)]
    [InlineData("Thinnest", 0)]
    public void PickingALineweightSetsIt(string chosen, short hundredths)
    {
        var weight = OnCanvas((canvas, drawing) =>
        {
            DrawAndSelect(canvas, drawing);
            Row(PropertySource.Build(canvas), "Lineweight").Value = chosen;

            return drawing.Entities[0].Style.Lineweight;
        });

        Assert.Equal(hundredths, weight.Hundredths);
    }

    [Fact]
    public void EveryOfferedLineweightIsOneThatCanBeChosen()
    {
        var rows = OnCanvas((canvas, drawing) =>
        {
            DrawAndSelect(canvas, drawing);
            return PropertySource.Build(canvas);
        });

        // A list holding an answer the parser then refuses would be a trap,
        // so every entry has to survive the round trip it will be put through.
        foreach (string choice in Row(rows, "Lineweight").Choices!)
        {
            Assert.True(StyleChoices.TryWeight(choice, out var weight), $"'{choice}' was offered but not accepted");
            Assert.Equal(choice, StyleChoices.WeightName(weight));
        }
    }

    [Fact]
    public void EveryOfferedColourIsOneThatCanBeChosen()
    {
        var rows = OnCanvas((canvas, drawing) =>
        {
            DrawAndSelect(canvas, drawing);
            return PropertySource.Build(canvas);
        });

        foreach (string choice in Row(rows, "Colour").Choices!)
        {
            if (choice == StyleChoices.ByLayer) continue;   // handled on its own

            Assert.True(StyleChoices.TryColour(choice, out var colour), $"'{choice}' was offered but not accepted");
            Assert.Equal(choice, StyleChoices.ColourName(colour));
        }
    }

    [Fact]
    public void ChangingAColourIsUndoable()
    {
        var (changed, undone) = OnCanvas((canvas, drawing) =>
        {
            DrawAndSelect(canvas, drawing);
            Row(PropertySource.Build(canvas), "Colour").Value = "Blue";

            var after = drawing.Entities[0].Style.Color;
            canvas.Undo();

            return (after, drawing.Entities[0].Style.Color);
        });

        Assert.Equal(new Rgb(0, 0, 255), changed);
        Assert.Equal(Rgb.White, undone);
    }

    [Fact]
    public void MovingAnObjectToAnotherLayerByName()
    {
        var (index, colour) = OnCanvas((canvas, drawing) =>
        {
            drawing.AddLayer(new FreeDwg.Core.Scene.Layer("WALLS")
            {
                Color = new FreeDwg.Core.Styling.Rgb(255, 0, 0),
            });

            DrawAndSelect(canvas, drawing);
            Row(PropertySource.Build(canvas), "Layer").Value = "walls";

            var entity = drawing.Entities[0];
            return (entity.LayerIndex, entity.Style.Color);
        });

        // Matched without regard to case, and the style follows the layer,
        // because styles are resolved rather than looked up at draw time.
        Assert.Equal(1, index);
        Assert.Equal(new FreeDwg.Core.Styling.Rgb(255, 0, 0), colour);
    }

    [Fact]
    public void AnUnknownLayerNameIsRefused()
    {
        int index = OnCanvas((canvas, drawing) =>
        {
            DrawAndSelect(canvas, drawing);
            Row(PropertySource.Build(canvas), "Layer").Value = "no such layer";

            return drawing.Entities[0].LayerIndex;
        });

        Assert.Equal(0, index);
    }
}
