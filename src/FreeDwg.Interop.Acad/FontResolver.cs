using ACadSharp.Tables;

namespace FreeDwg.Interop.Acad;

/// <summary>
/// Turns a DWG text style into a font family name the renderer can ask for.
/// </summary>
/// <remarks>
/// DWG's native fonts are SHX: compiled stroke fonts, drawn as polylines
/// rather than filled glyphs, and not installed on the system. Rendering them
/// properly means parsing the .shx files, which is a project of its own. Until
/// then every SHX style is substituted with an outline font, which is legible
/// and correctly placed but not shaped like the original. Substitutions are
/// reported so the status bar can say so rather than quietly lying.
/// </remarks>
public sealed class FontResolver
{
    /// <summary>Stand-in for any SHX font.</summary>
    public string ShxSubstitute { get; set; } = "Arial";

    private readonly Dictionary<string, string> _substituted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>SHX styles seen so far, mapped to what was drawn instead.</summary>
    public IReadOnlyDictionary<string, string> Substitutions => _substituted;

    public (string Family, bool Bold, bool Italic, double WidthFactor, double Oblique) Resolve(TextStyle? style)
    {
        if (style is null)
            return (ShxSubstitute, false, false, 1.0, 0.0);

        bool bold = style.TrueType.HasFlag(FontFlags.Bold);
        bool italic = style.TrueType.HasFlag(FontFlags.Italic);
        double width = style.Width > 0 ? style.Width : 1.0;

        string family = FamilyFor(style);
        return (family, bold, italic, width, style.ObliqueAngle);
    }

    private string FamilyFor(TextStyle style)
    {
        string file = style.Filename ?? string.Empty;
        if (file.Length == 0) return ShxSubstitute;

        string name = Path.GetFileNameWithoutExtension(file);
        string extension = Path.GetExtension(file);

        if (extension.Equals(".shx", StringComparison.OrdinalIgnoreCase) || style.IsShapeFile)
        {
            _substituted[name] = ShxSubstitute;
            return ShxSubstitute;
        }

        // A TrueType style names the file; the family is usually the stem, and
        // WPF falls back on its own if the guess is not installed.
        return name.Length > 0 ? name : ShxSubstitute;
    }
}
