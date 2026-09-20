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

    // ---- the colour picker -------------------------------------------------

    [Fact]
    public void ThePaletteIsEvenAndHasNoRepeats()
    {
        var swatches = ColourPalette.Swatches;

        Assert.Equal(0, swatches.Count % ColourPalette.Columns);
        Assert.Equal(swatches.Count, swatches.Distinct().Count());
    }

    [Fact]
    public void ThePaletteHoldsTheColoursTheDropdownNames()
    {
        var swatches = ColourPalette.Swatches.ToHashSet();

        // The picker and the list have to agree, or picking red from one and
        // reading it back in the other would show a hex code.
        Assert.Contains(new Rgb(255, 0, 0), swatches);
        Assert.Contains(new Rgb(0, 255, 0), swatches);
        Assert.Contains(new Rgb(0, 0, 255), swatches);
        Assert.Contains(new Rgb(0, 255, 255), swatches);
        Assert.Contains(new Rgb(255, 255, 0), swatches);
        Assert.Contains(new Rgb(255, 0, 255), swatches);

        Assert.Contains(Rgb.Black, swatches);
        Assert.Contains(Rgb.White, swatches);
        Assert.Contains(new Rgb(128, 128, 128), swatches);
    }

    [Fact]
    public void EveryPaletteColourSurvivesBeingNamedAndReadBack()
    {
        foreach (var colour in ColourPalette.Swatches)
        {
            string name = StyleChoices.ColourName(colour);

            Assert.True(StyleChoices.TryColour(name, out var again), $"'{name}' could not be read back");
            Assert.Equal(colour, again);
        }
    }

    [Theory]
    [InlineData(0.0, 255, 0, 0)]
    [InlineData(120.0, 0, 255, 0)]
    [InlineData(240.0, 0, 0, 255)]
    public void AFullySaturatedMidLightnessHueIsThePureColour(double hue, byte r, byte g, byte b)
    {
        Assert.Equal(new Rgb(r, g, b), ColourPalette.FromHsl(hue, 1.0, 0.5));
    }

    [Fact]
    public void NoSaturationIsGreyWhateverTheHue()
    {
        var grey = ColourPalette.FromHsl(200, 0.0, 0.5);

        Assert.Equal(grey.R, grey.G);
        Assert.Equal(grey.G, grey.B);
    }

    [Fact]
    public void ThePickerIsOfferedOnlyWhenThereIsOneToOpen()
    {
        var (without, with) = OnCanvas((canvas, _) =>
        {
            DrawAndSelectMany(canvas, 1);

            var plain = Row(PropertySource.Build(canvas), "Colour").Choices!;
            var offered = Row(PropertySource.Build(canvas, (_, _) => { }), "Colour").Choices!;

            return (plain.ToList(), offered.ToList());
        });

        Assert.DoesNotContain(PropertySource.MoreColours, without);
        Assert.Contains(PropertySource.MoreColours, with);
        Assert.Equal(PropertySource.MoreColours, with[^1]);
    }

    [Fact]
    public void ChoosingAColourFromThePickerAppliesIt()
    {
        var colour = OnCanvas((canvas, drawing) =>
        {
            DrawAndSelect(canvas, drawing);

            // Stands in for the dialog: answers at once with a fixed colour.
            void Picker(Rgb current, Action<Rgb> chosen) => chosen(new Rgb(18, 52, 86));

            Row(PropertySource.Build(canvas, Picker), "Colour").Value = PropertySource.MoreColours;

            return drawing.Entities[0].Style.Color;
        });

        Assert.Equal(new Rgb(18, 52, 86), colour);
    }

    [Fact]
    public void ThePickerIsHandedTheColourTheObjectAlreadyHas()
    {
        var offered = OnCanvas((canvas, drawing) =>
        {
            DrawAndSelect(canvas, drawing);
            Row(PropertySource.Build(canvas), "Colour").Value = "Green";

            var seen = Rgb.Black;
            void Picker(Rgb current, Action<Rgb> chosen) => seen = current;

            Row(PropertySource.Build(canvas, Picker), "Colour").Value = PropertySource.MoreColours;
            return seen;
        });

        // So the dialog opens on the colour being changed, not on black.
        Assert.Equal(new Rgb(0, 255, 0), offered);
    }

    [Fact]
    public void CancellingThePickerChangesNothing()
    {
        var colour = OnCanvas((canvas, drawing) =>
        {
            DrawAndSelect(canvas, drawing);

            // A cancelled dialog simply never calls back.
            void Picker(Rgb current, Action<Rgb> chosen) { }

            Row(PropertySource.Build(canvas, Picker), "Colour").Value = PropertySource.MoreColours;

            return drawing.Entities[0].Style.Color;
        });

        Assert.Equal(Rgb.White, colour);
    }

    [Fact]
    public void APickedColourAppliesAcrossAWholeSelection()
    {
        var colours = OnCanvas((canvas, drawing) =>
        {
            DrawAndSelectMany(canvas, 3);

            void Picker(Rgb current, Action<Rgb> chosen) => chosen(new Rgb(200, 100, 50));

            Row(PropertySource.Build(canvas, Picker), "Colour").Value = PropertySource.MoreColours;

            return drawing.Entities.Select(entity => entity.Style.Color).Distinct().ToList();
        });

        Assert.Equal(new Rgb(200, 100, 50), Assert.Single(colours));
    }

    // ---- several objects at once ------------------------------------------

    /// <summary>Draws a stack of lines and selects them all.</summary>
    private static void DrawAndSelectMany(CadCanvas canvas, int count)
    {
        canvas.UseTool(new LineTool());

        for (int i = 0; i < count; i++)
        {
            canvas.PlaceToolPoint(new Vec2(0, i * 10));
            canvas.PlaceToolPoint(new Vec2(100, i * 10));
        }

        canvas.UseSelect();
        for (int i = 0; i < count; i++) canvas.PickAt(new Vec2(50, i * 10), extend: true);
    }

    [Fact]
    public void SeveralObjectsShowWhatTheyHaveInCommon()
    {
        var rows = OnCanvas((canvas, _) =>
        {
            DrawAndSelectMany(canvas, 3);
            return PropertySource.Build(canvas);
        });

        Assert.Equal("3 lines", Row(rows, "Selection").Value);

        // The three every entity has, whatever it is, and all editable.
        Assert.Equal("0", Row(rows, "Layer").Value);
        Assert.Equal("White", Row(rows, "Colour").Value);
        Assert.False(Row(rows, "Layer").IsReadOnly);
        Assert.False(Row(rows, "Lineweight").IsReadOnly);

        // Positions are not offered across a selection: setting every start
        // point to one coordinate would just stack them.
        Assert.DoesNotContain(rows, row => row.Name == "Start X");
    }

    [Fact]
    public void SettingTheLayerOfManyObjectsIsOneUndoStep()
    {
        var (moved, afterUndo) = OnCanvas((canvas, drawing) =>
        {
            drawing.AddLayer(new FreeDwg.Core.Scene.Layer("WALLS"));
            DrawAndSelectMany(canvas, 4);

            Row(PropertySource.Build(canvas), "Layer").Value = "WALLS";
            int onWalls = drawing.Entities.Count(entity => entity.LayerIndex == 1);

            // One press of Ctrl+Z, not four.
            canvas.Undo();
            return (onWalls, drawing.Entities.Count(entity => entity.LayerIndex == 1));
        });

        Assert.Equal(4, moved);
        Assert.Equal(0, afterUndo);
    }

    [Fact]
    public void AFieldTheSelectionDisagreesOnShowsAsVarying()
    {
        var rows = OnCanvas((canvas, drawing) =>
        {
            drawing.AddLayer(new FreeDwg.Core.Scene.Layer("WALLS"));
            DrawAndSelectMany(canvas, 2);

            // Move one of them, then take both again.
            canvas.Selection.Clear();
            canvas.PickAt(new Vec2(50, 0));
            Row(PropertySource.Build(canvas), "Layer").Value = "WALLS";

            canvas.PickAt(new Vec2(50, 10), extend: true);
            return PropertySource.Build(canvas);
        });

        // Showing one of the two answers would be a lie about the other.
        Assert.Equal(PropertySource.Varies, Row(rows, "Layer").Value);
        Assert.Contains(PropertySource.Varies, Row(rows, "Layer").Choices!);
    }

    [Fact]
    public void NoColourChipWhileTheSelectionDisagrees()
    {
        bool chip = OnCanvas((canvas, _) =>
        {
            DrawAndSelectMany(canvas, 2);

            canvas.Selection.Clear();
            canvas.PickAt(new Vec2(50, 0));
            Row(PropertySource.Build(canvas), "Colour").Value = "Red";

            canvas.PickAt(new Vec2(50, 10), extend: true);
            return Row(PropertySource.Build(canvas), "Colour").HasSwatch;
        });

        // There is no one colour to show a chip of.
        Assert.False(chip);
    }

    [Fact]
    public void ChoosingTheVaryingMarkerChangesNothing()
    {
        var layers = OnCanvas((canvas, drawing) =>
        {
            drawing.AddLayer(new FreeDwg.Core.Scene.Layer("WALLS"));
            DrawAndSelectMany(canvas, 2);

            canvas.Selection.Clear();
            canvas.PickAt(new Vec2(50, 0));
            Row(PropertySource.Build(canvas), "Layer").Value = "WALLS";
            canvas.PickAt(new Vec2(50, 10), extend: true);

            Row(PropertySource.Build(canvas), "Layer").Value = PropertySource.Varies;

            return drawing.Entities.Select(entity => entity.LayerIndex).OrderBy(index => index).ToList();
        });

        // Picking the marker back is changing your mind, not an instruction.
        Assert.Equal([0, 1], layers);
    }

    [Fact]
    public void SettingARadiusAcrossSeveralCircles()
    {
        var radii = OnCanvas((canvas, drawing) =>
        {
            canvas.UseTool(new CircleTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(10, 0));
            canvas.PlaceToolPoint(new Vec2(0, 60));
            canvas.PlaceToolPoint(new Vec2(25, 60));

            canvas.UseSelect();
            canvas.PickAt(new Vec2(10, 0));
            canvas.PickAt(new Vec2(25, 60), extend: true);

            var rows = PropertySource.Build(canvas);

            // They disagree to start with, and agree afterwards.
            Assert.Equal(PropertySource.Varies, Row(rows, "Radius").Value);
            Row(rows, "Radius").Value = "7";

            return drawing.Entities.OfType<SCircle>().Select(circle => circle.Radius).ToList();
        });

        Assert.Equal([7.0, 7.0], radii);
    }

    [Fact]
    public void AGeometryRowIsOnlyOfferedWhenEverythingSelectedHasIt()
    {
        var names = OnCanvas((canvas, _) =>
        {
            canvas.UseTool(new CircleTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(10, 0));

            canvas.UseTool(new LineTool());
            canvas.PlaceToolPoint(new Vec2(0, 60));
            canvas.PlaceToolPoint(new Vec2(100, 60));

            canvas.UseSelect();
            canvas.PickAt(new Vec2(10, 0));
            canvas.PickAt(new Vec2(50, 60), extend: true);

            return PropertySource.Build(canvas).Select(row => row.Name).ToList();
        });

        // A circle and a line have a layer and a colour in common, and no
        // geometry at all.
        Assert.Contains("Colour", names);
        Assert.DoesNotContain("Radius", names);
    }

    [Fact]
    public void EditingManyObjectsKeepsThemAllSelected()
    {
        int count = OnCanvas((canvas, _) =>
        {
            DrawAndSelectMany(canvas, 5);
            Row(PropertySource.Build(canvas), "Colour").Value = "Red";

            // The edit swaps every one of them for a copy, so the selection
            // has to follow or the panel empties itself mid-edit.
            return canvas.Selection.Count;
        });

        Assert.Equal(5, count);
    }

    [Fact]
    public void EditingASelectionKeepsThePaintingOrderItWasIn()
    {
        var order = OnCanvas((canvas, drawing) =>
        {
            DrawAndSelectMany(canvas, 4);

            // Picked bottom-up, which is not the order they sit in.
            canvas.Selection.Clear();
            for (int i = 3; i >= 0; i--) canvas.PickAt(new Vec2(50, i * 10), extend: true);

            Row(PropertySource.Build(canvas), "Colour").Value = "Blue";

            // Entity order is painting order, so a batch edit that put them
            // back in the order they were picked would reshuffle the drawing.
            return drawing.Entities.OfType<SLine>().Select(line => line.Start.Y).ToList();
        });

        Assert.Equal([0.0, 10.0, 20.0, 30.0], order);
    }

    // ---- editing settings --------------------------------------------------

    [Fact]
    public void TypingAFilletRadiusReachesTheCanvas()
    {
        double radius = OnCanvas((canvas, _) =>
        {
            Row(PropertySource.Build(canvas), "Fillet radius").Value = "12.5";
            return canvas.FilletRadius;
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
            canvas.FilletRadius = 5;

            var row = Row(PropertySource.Build(canvas), "Fillet radius");
            row.Value = "not a number";

            return (canvas.FilletRadius, row.Value);
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
            return canvas.FilletRadius;
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
