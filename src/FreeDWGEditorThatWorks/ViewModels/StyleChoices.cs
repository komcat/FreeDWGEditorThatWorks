using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FreeDwg.Core.Styling;

namespace FreeDWGEditorThatWorks.ViewModels;

/// <summary>
/// The names a colour and a lineweight go by in the properties panel.
/// </summary>
/// <remarks>
/// Core resolves ByLayer, ByBlock and AutoCAD's colour indices away at
/// import, so by the time anything gets here a colour is 24 bits and a
/// lineweight is a number of hundredths of a millimetre. These put names back
/// on the handful of values people actually pick, and fall back to a hex
/// triple or a plain measurement for everything else, so no value in a file
/// is unrepresentable in the panel.
/// </remarks>
public static class StyleChoices
{
    /// <summary>
    /// The seven colours AutoCAD puts at the top of its own list. They are
    /// the first seven colour indices, and between them they cover most of
    /// what is in a real drawing.
    /// </summary>
    private static readonly (string Name, Rgb Colour)[] Named =
    [
        ("Red", new Rgb(255, 0, 0)),
        ("Yellow", new Rgb(255, 255, 0)),
        ("Green", new Rgb(0, 255, 0)),
        ("Cyan", new Rgb(0, 255, 255)),
        ("Blue", new Rgb(0, 0, 255)),
        ("Magenta", new Rgb(255, 0, 255)),
        ("White", Rgb.White),
        ("Black", Rgb.Black),
        ("Grey", new Rgb(128, 128, 128)),
    ];

    /// <summary>What picking this offers: take the current layer's colour.</summary>
    public const string ByLayer = "By layer";

    public static string ColourName(Rgb colour)
    {
        foreach (var (name, known) in Named)
            if (known == colour) return name;

        return $"#{colour.Packed:X6}";
    }

    /// <summary>
    /// Reads a name, or a hex triple with or without its hash. Returns false
    /// for anything else, which leaves the row showing what is really set.
    /// </summary>
    public static bool TryColour(string text, out Rgb colour)
    {
        colour = Rgb.Black;
        string trimmed = text.Trim();

        foreach (var (name, known) in Named)
        {
            if (!string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase)) continue;

            colour = known;
            return true;
        }

        string hex = trimmed.TrimStart('#');
        if (hex.Length != 6 ||
            !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint packed))
        {
            return false;
        }

        colour = new Rgb((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
        return true;
    }

    /// <summary>The names offered in the colour dropdown, current value included.</summary>
    public static IReadOnlyList<string> Colours(Rgb current)
    {
        var names = new List<string> { ByLayer };
        names.AddRange(Named.Select(entry => entry.Name));

        // A drawing full of hand-mixed colours should still show the one it
        // has rather than snapping to the nearest name in the list.
        string mine = ColourName(current);
        if (!names.Contains(mine)) names.Insert(1, mine);

        return names;
    }

    /// <summary>
    /// The standard plot widths, in hundredths of a millimetre. This is the
    /// list DWG itself allows, so a width chosen here is one every other CAD
    /// tool will read back unchanged.
    /// </summary>
    private static readonly short[] Standard =
    [
        0, 5, 9, 13, 15, 18, 20, 25, 30, 35, 40, 50, 53, 60, 70, 80,
        90, 100, 106, 120, 140, 158, 200, 211,
    ];

    public static string WeightName(Lineweight weight) => weight.Hundredths <= 0
        ? "Thinnest"
        : weight.Millimeters.ToString("0.00", CultureInfo.InvariantCulture) + " mm";

    public static bool TryWeight(string text, out Lineweight weight)
    {
        weight = Lineweight.Thinnest;
        string trimmed = text.Trim();

        if (string.Equals(trimmed, "Thinnest", StringComparison.OrdinalIgnoreCase)) return true;

        string number = trimmed.Replace("mm", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double millimetres)
            || millimetres < 0 || millimetres > 2.5)
        {
            return false;
        }

        weight = new Lineweight((short)Math.Round(millimetres * 100));
        return true;
    }

    public static IReadOnlyList<string> Weights(Lineweight current)
    {
        var names = Standard.Select(hundredths => WeightName(new Lineweight(hundredths))).ToList();

        string mine = WeightName(current);
        if (!names.Contains(mine)) names.Insert(0, mine);

        return names;
    }
}
