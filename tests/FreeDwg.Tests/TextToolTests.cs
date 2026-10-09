using System.IO;
using System.Windows;
using FreeDwg.Core.Commands;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Snapping;
using FreeDwg.Core.Tools;
using FreeDwg.Interop.Acad;
using FreeDwg.Tests.Rendering;
using FreeDWGEditorThatWorks.Controls;
using SceneDrawing = FreeDwg.Core.Scene.Drawing;

namespace FreeDwg.Tests;

/// <summary>
/// Placing text and editing it: a click, the words, Enter.
/// </summary>
/// <remarks>
/// Text is anchored at its top-left, as MTEXT is, so a second line grows
/// downwards from where the first was put instead of shoving it up. The
/// render test checks that in pixels, because "the first line did not move"
/// is a statement about where ink lands and nothing else.
/// </remarks>
public sealed class TextToolTests
{
    // ---- the tool ----------------------------------------------------------

    [Fact]
    public void TheClickIsNotEnoughAndEnterWithWordsIs()
    {
        var tool = new TextTool { Height = 5 };

        Assert.Null(tool.Click(new Vec2(10, 20)));
        Assert.True(tool.InProgress);

        tool.Lines = ["Hello", "there", ""];
        var text = Assert.IsType<SText>(tool.Finish());

        Assert.Equal(new Vec2(10, 20), text.Position);
        Assert.Equal(["Hello", "there"], text.Lines);   // the stray new line goes
        Assert.Equal(5, text.Height);
        Assert.Equal(TextAnchorY.Top, text.AnchorY);
        Assert.Equal(5 * TextTool.LineSpacing, text.LineStep, 9);
        Assert.False(tool.InProgress);
        Assert.Empty(tool.Lines);
    }

    [Fact]
    public void BlankTextIsNotPlaced()
    {
        var tool = new TextTool();
        tool.Click(Vec2.Zero);
        tool.Lines = ["   ", ""];

        Assert.Null(tool.Finish());
    }

    [Fact]
    public void ASecondClickMovesTheStartOrPlacesTheWords()
    {
        var tool = new TextTool();

        // Nothing typed yet: the second click is just a better place.
        tool.Click(new Vec2(0, 0));
        Assert.Null(tool.Click(new Vec2(50, 50)));
        Assert.Equal(new Vec2(50, 50), Assert.Single(tool.Points));

        // Words typed: clicking away places them where they were started.
        tool.Lines = ["Done"];
        var text = Assert.IsType<SText>(tool.Click(new Vec2(90, 90)));
        Assert.Equal(new Vec2(50, 50), text.Position);
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
    public void TypedTextGoesInOnTheCurrentLayerAsOneStep()
    {
        var (entry, text, undone) = OnCanvas((canvas, drawing) =>
        {
            canvas.UseTool(new TextTool());
            canvas.PlaceToolPoint(new Vec2(10, 10));
            var entry = canvas.Entry;

            canvas.SetTypedText(["Line one", "Line two"]);
            canvas.FinishTool();

            var text = (SText)Assert.Single(drawing.Entities);
            canvas.Undo();
            return (entry, text, drawing.Entities.Count);
        });

        Assert.Equal(CursorEntry.Text, entry);
        Assert.Equal(["Line one", "Line two"], text.Lines);
        Assert.Equal(0, text.LayerIndex);
        Assert.Equal(0, undone);
    }

    [Fact]
    public void NewTextFollowsTheDimensionTextUntilItIsGivenAHeight()
    {
        var (following, followed, set, stillSet) = OnCanvas((canvas, drawing) =>
        {
            bool following = canvas.TextHeightFollowsDimensions;

            canvas.SetDimensionSettings(new DimensionSettings(DimensionStyle.Iso, 4), restyleExisting: false);
            double followed = canvas.TextHeight;

            canvas.TextHeight = 7;
            double set = canvas.TextHeight;

            canvas.SetDimensionSettings(new DimensionSettings(DimensionStyle.Iso, 8), restyleExisting: false);
            return (following, followed, set, canvas.TextHeight);
        });

        Assert.True(following);
        Assert.Equal(10, followed, 9);
        Assert.Equal(7, set);
        Assert.Equal(7, stillSet);
    }

    [Fact]
    public void EditingOneLineIntoTwoHangsItFromItsTopWithTheFirstLineWhereItWas()
    {
        var (edited, undone) = OnCanvas((canvas, drawing) =>
        {
            // Baseline-anchored, as TEXT from a file is.
            var original = new SText(["Before"], new Vec2(0, 0), 2) { SourceHandle = 0x99 };
            canvas.Commands!.Do(new AddEntities(drawing.ActiveLayout, original));

            canvas.ReplaceText(original, ["After", "and more"]);
            var edited = (SText)drawing.Entities.Single();

            canvas.Undo();
            return (edited, ((SText)drawing.Entities.Single()).Lines);
        });

        Assert.Equal(["After", "and more"], edited.Lines);
        Assert.Equal(TextAnchorY.Top, edited.AnchorY);
        Assert.Equal(new Vec2(0, 2), edited.Position);       // the baseline, lifted by the cap height
        Assert.Equal(0x99ul, edited.SourceHandle);           // still the object the file knows
        Assert.True(edited.LineStep > edited.Height);
        Assert.Equal(["Before"], undone);
    }

    // ---- pixels ------------------------------------------------------------

    [Fact]
    public void TheFirstLineIsWhereItWasClickedAndTheSecondHangsBelowIt()
    {
        var drawing = SceneDrawing.CreateEmpty();
        drawing.Add(new SPolyline(
            [new(new(0, 0)), new(new(200, 0)), new(new(200, 120)), new(new(0, 120))], closed: true));

        StaRenderer.OnSta(() =>
        {
            var canvas = new CadCanvas { Drawing = drawing };
            canvas.TextHeight = 10;
            canvas.UseTool(new TextTool());
            canvas.PlaceToolPoint(new Vec2(20, 90));
            canvas.SetTypedText(["HELLO", "WORLD"]);
            canvas.FinishTool();
            return 0;
        });

        var probe = new Probe(StaRenderer.Render(drawing, saveAs: "text-tool"));
        var ink = probe.InkExtentIn(new Vec2(5, 30), new Vec2(190, 115));

        // The tops of the capitals sit on the click, not a line above it.
        Assert.InRange(ink.MaxY, 85, 91);
        Assert.InRange(ink.MinX, 18, 24);

        // And a second line exists below the first: ink well under one line.
        Assert.True(ink.MinY < 90 - 10 * TextTool.LineSpacing, $"ink stops at {ink.MinY}");
    }

    // ---- saved -------------------------------------------------------------

    [Fact]
    public void PlacedTextIsSavedAsTextOrMTextByItsLines()
    {
        var session = DwgSession.CreateNew();
        var drawing = session.Drawing;
        var stack = new CommandStack(drawing);

        var one = drawing.Place(TextTool.Make(new Vec2(0, 50), ["One line"], 3));
        var two = drawing.Place(TextTool.Make(new Vec2(0, 0), ["Two", "lines"], 3));
        stack.Do(new AddEntities(drawing.ActiveLayout, one, two));

        string file = Path.Combine(Path.GetTempPath(), $"freedwg-text-{Guid.NewGuid():N}.dwg");
        try
        {
            session.Save(stack, file);

            var doc = ACadSharp.IO.DwgReader.Read(file);
            Assert.Equal("One line", Assert.Single(doc.Entities.OfType<ACadSharp.Entities.TextEntity>()).Value);
            Assert.Single(doc.Entities.OfType<ACadSharp.Entities.MText>());

            var back = DwgLoader.Load(file).ModelSpace.Entities.OfType<SText>().ToList();
            var single = back.Single(t => t.Lines.Count == 1);
            var multi = back.Single(t => t.Lines.Count == 2);

            Assert.Equal(one.Position.X, single.Position.X, 6);
            Assert.Equal(one.Position.Y, single.Position.Y, 6);
            Assert.Equal(["Two", "lines"], multi.Lines);
            Assert.Equal(two.Position.Y, multi.Position.Y, 6);
            Assert.Equal(TextAnchorY.Top, multi.AnchorY);
        }
        finally
        {
            File.Delete(file);
        }
    }

    // ---- format ------------------------------------------------------------

    [Fact]
    public void TheFormatIsWhatTheTextIsPlacedWith()
    {
        var format = TextFormat.Default with
        {
            Height = 4, Rotation = Math.PI / 6, AnchorX = TextAnchorX.Center, AnchorY = TextAnchorY.Middle,
            FontFamily = "Georgia", Italic = true,
        };

        var text = TextTool.Make(new Vec2(3, 4), ["Hi"], format);

        Assert.Equal(new Vec2(3, 4), text.Position);
        Assert.Equal(format, TextFormat.Of(text));
    }

    [Fact]
    public void SeveralLinesOnABaselineHangFromTheTopWithTheFirstLineInPlace()
    {
        var format = TextFormat.Default with { Height = 2, AnchorY = TextAnchorY.Baseline };

        var one = TextTool.Make(Vec2.Zero, ["One"], format);
        var two = TextTool.Make(Vec2.Zero, ["One", "Two"], format);

        Assert.Equal(TextAnchorY.Baseline, one.AnchorY);
        Assert.Equal(TextAnchorY.Top, two.AnchorY);
        Assert.Equal(new Vec2(0, 2), two.Position);
    }

    [Fact]
    public void TheCanvasPlacesTextInItsTurnJustificationAndTypeface()
    {
        var (text, undone) = OnCanvas((canvas, drawing) =>
        {
            canvas.UseTool(new TextTool());
            canvas.TextRotation = Math.PI / 2;
            canvas.TextJustification = (TextAnchorX.Right, TextAnchorY.Bottom);
            canvas.SetTextSettings(drawing.Text with { FontFamily = "Georgia", Bold = true });

            canvas.PlaceToolPoint(new Vec2(5, 5));
            canvas.SetTypedText(["Up"]);
            canvas.FinishTool();
            var text = drawing.Entities.OfType<SText>().Single();

            canvas.Undo();   // the text
            canvas.Undo();   // the typeface
            return (text, drawing.Text);
        });

        Assert.Equal(Math.PI / 2, text.Rotation, 12);
        Assert.Equal(TextAnchorX.Right, text.AnchorX);
        Assert.Equal(TextAnchorY.Bottom, text.AnchorY);
        Assert.Equal("Georgia", text.FontFamily);
        Assert.True(text.Bold);
        Assert.Null(undone.FontFamily);
    }

    [Fact]
    public void ANewJustificationKeepsTheTextsPoint()
    {
        var edited = OnCanvas((canvas, drawing) =>
        {
            var original = TextTool.Make(new Vec2(10, 10), ["Label"], TextFormat.Default with { Height = 2 });
            canvas.Commands!.Do(new AddEntities(drawing.ActiveLayout, original));

            canvas.ReplaceText(original, original.Lines,
                TextFormat.Of(original) with { AnchorX = TextAnchorX.Center, AnchorY = TextAnchorY.Middle });
            return drawing.Entities.OfType<SText>().Single();
        });

        Assert.Equal(new Vec2(10, 10), edited.Position);
        Assert.Equal(TextAnchorX.Center, edited.AnchorX);
        Assert.Equal(TextAnchorY.Middle, edited.AnchorY);
    }

    [Fact]
    public void ANewJustificationOnTextFromAFileIsSaved()
    {
        var session = DwgSession.CreateNew();
        var drawing = session.Drawing;
        var stack = new CommandStack(drawing);

        var text = drawing.Place(TextTool.Make(new Vec2(20, 30), ["Label"], TextFormat.Default with { Height = 2 }));
        stack.Do(new AddEntities(drawing.ActiveLayout, text));

        string file = Path.Combine(Path.GetTempPath(), $"freedwg-justify-{Guid.NewGuid():N}.dwg");
        try
        {
            session.Save(stack, file);

            // Now justified from its middle, through the same swap the panel uses.
            var middle = (SText)text.Clone();
            middle.AnchorX = TextAnchorX.Center;
            middle.AnchorY = TextAnchorY.Middle;
            stack.Do(new ReplaceEntities(drawing.ActiveLayout,
                FreeDwg.Core.Editing.EditPlan.Replace(text, middle), "Justify"));
            session.Save(stack);

            var back = DwgLoader.Load(file).ModelSpace.Entities.OfType<SText>().Single();
            Assert.Equal(TextAnchorX.Center, back.AnchorX);
            Assert.Equal(TextAnchorY.Middle, back.AnchorY);
            Assert.Equal(20, back.Position.X, 6);
            Assert.Equal(30, back.Position.Y, 6);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
