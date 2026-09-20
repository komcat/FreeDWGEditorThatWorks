using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeDwg.Core.Styling;
using FreeDwg.Tests.Rendering;
using FreeDWGEditorThatWorks.ViewModels;
using FreeDWGEditorThatWorks.Views;

namespace FreeDwg.Tests;

/// <summary>
/// The colour picker dialog.
/// </summary>
/// <remarks>
/// Nothing else in the suite builds this window, and it only opens when
/// someone reaches the last entry of the colour list. A broken binding or a
/// mistyped template in it would therefore lie quiet until a user found it,
/// which is the same gap <c>StartupTests</c> exists to close for the main
/// window. Constructing it here is the cheap half of that.
/// <para>
/// Set FREEDWG_TEST_RENDERS to a directory and it also writes
/// <c>colour_picker.png</c>, which is how the layout gets looked at.
/// </para>
/// </remarks>
public sealed class ColourPickerTests
{
    [Fact]
    public void ThePickerBuildsAndStartsOnTheColourItWasGiven()
    {
        var chosen = StaRenderer.OnSta(() =>
        {
            var picker = new ColourPickerWindow(new Rgb(18, 52, 86));
            return picker.Chosen;
        });

        // It opens on the colour being changed, so cancelling and accepting
        // without touching anything both mean the same thing.
        Assert.Equal(new Rgb(18, 52, 86), chosen);
    }

    [Fact]
    public void ThePickerLaysOutAtASensibleSize()
    {
        var size = StaRenderer.OnSta(() =>
        {
            var content = Measure(new ColourPickerWindow(Rgb.White));
            return (content.DesiredSize.Width, content.DesiredSize.Height);
        });

        // Twelve columns of swatches plus the hex row: wide enough to be a
        // palette and small enough to be a dialog.
        Assert.InRange(size.Width, 280, 560);
        Assert.InRange(size.Height, 180, 420);
    }

    [Fact]
    public void Screenshot()
    {
        string? directory = StaRenderer.OutputDirectory;
        if (string.IsNullOrWhiteSpace(directory)) return;   // opt in, like the other dumps

        StaRenderer.OnSta(() =>
        {
            var content = Measure(new ColourPickerWindow(new Rgb(18, 52, 86)));

            int width = (int)Math.Ceiling(content.DesiredSize.Width);
            int height = (int)Math.Ceiling(content.DesiredSize.Height);

            content.Arrange(new Rect(0, 0, width, height));
            content.UpdateLayout();

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);

            Directory.CreateDirectory(directory);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var stream = File.Create(Path.Combine(directory, "colour_picker.png"));
            encoder.Save(stream);
            return 0;
        });
    }

    /// <summary>
    /// Lays out the window's content without ever showing a window: a test
    /// that opened one would put it on the desk of whoever ran the suite.
    /// </summary>
    private static FrameworkElement Measure(ColourPickerWindow picker)
    {
        var content = (FrameworkElement)picker.Content;

        // A panel has no background of its own, and a transparent render
        // would make the swatches impossible to judge.
        if (content is Panel panel) panel.Background = Brushes.White;

        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return content;
    }
}
