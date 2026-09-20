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
/// copying the numbers across. There is no plot scale yet -- DWG's DIMSCALE
/// -- so a drawing meant for 1:100 gets text sized for 1:1.
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

    /// <summary>The ISO figures in <paramref name="drawing"/>'s own units.</summary>
    public static DimensionStyle For(Drawing drawing) =>
        ForUnits(drawing.Units, drawing.LinearPrecision);

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
