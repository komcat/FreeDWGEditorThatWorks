using System.IO;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Interop.Acad;

namespace FreeDwg.Tests;

/// <summary>
/// Three things a real file says that this used to get wrong: what its units
/// are, how tall its text is, and which font it asked for.
/// </summary>
/// <remarks>
/// All three failed the same way -- quietly, and only against files nobody
/// had opened yet. A folder of fourteen ordinary sample drawings had text
/// drawn at the wrong size in five of them, every font but three replaced
/// without a word, and nine files labelled millimetres that had never said
/// so. None of it showed up in a fixture written here, because a fixture
/// written here only ever contains what was thought of.
/// </remarks>
public sealed class ImportFidelityTests
{
    private static Drawing Import(CadDocument document, out ImportDiagnostics diagnostics)
    {
        diagnostics = new ImportDiagnostics();
        return DwgLoader.Convert(document, diagnostics);
    }

    // ---- units -------------------------------------------------------------

    [Theory]
    [InlineData(ACadSharp.Types.Units.UnitsType.Millimeters, DrawingUnits.Millimetres)]
    [InlineData(ACadSharp.Types.Units.UnitsType.Centimeters, DrawingUnits.Centimetres)]
    [InlineData(ACadSharp.Types.Units.UnitsType.Meters, DrawingUnits.Metres)]
    [InlineData(ACadSharp.Types.Units.UnitsType.Kilometers, DrawingUnits.Kilometres)]
    [InlineData(ACadSharp.Types.Units.UnitsType.Inches, DrawingUnits.Inches)]
    [InlineData(ACadSharp.Types.Units.UnitsType.Feet, DrawingUnits.Feet)]
    [InlineData(ACadSharp.Types.Units.UnitsType.Yards, DrawingUnits.Yards)]
    [InlineData(ACadSharp.Types.Units.UnitsType.Miles, DrawingUnits.Miles)]
    public void TheUnitComesFromTheHeader(ACadSharp.Types.Units.UnitsType header, DrawingUnits expected)
    {
        var document = new CadDocument();
        document.Header.InsUnits = header;

        Assert.Equal(expected, Import(document, out _).Units);
    }

    [Fact]
    public void AFileThatDoesNotSayItsUnitIsNotGivenOne()
    {
        var document = new CadDocument();
        document.Header.InsUnits = ACadSharp.Types.Units.UnitsType.Unitless;

        var drawing = Import(document, out var diagnostics);

        // It used to read as millimetres, which put a unit on the coordinate
        // readout that nothing in the file had said -- and made a typed 2in
        // land as though the drawing were metric.
        Assert.Equal(DrawingUnits.Unitless, drawing.Units);
        Assert.Equal("", Units.Suffix(drawing.Units));
        Assert.Contains(diagnostics.Messages, m => m.Contains("does not say"));
    }

    [Fact]
    public void AUnitThisEditorDoesNotModelIsReportedRatherThanGuessedAt()
    {
        var document = new CadDocument();
        document.Header.InsUnits = ACadSharp.Types.Units.UnitsType.Microinches;

        var drawing = Import(document, out var diagnostics);

        // Being wrong by a factor of twenty-five thousand is worse than
        // saying nothing.
        Assert.Equal(DrawingUnits.Unitless, drawing.Units);
        Assert.Contains(diagnostics.Messages, m => m.Contains("Microinches"));
    }

    // ---- text height -------------------------------------------------------

    private static CadDocument WithText(double styleHeight, double entityHeight, string file = "arial.ttf")
    {
        var document = new CadDocument();

        var style = new TextStyle("FIXED") { Filename = file, Height = styleHeight };
        document.TextStyles.Add(style);

        document.Entities.Add(new TextEntity
        {
            Value = "ROOM 101",
            Height = entityHeight,
            Style = style,
        });

        return document;
    }

    [Fact]
    public void AStyleWithAFixedHeightDecidesHowTallTheTextIs()
    {
        var text = Assert.Single(Import(WithText(styleHeight: 72, entityHeight: 2.5), out _)
            .Entities.OfType<SText>());

        // AutoCAD does not even ask for a height when the style fixes one,
        // so whatever number is sitting on the entity is not the answer.
        // Reading it was drawing thirty-seven of thirty-eight labels in one
        // sample file at the wrong size.
        Assert.Equal(72, text.Height);
        Assert.Equal(72, text.LineStep);
    }

    [Fact]
    public void AStyleThatLeavesTheHeightOpenLetsTheTextSayIt()
    {
        var text = Assert.Single(Import(WithText(styleHeight: 0, entityHeight: 2.5), out _)
            .Entities.OfType<SText>());

        Assert.Equal(2.5, text.Height);
    }

    [Fact]
    public void TextWithNoHeightAtAllOnAFixedStyleIsStillDrawn()
    {
        // Legal, and what a drawing produced entirely through a fixed style
        // looks like. It used to be dropped on the floor.
        var text = Assert.Single(Import(WithText(styleHeight: 3.5, entityHeight: 0), out _)
            .Entities.OfType<SText>());

        Assert.Equal(3.5, text.Height);
    }

    // ---- fonts -------------------------------------------------------------

    [Fact]
    public void AFontFileIsResolvedToTheFamilyTheSystemKnowsItBy()
    {
        // A DWG names a file, not a family, and the two differ more often
        // than they match: arialn.ttf is "Arial Narrow", times.ttf is "Times
        // New Roman". Handing over the stem worked for three of the
        // seventeen fonts in a folder of sample drawings.
        FontResolver.ResolveFile = file =>
            file.Equals("arialn.ttf", StringComparison.OrdinalIgnoreCase) ? "Arial Narrow" : null;

        try
        {
            var text = Assert.Single(Import(WithText(0, 2.5, "arialn.ttf"), out var diagnostics)
                .Entities.OfType<SText>());

            Assert.Equal("Arial Narrow", text.FontFamily);
            Assert.Empty(diagnostics.FontSubstitutions);
        }
        finally
        {
            FontResolver.ResolveFile = _ => null;
        }
    }

    [Fact]
    public void AFontThisMachineHasNotGotIsSubstitutedAndSaidSo()
    {
        var drawing = Import(WithText(0, 2.5, "cityb___.ttf"), out var diagnostics);
        var text = Assert.Single(drawing.Entities.OfType<SText>());

        // It used to be handed to the renderer as "cityb___", which WPF
        // silently replaced with its own default -- a drawing in the wrong
        // typeface, and a status bar reporting nothing.
        Assert.Equal("Arial", text.FontFamily);
        Assert.Contains("cityb___", diagnostics.FontSubstitutions.Keys);
    }

    [Theory]
    [InlineData("TXT")]
    [InlineData("SIMPLEX")]
    [InlineData("romanc")]
    [InlineData("ROMAND")]
    public void AStrokeFontNamedWithoutItsExtensionIsStillAStrokeFont(string file)
    {
        // Real files carry these with no extension at all. They were being
        // taken for TrueType, handed over as a family nobody has, and left
        // out of the substitution count the status bar reports.
        var diagnostics = new ImportDiagnostics();
        var drawing = DwgLoader.Convert(WithText(0, 2.5, file), diagnostics);

        Assert.Equal("Arial", Assert.Single(drawing.Entities.OfType<SText>()).FontFamily);
        Assert.Contains(file, diagnostics.FontSubstitutions.Keys);
    }

    /// <summary>
    /// A typeface picked here survives a file: a style made the way AutoCAD
    /// makes one -- the font file, and the face name with its bold and italic
    /// in the ACAD xdata -- and read back from that xdata.
    /// </summary>
    /// <remarks>
    /// In this class because it swaps the process-wide font hooks, and the
    /// tests of one class run one after another.
    /// </remarks>
    [Fact]
    public void AChosenTypefaceIsSavedAsAStyleAndReadBack()
    {
        FontResolver.ResolveFile = name =>
            name.Equals("verdana.ttf", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Verdana", StringComparison.OrdinalIgnoreCase) ? "Verdana" : null;
        FontResolver.FileOf = family => family == "Verdana" ? "verdana.ttf" : null;

        string file = Path.Combine(Path.GetTempPath(), $"freedwg-font-{Guid.NewGuid():N}.dwg");
        try
        {
            var session = DwgSession.CreateNew();
            var drawing = session.Drawing;
            var stack = new FreeDwg.Core.Commands.CommandStack(drawing);

            var format = FreeDwg.Core.Tools.TextFormat.Default with { Height = 3, FontFamily = "Verdana", Bold = true };
            var text = drawing.Place(FreeDwg.Core.Tools.TextTool.Make(new FreeDwg.Core.Geometry.Vec2(0, 0), ["Bold words"], format));
            stack.Do(new FreeDwg.Core.Commands.AddEntities(drawing.ActiveLayout, text));
            stack.Do(new FreeDwg.Core.Commands.ChangeTextSettings(new TextSettings(4, "Verdana", true, false)));

            session.Save(stack, file);

            var doc = ACadSharp.IO.DwgReader.Read(file);
            var style = Assert.Single(doc.Entities.OfType<TextEntity>()).Style;
            Assert.Equal("Verdana Bold", style.Name);
            Assert.Equal("verdana.ttf", style.Filename);

            var (face, flags) = FontResolver.FaceOf(style);
            Assert.Equal("Verdana", face);
            Assert.NotEqual(0, flags & FontResolver.BoldFlag);
            Assert.Equal(0, flags & FontResolver.ItalicFlag);

            Assert.Equal(4, doc.Header.TextHeightDefault);
            Assert.Equal("Verdana Bold", doc.Header.CurrentTextStyleName);

            var back = DwgLoader.Load(file);
            var read = Assert.Single(back.ModelSpace.Entities.OfType<SText>());
            Assert.Equal("Verdana", read.FontFamily);
            Assert.True(read.Bold);
            Assert.False(read.Italic);
            Assert.Equal(new TextSettings(4, "Verdana", true, false), back.Text);
        }
        finally
        {
            FontResolver.ResolveFile = _ => null;
            FontResolver.FileOf = _ => null;
            File.Delete(file);
        }
    }

    /// <summary>
    /// A file saved without its text settings being touched gets no new text
    /// style and keeps its current one -- even when its current style is a
    /// TrueType face the machine can draw, which is when re-finding that face
    /// by name would otherwise add a style nobody asked for.
    /// </summary>
    [Fact]
    public void AnUntouchedSaveLeavesTheTextStylesAlone()
    {
        FontResolver.ResolveFile = name =>
            name.Equals("verdana.ttf", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Verdana", StringComparison.OrdinalIgnoreCase) ? "Verdana" : null;
        FontResolver.FileOf = family => family == "Verdana" ? "verdana.ttf" : null;

        string file = Path.Combine(Path.GetTempPath(), $"freedwg-styles-{Guid.NewGuid():N}.dwg");
        try
        {
            var doc = new CadDocument();
            doc.TextStyles.Add(new TextStyle("Notes") { Filename = "verdana.ttf" });
            doc.Header.CurrentTextStyleName = "Notes";
            doc.Entities.Add(new Line(new CSMath.XYZ(0, 0, 0), new CSMath.XYZ(10, 0, 0)));
            ACadSharp.IO.DwgWriter.Write(file, doc);
            int styles = ACadSharp.IO.DwgReader.Read(file).TextStyles.Count;

            var session = DwgSession.Open(file);
            Assert.Equal("Verdana", session.Drawing.Text.FontFamily);
            session.Save(new FreeDwg.Core.Commands.CommandStack(session.Drawing));

            var back = ACadSharp.IO.DwgReader.Read(file);
            Assert.Equal(styles, back.TextStyles.Count);
            Assert.Equal("Notes", back.Header.CurrentTextStyleName);
        }
        finally
        {
            FontResolver.ResolveFile = _ => null;
            FontResolver.FileOf = _ => null;
            File.Delete(file);
            File.Delete(Path.ChangeExtension(file, ".bak"));
        }
    }
}
