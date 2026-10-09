using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using FreeDwg.Core.Rendering;

namespace FreeDWGEditorThatWorks.ViewModels;

/// <summary>
/// The names the text box and the properties panel offer for a typeface and
/// a justification, and what each one means.
/// </summary>
/// <remarks>
/// One list for both places, so a font picked in one is spelled the same in
/// the other and a justification means the same thing wherever it is chosen.
/// </remarks>
public static class TextChoices
{
    /// <summary>Not a font: the drawing's own standard style, whatever that draws as.</summary>
    public const string StandardFont = "(Standard)";

    private static List<string>? _fonts;

    /// <summary>The standard style, then every family installed, alphabetically.</summary>
    public static IReadOnlyList<string> Fonts(string? include = null)
    {
        _fonts ??= System.Windows.Media.Fonts.SystemFontFamilies
            .Select(family => family.Source)
            .Distinct()
            .OrderBy(name => name, System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        var list = new List<string> { StandardFont };

        // A family the drawing names but this machine lacks is still offered,
        // or the box would have nothing to show for the text it describes.
        if (include is not null && !_fonts.Contains(include)) list.Add(include);

        list.AddRange(_fonts);
        return list;
    }

    public static string FontName(string? family) => family ?? StandardFont;

    public static string? FamilyOf(string name) => name == StandardFont ? null : name;

    private static readonly (string Name, TextAnchorX X, TextAnchorY Y)[] Justifications =
    [
        ("Top left", TextAnchorX.Left, TextAnchorY.Top),
        ("Top centre", TextAnchorX.Center, TextAnchorY.Top),
        ("Top right", TextAnchorX.Right, TextAnchorY.Top),
        ("Middle left", TextAnchorX.Left, TextAnchorY.Middle),
        ("Middle centre", TextAnchorX.Center, TextAnchorY.Middle),
        ("Middle right", TextAnchorX.Right, TextAnchorY.Middle),
        ("Bottom left", TextAnchorX.Left, TextAnchorY.Bottom),
        ("Bottom centre", TextAnchorX.Center, TextAnchorY.Bottom),
        ("Bottom right", TextAnchorX.Right, TextAnchorY.Bottom),
        ("Baseline left", TextAnchorX.Left, TextAnchorY.Baseline),
        ("Baseline centre", TextAnchorX.Center, TextAnchorY.Baseline),
        ("Baseline right", TextAnchorX.Right, TextAnchorY.Baseline),
    ];

    /// <summary>Which point of the text sits on its insertion point, by name.</summary>
    public static IReadOnlyList<string> JustificationNames { get; } =
        Justifications.Select(j => j.Name).ToList();

    public static string JustificationName(TextAnchorX x, TextAnchorY y) =>
        Justifications.First(j => j.X == x && j.Y == y).Name;

    public static bool TryJustification(string name, out TextAnchorX x, out TextAnchorY y)
    {
        foreach (var j in Justifications)
        {
            if (j.Name != name) continue;

            (x, y) = (j.X, j.Y);
            return true;
        }

        (x, y) = (TextAnchorX.Left, TextAnchorY.Top);
        return false;
    }
}
