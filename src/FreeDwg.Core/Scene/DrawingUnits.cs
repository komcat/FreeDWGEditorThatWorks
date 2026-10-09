using System.Globalization;

namespace FreeDwg.Core.Scene;

/// <summary>What one drawing unit means.</summary>
/// <remarks>
/// The ones DWG's INSUNITS header carries in practice. A file naming
/// anything else -- microinches, angstroms and parsecs are all in the
/// specification -- reads as <see cref="Unitless"/> rather than being
/// guessed at, because being wrong by a factor of ten thousand is worse
/// than saying nothing.
/// </remarks>
public enum DrawingUnits
{
    /// <summary>
    /// The file did not say, or said something not modelled here. The
    /// numbers are shown at face value and nothing claims to know what they
    /// count.
    /// </summary>
    Unitless,

    Millimetres,
    Centimetres,
    Metres,
    Kilometres,
    Inches,
    Feet,
    Yards,
    Miles,
}

/// <summary>
/// Turning lengths into text and back, in whatever the drawing is measured
/// in.
/// </summary>
/// <remarks>
/// A coordinate in the scene is a bare number, and the unit says what that
/// number counts -- exactly as DWG does it, where the file holds numbers and
/// the INSUNITS header says what they are. Changing the setting therefore
/// re-labels the drawing rather than rescaling it: a line 50 long becomes 50
/// inches instead of 50 millimetres, and does not move.
/// <para>
/// The one place the conversion factors matter is typing. A length entered
/// as 3ft in a millimetre drawing has to arrive as 914.4, because that is
/// what three feet is in the numbers this drawing counts in.
/// </para>
/// </remarks>
public static class Units
{
    /// <summary>How many millimetres one of these is.</summary>
    public static double InMillimetres(DrawingUnits units) => units switch
    {
        DrawingUnits.Centimetres => 10.0,
        DrawingUnits.Metres => 1000.0,
        DrawingUnits.Kilometres => 1_000_000.0,
        DrawingUnits.Inches => 25.4,
        DrawingUnits.Feet => 304.8,
        DrawingUnits.Yards => 914.4,
        DrawingUnits.Miles => 1_609_344.0,

        // A unitless drawing has to be drawn at some size, and a millimetre
        // is the least surprising one to pick -- but nothing labels it.
        _ => 1.0,
    };

    public static string Suffix(DrawingUnits units) => units switch
    {
        DrawingUnits.Millimetres => "mm",
        DrawingUnits.Centimetres => "cm",
        DrawingUnits.Metres => "m",
        DrawingUnits.Kilometres => "km",
        DrawingUnits.Inches => "in",
        DrawingUnits.Feet => "ft",
        DrawingUnits.Yards => "yd",
        DrawingUnits.Miles => "mi",

        // Deliberately blank: a readout that put "mm" on a drawing whose
        // file never said so would be inventing the one fact it is there
        // to report.
        _ => "",
    };

    /// <summary>The name shown in a settings list.</summary>
    public static string Name(DrawingUnits units) => units switch
    {
        DrawingUnits.Millimetres => "Millimetres (mm)",
        DrawingUnits.Centimetres => "Centimetres (cm)",
        DrawingUnits.Metres => "Metres (m)",
        DrawingUnits.Kilometres => "Kilometres (km)",
        DrawingUnits.Inches => "Inches (in)",
        DrawingUnits.Feet => "Feet (ft)",
        DrawingUnits.Yards => "Yards (yd)",
        DrawingUnits.Miles => "Miles (mi)",
        _ => "Unitless",
    };

    public static IReadOnlyList<DrawingUnits> All { get; } =
    [
        DrawingUnits.Unitless,
        DrawingUnits.Millimetres, DrawingUnits.Centimetres,
        DrawingUnits.Metres, DrawingUnits.Kilometres,
        DrawingUnits.Inches, DrawingUnits.Feet,
        DrawingUnits.Yards, DrawingUnits.Miles,
    ];

    public static bool TryParseName(string text, out DrawingUnits units)
    {
        foreach (var candidate in All)
        {
            if (!string.Equals(Name(candidate), text.Trim(), StringComparison.OrdinalIgnoreCase)) continue;

            units = candidate;
            return true;
        }

        units = DrawingUnits.Millimetres;
        return false;
    }

    public static string Format(double value, DrawingUnits units, int decimals) =>
        value.ToString("0." + new string('#', Math.Clamp(decimals, 0, 12)), CultureInfo.InvariantCulture);

    /// <summary>With the unit on the end, for a readout.</summary>
    public static string Describe(double value, DrawingUnits units, int decimals) =>
        $"{Format(value, units, decimals)} {Suffix(units)}";

    /// <summary>
    /// Reads a typed length as a number of drawing units.
    /// </summary>
    /// <remarks>
    /// A bare number is already in the drawing's units. A number with a unit
    /// on it is converted, so 3ft in a millimetre drawing arrives as 914.4 --
    /// which is how anyone working to a drawing in one unit and a datasheet
    /// in another expects to be able to type.
    /// </remarks>
    public static bool TryParseLength(string text, DrawingUnits document, out double value) =>
        TryParseLength(text, document, out value, out _);

    /// <summary>
    /// The same, also saying which unit the text named -- null for a bare
    /// number -- so the box it was typed in can show what it converted to.
    /// </summary>
    public static bool TryParseLength(string text, DrawingUnits document, out double value, out DrawingUnits? named)
    {
        value = 0;
        named = null;

        string trimmed = text.Trim();
        if (trimmed.Length == 0) return false;

        // Feet and inches together, the way an architectural drawing writes
        // them: 5'6", 5' 6, 5'-6", 5ft 6in. Read before the single-unit form,
        // which would otherwise take the inch mark and choke on the feet.
        var compound = FeetAndInches.Match(trimmed);
        if (compound.Success)
        {
            double feet = double.Parse(compound.Groups["feet"].Value, CultureInfo.InvariantCulture);
            double inches = double.Parse(compound.Groups["inches"].Value, CultureInfo.InvariantCulture);

            named = DrawingUnits.Feet;
            value = (feet * 12 + inches) * InMillimetres(DrawingUnits.Inches) / InMillimetres(document);
            return true;
        }

        DrawingUnits? unit = null;
        foreach (var (suffix, units) in Suffixes)
        {
            if (!trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;

            // A unit has to stand on its own: "5min" is not five inches with
            // something in front of it, it is a typing mistake.
            string rest = trimmed[..^suffix.Length];
            if (rest.Length > 0 && char.IsLetter(rest[^1])) continue;

            unit = units;
            trimmed = rest.Trim();
            break;
        }

        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            return false;

        named = unit;
        value = number * InMillimetres(unit ?? document) / InMillimetres(document);
        return true;
    }

    private static readonly System.Text.RegularExpressions.Regex FeetAndInches = new(
        """^(?<feet>\d+(\.\d+)?)\s*('|ft|foot|feet)\s*-?\s*(?<inches>\d+(\.\d+)?)\s*("|in|inch|inches)?$""",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Longest first, so that "mm" is not read as "m" with a stray letter in
    /// front of it, nor "inches" as "es" after something.
    /// </summary>
    private static readonly (string Suffix, DrawingUnits Units)[] Suffixes =
        new (string Suffix, DrawingUnits Units)[]
        {
            ("mm", DrawingUnits.Millimetres), ("millimetre", DrawingUnits.Millimetres),
            ("millimetres", DrawingUnits.Millimetres), ("millimeter", DrawingUnits.Millimetres),
            ("millimeters", DrawingUnits.Millimetres),
            ("cm", DrawingUnits.Centimetres), ("centimetre", DrawingUnits.Centimetres),
            ("centimetres", DrawingUnits.Centimetres), ("centimeter", DrawingUnits.Centimetres),
            ("centimeters", DrawingUnits.Centimetres),
            ("m", DrawingUnits.Metres), ("metre", DrawingUnits.Metres), ("metres", DrawingUnits.Metres),
            ("meter", DrawingUnits.Metres), ("meters", DrawingUnits.Metres),
            ("km", DrawingUnits.Kilometres), ("kilometre", DrawingUnits.Kilometres),
            ("kilometres", DrawingUnits.Kilometres), ("kilometer", DrawingUnits.Kilometres),
            ("kilometers", DrawingUnits.Kilometres),
            ("in", DrawingUnits.Inches), ("inch", DrawingUnits.Inches), ("inches", DrawingUnits.Inches),
            ("\"", DrawingUnits.Inches),
            ("ft", DrawingUnits.Feet), ("foot", DrawingUnits.Feet), ("feet", DrawingUnits.Feet),
            ("'", DrawingUnits.Feet),
            ("yd", DrawingUnits.Yards), ("yard", DrawingUnits.Yards), ("yards", DrawingUnits.Yards),
            ("mi", DrawingUnits.Miles), ("mile", DrawingUnits.Miles), ("miles", DrawingUnits.Miles),
        }
        .OrderByDescending(entry => entry.Suffix.Length)
        .ToArray();
}
