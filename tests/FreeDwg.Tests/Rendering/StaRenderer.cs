using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Styling;
using FreeDWGEditorThatWorks.Controls;
using SceneDrawing = FreeDwg.Core.Scene.Drawing;

namespace FreeDwg.Tests.Rendering;

/// <summary>
/// Renders a scene through the real canvas and sink, off screen.
/// </summary>
/// <remarks>
/// This drives production code rather than a test double on purpose. Three of
/// the bugs this suite has caught -- inverted arc winding, a mirrored block
/// flipping it back, and dimensions losing their arrowheads -- were invisible
/// to the model and only showed up in pixels.
/// </remarks>
public static class StaRenderer
{
    public const int DefaultWidth = 900;
    public const int DefaultHeight = 600;

    /// <summary>Renders and returns the pixels, having done all WPF work on an STA thread.</summary>
    public static RenderResult Render(
        SceneDrawing drawing,
        int width = DefaultWidth,
        int height = DefaultHeight,
        Action<RenderSettings>? configure = null,
        string? saveAs = null,
        Action<CadCanvas>? prepare = null)
    {
        return OnSta(() => RenderCore(drawing, width, height, configure, saveAs, prepare));
    }

    private static RenderResult RenderCore(
        SceneDrawing drawing, int width, int height, Action<RenderSettings>? configure, string? saveAs,
        Action<CadCanvas>? prepare)
    {
        // The shell installs this at startup; a test host has no startup.
        TextMetrics.Measure = FreeDWGEditorThatWorks.Rendering.WpfText.MeasureWidth;

        var canvas = new CadCanvas { Width = width, Height = height };
        configure?.Invoke(canvas.Settings);

        canvas.Drawing = drawing;
        canvas.Measure(new Size(width, height));
        canvas.Arrange(new Rect(0, 0, width, height));
        canvas.ZoomExtents();

        // After the drawing is set, which clears any selection, and before the
        // frame is taken: this is where a test selects something.
        prepare?.Invoke(canvas);

        canvas.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas);

        var pixels = new byte[width * 4 * height];
        bitmap.CopyPixels(pixels, width * 4, 0);

        if (saveAs is not null) Save(bitmap, saveAs);

        return new RenderResult
        {
            Pixels = pixels,
            Width = width,
            Height = height,
            CameraCenter = canvas.Camera.Center,
            CameraScale = canvas.Camera.Scale,
            Background = canvas.Settings.Background,
            Stats = canvas.LastStats,
        };
    }

    /// <summary>
    /// Writes the frame to disk for eyeballing when a probe fails. Off by
    /// default: set FREEDWG_TEST_RENDERS to a directory to collect them.
    /// </summary>
    public static string? OutputDirectory => Environment.GetEnvironmentVariable("FREEDWG_TEST_RENDERS");

    private static void Save(RenderTargetBitmap bitmap, string name)
    {
        string? directory = OutputDirectory;
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }

    /// <summary>
    /// Runs a delegate on a fresh STA thread. WPF visuals have thread affinity
    /// and the xunit host thread is MTA, so every render needs one of these.
    /// Public because anything building WPF objects in a test needs it, not
    /// just scene renders.
    /// </summary>
    public static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { failure = ex; }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (failure is not null)
            throw new InvalidOperationException("Off-screen render failed.", failure);

        return result;
    }
}
