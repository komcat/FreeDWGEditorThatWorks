using FreeDwg.Core.Scene;

namespace FreeDwg.Tests;

/// <summary>
/// What a drawing unit means, and how a typed length is read.
/// </summary>
/// <remarks>
/// The scene holds bare numbers and the unit says what they count, exactly
/// as DWG's INSUNITS does. So the only place conversion happens is typing: a
/// length entered in one unit has to arrive in the numbers the drawing
/// counts in.
/// </remarks>
public sealed class UnitTests
{
    [Fact]
    public void ANewDrawingIsInMillimetres()
    {
        var drawing = Drawing.CreateEmpty();

        // The default everywhere it is not stated, since a mechanical
        // drawing is far more often in millimetres than anything else.
        Assert.Equal(DrawingUnits.Millimetres, drawing.Units);
    }

    [Theory]
    [InlineData("50", 50.0)]
    [InlineData("  50.5  ", 50.5)]
    [InlineData("50mm", 50.0)]
    [InlineData("2in", 50.8)]
    [InlineData("1ft", 304.8)]
    [InlineData("0.5m", 500.0)]
    public void ALengthTypedInAnyUnitArrivesInTheDrawingsOwn(string typed, double expected)
    {
        Assert.True(Units.TryParseLength(typed, DrawingUnits.Millimetres, out double value));
        Assert.Equal(expected, value, 9);
    }

    [Theory]
    [InlineData("36", 36.0)]
    [InlineData("3ft", 36.0)]
    [InlineData("914.4mm", 36.0)]
    public void TheSameLengthReadsTheSameWhateverItIsTypedIn(string typed, double expected)
    {
        // A drawing built in inches, and three feet typed into it.
        Assert.True(Units.TryParseLength(typed, DrawingUnits.Inches, out double value));
        Assert.Equal(expected, value, 6);
    }

    [Theory]
    [InlineData("12\"", 12.0)]
    [InlineData("1'", 12.0)]
    public void TheFootAndInchMarksAreUnderstood(string typed, double expected)
    {
        Assert.True(Units.TryParseLength(typed, DrawingUnits.Inches, out double value));
        Assert.Equal(expected, value, 9);
    }

    [Fact]
    public void MillimetresAreNotReadAsMetresWithAStrayLetter()
    {
        // "mm" ends in "m", so the suffixes have to be tried longest first.
        Assert.True(Units.TryParseLength("500mm", DrawingUnits.Metres, out double value));
        Assert.Equal(0.5, value, 9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("fifty")]
    [InlineData("mm")]
    [InlineData("50 furlongs")]
    public void NonsenseIsRefused(string typed)
    {
        Assert.False(Units.TryParseLength(typed, DrawingUnits.Millimetres, out _));
    }

    [Fact]
    public void ChangingTheUnitRelabelsRatherThanResizes()
    {
        var drawing = Drawing.CreateEmpty();
        drawing.Add(new FreeDwg.Core.Scene.Entities.SLine(
            FreeDwg.Core.Geometry.Vec2.Zero, new FreeDwg.Core.Geometry.Vec2(50, 0)));

        var before = drawing.Bounds;
        drawing.Units = DrawingUnits.Inches;

        // Fifty millimetres becomes fifty inches, and does not move: the
        // coordinates are what the file holds and the unit says what they
        // count, which is how INSUNITS behaves.
        Assert.Equal(before.MaxX, drawing.Bounds.MaxX, 9);
    }

    [Theory]
    [InlineData(1234.56789, 3, "1234.568")]
    [InlineData(1234.56789, 0, "1235")]
    [InlineData(50.0, 3, "50")]
    public void LengthsAreShownToTheDrawingsPrecision(double value, int decimals, string expected)
    {
        Assert.Equal(expected, Units.Format(value, DrawingUnits.Millimetres, decimals));
    }

    [Fact]
    public void AReadoutCarriesTheUnitItIsIn()
    {
        Assert.Equal("25.4 mm", Units.Describe(25.4, DrawingUnits.Millimetres, 3));
        Assert.Equal("1 in", Units.Describe(1, DrawingUnits.Inches, 3));
    }

    [Fact]
    public void EveryUnitHasANameThatReadsBack()
    {
        foreach (var units in Units.All)
        {
            Assert.True(Units.TryParseName(Units.Name(units), out var again),
                $"'{Units.Name(units)}' was offered but not accepted");

            Assert.Equal(units, again);
        }
    }
}
