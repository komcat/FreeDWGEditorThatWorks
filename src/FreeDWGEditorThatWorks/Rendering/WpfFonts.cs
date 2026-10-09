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

    private static readonly Lazy<Dictionary<string, string>> FilesByFamily = new(() =>
    {
        // Upright, normal-weight faces first, so "Arial" finds arial.ttf
        // rather than whichever of its bold or italic siblings sorts first.
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var candidates = Folders.Value
            .SelectMany(folder => Directory.EnumerateFiles(folder))
            .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".ttf" or ".otf" or ".ttc");

        foreach (var (path, family, regular) in candidates.Select(Describe).Where(d => d.Family is not null)
                     .OrderByDescending(d => d.Regular))
        {
            map.TryAdd(family!, Path.GetFileName(path));
        }

        return map;

        static (string Path, string? Family, bool Regular) Describe(string path)
        {
            try
            {
                var typeface = new GlyphTypeface(new Uri(path));
                bool regular = typeface.Weight == System.Windows.FontWeights.Normal &&
                               typeface.Style == System.Windows.FontStyles.Normal;
                return (path, typeface.Win32FamilyNames.Values.FirstOrDefault()
                              ?? typeface.FamilyNames.Values.FirstOrDefault(), regular);
            }
            catch
            {
                return (path, null, false);
            }
        }
    });

    /// <summary>
    /// The file a family is installed as -- "Arial" to arial.ttf -- or null.
    /// The inverse of <see cref="FamilyOf"/>, for a text style written here:
    /// a DWG names a file, so a style has to have one.
    /// </summary>
    /// <remarks>
    /// Built once, by opening every font file there is; that is a second or
    /// so, paid at the first save that needs it rather than at start-up.
    /// </remarks>
    public static string? FileOf(string family) =>
        FilesByFamily.Value.TryGetValue(family, out string? file) ? file : null;

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
