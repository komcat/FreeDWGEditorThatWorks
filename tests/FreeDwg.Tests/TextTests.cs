using FreeDwg.Core.Geometry;
using FreeDwg.Tests.Fixtures;
using FreeDwg.Tests.Rendering;

namespace FreeDwg.Tests;

public sealed class TextTests : IClassFixture<TextFixture>
{
    private readonly TextFixture _fixture;

    public TextTests(TextFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData(-16, 0.97, 1.0, "a continuous line is unbroken")]
    [InlineData(-8, 0.25, 0.75, "a linetype set on the entity dashes")]
    [InlineData(0, 0.25, 0.75, "a linetype inherited from the layer dashes")]
    [InlineData(8, 0.45, 0.95, "a dash-dot pattern dashes")]
    public void StrokesDashPatterns(double y, double low, double high, string what) =>
        _fixture.Probe.AssertInkRatio(new Vec2(12, y), new Vec2(108, y), low, high, what);

    /// <summary>
    /// DWG keeps left-baseline text on the insertion point but everything
    /// else on the alignment point. Reading the wrong one shifts a label by
    /// its own width, which is exactly what the side of the anchor the ink
    /// falls on detects.
    /// </summary>
    [Theory]
    [InlineData(10, 25, false, true, "LEFT runs right from its anchor")]
    [InlineData(110, 25, true, false, "RIGHT runs left from its anchor")]
    [InlineData(60, 45, true, true, "CENTER straddles its anchor")]
    public void AnchorsTextOnTheCorrectPoint(
        double anchorX, double anchorY, bool expectLeft, bool expectRight, string what)
    {
        const double Height = 8;
        double pad = Height * 6;

        var ink = _fixture.Probe.InkExtentIn(
            new Vec2(anchorX - pad, anchorY - Height),
            new Vec2(anchorX + pad, anchorY + Height * 1.4));

        Assert.False(ink.IsEmpty, $"{what}: no text found near the anchor.");

        // Half a character of slack for glyph bearings and antialiasing.
        double slack = Height * 0.5;

        Assert.True(ink.MinX < anchorX - slack == expectLeft,
            $"{what}: ink spans {ink.MinX:0.#}..{ink.MaxX:0.#}, anchor at {anchorX:0.#}.");
        Assert.True(ink.MaxX > anchorX + slack == expectRight,
            $"{what}: ink spans {ink.MinX:0.#}..{ink.MaxX:0.#}, anchor at {anchorX:0.#}.");
    }

    [Fact]
    public void AppliesTheWidthFactor()
    {
        var wide = _fixture.Probe.InkExtentIn(new Vec2(70, 55), new Vec2(140, 72));
        var plain = _fixture.Probe.InkExtentIn(new Vec2(5, 20), new Vec2(60, 38));

        Assert.False(wide.IsEmpty);
        Assert.False(plain.IsEmpty);
        Assert.True(wide.Width > 1.4 * plain.Width,
            $"a width factor of 2 should stretch the text: {wide.Width:0.#} vs {plain.Width:0.#}");
    }

    [Fact]
    public void RotatesText()
    {
        var ink = _fixture.Probe.InkExtentIn(new Vec2(8, 55), new Vec2(60, 90));

        Assert.False(ink.IsEmpty);
        Assert.True(ink.Height > 8, $"text rotated 30 degrees should rise across its length, got {ink.Height:0.#}");
    }

    [Fact]
    public void HangsMTextBelowATopLeftAnchor()
    {
        var ink = _fixture.Probe.InkExtentIn(new Vec2(8, 75), new Vec2(70, 105));

        Assert.False(ink.IsEmpty);
        Assert.True(ink.MaxY <= 100.5, $"nothing should sit above the anchor at y=100, ink reaches {ink.MaxY:0.#}");
        Assert.True(ink.MinY < 90, $"three lines should reach well below the anchor, ink stops at {ink.MinY:0.#}");
    }

    [Fact]
    public void WrapsMTextInsideItsRectangle()
    {
        var ink = _fixture.Probe.InkExtentIn(new Vec2(93, 70), new Vec2(160, 105));

        Assert.False(ink.IsEmpty);
        Assert.True(ink.MaxX <= 136, $"the paragraph should stay inside its 40-unit rectangle, ink reaches {ink.MaxX:0.#}");
        Assert.True(ink.Height > 10, $"wrapping should produce several lines, got {ink.Height:0.#} of height");
    }

    [Fact]
    public void ReportsSubstitutedShxFonts()
    {
        var substitutions = _fixture.Diagnostics.FontSubstitutions;

        Assert.True(substitutions.ContainsKey("romans"),
            $"expected the SHX font to be reported, got [{string.Join(", ", substitutions.Keys)}]");
    }

    [Fact]
    public void ImportsLinetypePatterns()
    {
        var names = _fixture.Drawing.Linetypes.Select(l => l.Name).ToList();

        Assert.Contains("DASHED5", names);
        Assert.Contains("CENTERLINE", names);
        Assert.All(_fixture.Drawing.Linetypes, linetype => Assert.True(linetype.PatternLength > 0));
    }
}
