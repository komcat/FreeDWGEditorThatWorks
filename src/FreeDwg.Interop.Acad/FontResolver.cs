using ACadSharp.Tables;

namespace FreeDwg.Interop.Acad;

/// <summary>
/// Turns a DWG text style into a font family name the renderer can ask for.
/// </summary>
/// <remarks>
/// A DWG names a <em>file</em>, not a family: <c>arialn.ttf</c>, not "Arial
/// Narrow". Those two are the same string often enough to be misleading --
/// <c>arial.ttf</c> is Arial -- and different often enough to matter:
/// <c>times.ttf</c> is Times New Roman, <c>gothic.ttf</c> is Century Gothic,
/// <c>verdanaz.ttf</c> is Verdana. Handing the stem to the renderer and
/// hoping worked for three of the seventeen fonts in a folder of ordinary
/// sample drawings; the rest fell back to whatever the default was, and
/// nothing said a word about it.
/// <para>
/// So the file name is resolved by whoever owns the font stack, through
/// <see cref="ResolveFile"/>, which the shell installs at startup for the
/// same reason it installs a text measurer. With nothing installed the stem
/// is used, which is what a test host wants.
/// </para>
/// <para>
/// DWG's native fonts are SHX: compiled stroke fonts, drawn as polylines
/// rather than filled glyphs, and not installed on the system. Rendering
/// them properly means parsing the .shx files, which is a project of its
/// own. Until then every SHX style is substituted with an outline font.
/// </para>
/// </remarks>
public sealed class FontResolver
{
    /// <summary>Stand-in for any SHX font, or any file the system has not got.</summary>
    public string ShxSubstitute { get; set; } = "Arial";

    /// <summary>
    /// Maps a font file name to the family the system knows it by, or null
    /// when it has no such file. Installed by the shell.
    /// </summary>
    /// <remarks>
    /// A process-wide hook rather than a parameter, for the reason
    /// <c>TextMetrics.Measure</c> is one: the importer is several layers
    /// away from anything that has ever heard of a glyph.
    /// </remarks>
    public static Func<string, string?> ResolveFile { get; set; } = _ => null;

    private readonly Dictionary<string, string> _substituted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Styles whose font was not available, mapped to what was drawn instead.</summary>
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

        if (IsStrokeFont(style, file))
        {
            _substituted[name] = ShxSubstitute;
            return ShxSubstitute;
        }

        // The system's own answer for that file, which is the only reliable
        // one. A name is still tried on its own, since a style may carry a
        // family rather than a file name.
        if (ResolveFile(file) is { Length: > 0 } installed) return installed;
        if (ResolveFile(name + ".ttf") is { Length: > 0 } guessed) return guessed;

        _substituted[name] = ShxSubstitute;
        return ShxSubstitute;
    }

    /// <summary>
    /// Whether this style names an SHX stroke font.
    /// </summary>
    /// <remarks>
    /// Not merely a <c>.shx</c> extension: real files carry <c>TXT</c>,
    /// <c>SIMPLEX</c> and <c>romanc</c> with no extension at all, and those
    /// were being taken for TrueType, handed to the renderer as a family
    /// nobody has, and left out of the substitution count that the status
    /// bar reports.
    /// </remarks>
    private static bool IsStrokeFont(TextStyle style, string file)
    {
        if (style.IsShapeFile) return true;

        string extension = Path.GetExtension(file);

        if (extension.Equals(".shx", StringComparison.OrdinalIgnoreCase)) return true;
        if (extension.Length > 0) return false;

        // No extension, and no such family installed: AutoCAD would look for
        // an .shx of that name, so that is what it is.
        return ResolveFile(file + ".shx") is null or { Length: 0 }
            ? Known.Contains(file)
            : true;
    }

    /// <summary>
    /// The stroke fonts AutoCAD ships, which is what an extension-less name
    /// nearly always is.
    /// </summary>
    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        "txt", "simplex", "complex", "italic", "italicc", "italict",
        "monotxt", "romanc", "romand", "romans", "romant",
        "scriptc", "scripts", "gothice", "gothicg", "gothici",
        "greekc", "greeks", "syastro", "symap", "symath", "symeteo", "symusic",
        "isocp", "isocp2", "isocp3", "isoct", "isoct2", "isoct3",
        "ltypeshp", "amgdt", "bigfont", "gdt",
    };
}
