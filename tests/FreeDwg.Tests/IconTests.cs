using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeDwg.Tests.Rendering;

namespace FreeDwg.Tests;

/// <summary>
/// The toolbar icons are drawn from path data rather than imported, so a typo
/// in a geometry is a silently blank button. These load the real dictionary
/// and check that every icon has ink, sits inside its 24x24 grid, and is
/// distinguishable from its neighbours.
/// </summary>
/// <remarks>
/// Set FREEDWG_TEST_RENDERS to a directory and this also writes
/// <c>icons.png</c>, a labelled contact sheet of the whole set. That is the
/// quickest way to see that an icon parses but looks like a scribble.
/// </remarks>
public sealed class IconTests
{
    private const double Grid = 24;

    /// <summary>
    /// The icons, by resource key, parsed from the shell's own dictionary.
    /// </summary>
    /// <remarks>
    /// Parsed from the file the build copies next to this assembly rather
    /// than read back through a pack URI, which needs a WPF Application the
    /// test host has no business creating. It is the same XAML either way,
    /// and the failure being guarded against -- a path string that does not
    /// convert to a Geometry -- happens at parse time in both.
    /// </remarks>
    private static Dictionary<string, Geometry> Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Icons.xaml");
        Assert.True(File.Exists(path), $"the icon dictionary was not copied to {path}");

        using var stream = File.OpenRead(path);
        var dictionary = (ResourceDictionary)System.Windows.Markup.XamlReader.Load(stream);

        var icons = new Dictionary<string, Geometry>();
        foreach (var key in dictionary.Keys)
        {
            if (dictionary[key] is not Geometry geometry) continue;

            // Frozen so the contact sheet can draw them from another thread.
            geometry.Freeze();
            icons.Add((string)key, geometry);
        }

        return icons;
    }

    [Fact]
    public void EveryIconIsDrawnAndFitsItsGrid()
    {
        var icons = StaRenderer.OnSta(Load);

        Assert.NotEmpty(icons);

        foreach (var (key, geometry) in icons)
        {
            // Stroked, never filled, so the bounds to check are the stroke's.
            var bounds = geometry.Bounds;

            Assert.False(bounds.IsEmpty, $"{key} has no geometry at all");
            Assert.True(bounds.Width > 4 && bounds.Height > 4,
                $"{key} is {bounds.Width:0.#}x{bounds.Height:0.#}, too small to read");

            Assert.True(bounds.Left >= -1 && bounds.Top >= -1
                     && bounds.Right <= Grid + 1 && bounds.Bottom <= Grid + 1,
                $"{key} escapes the {Grid}x{Grid} grid: {bounds}");
        }
    }

    [Fact]
    public void TheToolbarsFindEveryIconTheyAskFor()
    {
        var icons = StaRenderer.OnSta(Load);

        // A StaticResource that resolves to nothing is a XAML load failure at
        // window construction -- the app simply will not start, and no other
        // test here builds a window to find out. So read the references back
        // out of the window's own markup rather than keeping a list in sync
        // with it by hand.
        string markup = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));

        var referenced = Regex.Matches(markup, @"\{StaticResource (Icon\.[A-Za-z]+)\}")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.True(referenced.Count > 20,
            $"only found {referenced.Count} icon references; has the toolbar markup moved?");

        var missing = referenced.Where(key => !icons.ContainsKey(key)).ToList();
        Assert.True(missing.Count == 0, $"the toolbars ask for icons that do not exist: {string.Join(", ", missing)}");
    }

    [Fact]
    public void EveryIconEarnsItsPlaceOnAToolbar()
    {
        var icons = StaRenderer.OnSta(Load);
        string markup = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));

        // The other direction: an icon nobody uses is dead weight, and
        // usually means a button was dropped without its geometry.
        var unused = icons.Keys.Where(key => !markup.Contains($"{{StaticResource {key}}}")).ToList();

        Assert.True(unused.Count == 0, $"icons drawn but never shown: {string.Join(", ", unused)}");
    }

    [Fact]
    public void NoTwoIconsAreTheSameShape()
    {
        var icons = StaRenderer.OnSta(Load);

        // Copy-paste is how an icon set ends up with two identical buttons.
        var byShape = new Dictionary<string, string>();

        foreach (var (key, geometry) in icons)
        {
            string shape = geometry.GetFlattenedPathGeometry()
                .ToString(CultureInfo.InvariantCulture);

            Assert.False(byShape.TryGetValue(shape, out string? other),
                $"{key} and {other} are the same shape");

            byShape[shape] = key;
        }
    }

    [Fact]
    public void ContactSheet()
    {
        string? directory = StaRenderer.OutputDirectory;
        if (string.IsNullOrWhiteSpace(directory)) return;   // opt in, like the frame dumps

        StaRenderer.OnSta(() =>
        {
            var icons = Load();
            Save(RenderSheet(icons), directory, "icons");
            return 0;
        });
    }

    /// <summary>Lays the whole set out on a dark sheet with its keys under it.</summary>
    private static RenderTargetBitmap RenderSheet(Dictionary<string, Geometry> icons)
    {
        const int columns = 8;
        const int cell = 110;
        const double scale = 2.0;

        int rows = (icons.Count + columns - 1) / columns;
        int width = columns * cell;
        int height = rows * cell;

        var ink = new SolidColorBrush(Color.FromRgb(228, 234, 240));
        var pen = new Pen(ink, 1.4 * scale)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(33, 40, 48)), null,
                new Rect(0, 0, width, height));

            int index = 0;
            foreach (var (key, geometry) in icons.OrderBy(i => i.Key, StringComparer.Ordinal))
            {
                double x = index % columns * cell;
                double y = index / columns * cell;
                index++;

                dc.PushTransform(new TranslateTransform(
                    x + (cell - Grid * scale) / 2, y + 18));
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.DrawGeometry(null, pen, geometry);
                dc.Pop();
                dc.Pop();

                var label = new FormattedText(key.Replace("Icon.", ""),
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 12, ink, 1.0);

                dc.DrawText(label, new Point(x + (cell - label.Width) / 2, y + cell - 24));
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static void Save(RenderTargetBitmap bitmap, string directory, string name)
    {
        Directory.CreateDirectory(directory);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }
}
