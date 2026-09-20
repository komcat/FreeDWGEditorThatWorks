using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;

namespace FreeDWGEditorThatWorks.Rendering;

/// <summary>
/// Maps the font <em>file</em> a DWG names onto the family this machine
/// knows it by.
/// </summary>
/// <remarks>
/// A DWG text style carries a file name -- <c>arialn.ttf</c>, <c>times.ttf</c>,
/// <c>gothic.ttf</c> -- and WPF wants a family: "Arial Narrow", "Times New
/// Roman", "Century Gothic". Passing the file's stem and hoping resolved
/// three of the seventeen fonts in a folder of ordinary sample drawings.
/// The rest silently became whatever WPF falls back to, which is a drawing
/// rendered in the wrong typeface with nothing said about it.
/// <para>
/// The answer comes from the file itself, which is the only place it is
/// reliably written down. Both font folders are searched, because a font
/// installed for one user does not live in the Windows one.
/// </para>
/// </remarks>
public static class WpfFonts
{
    private static readonly Dictionary<string, string?> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Lazy<string[]> Folders = new(() => new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "Fonts"),
    }.Where(folder => folder.Length > 0 && Directory.Exists(folder)).ToArray());

    /// <summary>
    /// The family name for a font file, or null when this machine has not
    /// got it. Cached: a drawing asks about the same dozen styles for every
    /// one of its thousands of labels.
    /// </summary>
    public static string? FamilyOf(string file)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;

        lock (Cache)
        {
            if (Cache.TryGetValue(file, out string? known)) return known;

            string? family = Lookup(file);
            Cache[file] = family;
            return family;
        }
    }

    private static string? Lookup(string file)
    {
        foreach (string folder in Folders.Value)
        {
            string path = Path.Combine(folder, file);
            if (!File.Exists(path)) continue;

            if (FamilyIn(path) is { Length: > 0 } family) return family;
        }

        // Not a file we have. It may still be a family name outright, which
        // is how a style that was set up by name rather than by file reads.
        return Fonts.SystemFontFamilies
            .Select(candidate => candidate.Source)
            .FirstOrDefault(name => name.Equals(file, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FamilyIn(string path)
    {
        try
        {
            var typeface = new GlyphTypeface(new Uri(path));

            // Win32 names first: they are what the font is listed under, and
            // what WPF will match when the family is asked for by string.
            return typeface.Win32FamilyNames.Values.FirstOrDefault()
                ?? typeface.FamilyNames.Values.FirstOrDefault();
        }
        catch
        {
            // A font file that will not open is one we have not got, which
            // is a substitution rather than a failure to load a drawing.
            return null;
        }
    }
}
