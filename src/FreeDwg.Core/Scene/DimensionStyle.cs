namespace FreeDwg.Core.Scene;

/// <summary>
/// The sizes and the number format a dimension is drawn with.
/// </summary>
/// <remarks>
/// Carried on the dimension itself rather than looked up, for the reason
/// every other style here is resolved rather than referenced: an entity that
/// has to reach back to a table to know how big it is cannot be drawn,
/// measured or hit tested on its own.
/// <para>
/// The defaults are ISO's, which are stated in millimetres of paper. A
/// drawing in metres wants the same marks at the same physical size, so
/// <see cref="For"/> converts them into the drawing's units rather than
/// copying the numbers across. A drawing that says otherwise -- in its
/// <see cref="Drawing.Dimensions"/>, read from the file's DIM variables or
/// set in the dimension style dialog -- wins.
/// </para>
/// </remarks>
public readonly record struct DimensionStyle(
    double TextHeight,
    double ArrowSize,
    double ExtensionOffset,
    double ExtensionBeyond,
    double TextGap,
    DrawingUnits DisplayUnits,
    int Decimals)
{
    /// <summary>ISO 129 in millimetres: 2.5 text, 2.5 arrows, 0.625 gaps.</summary>
    public static DimensionStyle Iso { get; } =
        new(2.5, 2.5, 0.625, 1.25, 0.625, DrawingUnits.Millimetres, 2);

    /// <summary>
    /// What a new dimension in <paramref name="drawing"/> is drawn with: its
    /// own settings if it has them, the ISO figures in its units if not.
    /// </summary>
    public static DimensionStyle For(Drawing drawing) =>
        drawing.Dimensions is { } settings
            ? settings.StyleIn(drawing.Units)
            : ForUnits(drawing.Units, drawing.LinearPrecision);

    public static DimensionStyle ForUnits(DrawingUnits units, int decimals)
    {
        double millimetre = Units.InMillimetres(units);
        double factor = millimetre > 0 ? 1.0 / millimetre : 1.0;

        return Iso.Scaled(factor) with { DisplayUnits = units, Decimals = decimals };
    }

    /// <summary>
    /// The same marks at a different size, for an entity that has been
    /// scaled. Only the lengths move; the number format is not a length.
    /// </summary>
    public DimensionStyle Scaled(double factor) => this with
    {
        TextHeight = TextHeight * factor,
        ArrowSize = ArrowSize * factor,
        ExtensionOffset = ExtensionOffset * factor,
        ExtensionBeyond = ExtensionBeyond * factor,
        TextGap = TextGap * factor,
    };
}

/// <summary>
/// The dimension settings a drawing keeps for the dimensions drawn in it:
/// the sizes at a scale of one, and an overall scale they are all
/// multiplied by.
/// </summary>
/// <remarks>
/// The scale is DWG's DIMSCALE, and it is separate from the sizes for the
/// reason it is in AutoCAD: the sizes are what the marks measure on paper,
/// and the scale is what a drawing at 1:100 needs them blown up by to plot at
/// that size. Changing it is one number rather than five.
/// </remarks>
public readonly record struct DimensionSettings(DimensionStyle Sizes, double Scale)
{
    /// <summary>What <paramref name="drawing"/> draws new dimensions with now.</summary>
    public static DimensionSettings For(Drawing drawing) =>
        drawing.Dimensions ?? new(DimensionStyle.ForUnits(drawing.Units, drawing.LinearPrecision), 1.0);

    /// <summary>The sizes as drawn, in a drawing measured in <paramref name="units"/>.</summary>
    public DimensionStyle StyleIn(DrawingUnits units) =>
        Sizes.Scaled(Scale > 0 ? Scale : 1.0) with { DisplayUnits = units };

    /// <summary>Whether every size is a size: text and arrows that can be seen, gaps that are not negative.</summary>
    public bool IsValid =>
        Sizes.TextHeight > 0 && Sizes.ArrowSize > 0 && Sizes.ExtensionOffset >= 0 &&
        Sizes.ExtensionBeyond >= 0 && Sizes.TextGap >= 0 && Sizes.Decimals is >= 0 and <= 8 && Scale > 0;
}
