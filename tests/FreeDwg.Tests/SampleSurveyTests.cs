using System.IO;
using FreeDwg.Core.Commands;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Commands;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Interop.Acad;
using Xunit.Abstractions;

namespace FreeDwg.Tests;

/// <summary>
/// Opens a folder of real drawings and reports what the importer made of
/// them. Opt in with <c>FREEDWG_SAMPLES=&lt;dir&gt;</c>.
/// </summary>
/// <remarks>
/// Not an assertion of anything: a survey. It exists because three separate
/// import bugs -- the unit, the text height and the font -- were invisible
/// to every fixture in this suite and obvious within a minute of opening
/// fourteen ordinary files and printing what came back. A fixture written
/// here only ever contains what was thought of; a folder of other people's
/// drawings contains what there is.
/// <para>
/// Run it with <c>dotnet test --filter Survey -l "console;verbosity=detailed"</c>.
/// </para>
/// </remarks>
public sealed class SampleSurveyTests
{
    private readonly ITestOutputHelper _out;

    public SampleSurveyTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Survey()
    {
        string? dir = Environment.GetEnvironmentVariable("FREEDWG_SAMPLES");
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            _out.WriteLine("Set FREEDWG_SAMPLES to a folder of .dwg files to run this.");
            return;
        }

        // The shell installs this at startup; a survey has no startup, and
        // without it every TrueType font reads as unavailable.
        FontResolver.ResolveFile = FreeDWGEditorThatWorks.Rendering.WpfFonts.FamilyOf;
        FreeDwg.Core.Rendering.TextMetrics.Measure =
            FreeDWGEditorThatWorks.Rendering.WpfText.MeasureWidth;

        var missing = new SortedDictionary<string, int>();

        foreach (string path in Directory.GetFiles(dir, "*.dwg").OrderBy(p => p))
        {
            string name = Path.GetFileName(path);

            try
            {
                var diagnostics = new ImportDiagnostics();
                var drawing = DwgLoader.Load(path, diagnostics);

                foreach (var (kind, count) in diagnostics.UnsupportedEntities)
                    missing[kind] = missing.GetValueOrDefault(kind) + count;

                var texts = drawing.Entities.OfType<SText>().ToList();
                var fonts = texts.Select(t => t.FontFamily ?? "?").Distinct().OrderBy(f => f);

                _out.WriteLine($"== {name}");
                _out.WriteLine($"   {drawing.Units}   extents {drawing.Bounds.Width:0.#} x {drawing.Bounds.Height:0.#}"
                             + $"   entities={drawing.Entities.Count} blocks={drawing.Blocks.Count} layers={drawing.Layers.Count}");
                _out.WriteLine($"   text={texts.Count} in [{string.Join(", ", fonts)}]");
                _out.WriteLine($"   {diagnostics.Summary()}");

                foreach (string message in diagnostics.Messages) _out.WriteLine($"   ! {message}");
            }
            catch (Exception e)
            {
                _out.WriteLine($"== {name}  FAILED {e.GetType().Name}: {e.Message}");
            }
        }

        // The backlog, ranked by what these files actually contain rather
        // than by what seems important.
        _out.WriteLine("");
        _out.WriteLine("Unsupported across the folder, commonest first:");
        foreach (var (kind, count) in missing.OrderByDescending(pair => pair.Value))
            _out.WriteLine($"   {kind,-14} {count}");
    }

    /// <summary>
    /// Saves each drawing twice -- once untouched, once with everything in
    /// model space moved -- reads both back, and reports what differs. The
    /// samples themselves are never written: both copies go to a temporary
    /// folder.
    /// </summary>
    /// <remarks>
    /// A writer meets everything a reader does and more, since it also has to
    /// carry what the reader skipped. Fixtures cannot find what they do not
    /// contain, so this is how the writer meets real files.
    /// </remarks>
    [Fact]
    public void SaveSurvey()
    {
        string? dir = Environment.GetEnvironmentVariable("FREEDWG_SAMPLES");
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            _out.WriteLine("Set FREEDWG_SAMPLES to a folder of .dwg files to run this.");
            return;
        }

        string scratch = Path.Combine(Path.GetTempPath(), "FreeDwg.SaveSurvey");
        Directory.CreateDirectory(scratch);
        var shift = new Vec2(10, 20);

        foreach (string path in Directory.GetFiles(dir, "*.dwg").OrderBy(p => p))
        {
            string name = Path.GetFileName(path);
            _out.WriteLine($"== {name}");

            try
            {
                // Untouched: the same scene should come back.
                var session = DwgSession.Open(path);
                var stack = new CommandStack(session.Drawing);
                int count = session.Drawing.ModelSpace.Entities.Count;
                var bounds = session.Drawing.ModelSpace.Bounds;

                string plain = Path.Combine(scratch, "plain-" + name);
                var report = session.Save(stack, plain);
                var back = DwgLoader.Load(plain);
                _out.WriteLine($"   untouched  {report.Summary()}");
                Report("untouched", count, bounds, back);

                // Everything moved: a write to every entity at once.
                var layout = session.Drawing.ModelSpace;
                stack.Do(new TransformEntities(layout, layout.Entities.ToList(), Mat3.Translation(shift), "Move"));

                string moved = Path.Combine(scratch, "moved-" + name);
                report = session.Save(stack, moved);
                back = DwgLoader.Load(moved);
                _out.WriteLine($"   moved      {report.Summary()}");
                foreach (string note in report.Notes.Take(8)) _out.WriteLine($"     ! {note}");
                Report("moved", count, Bounds2.FromCorners(bounds.Min + shift, bounds.Max + shift), back);
            }
            catch (Exception e)
            {
                _out.WriteLine($"   FAILED {e.GetType().Name}: {e.Message}");
            }
        }

        void Report(string what, int count, Bounds2 bounds, Drawing back)
        {
            var got = back.ModelSpace.Bounds;
            double drift = Math.Max(Vec2.Distance(bounds.Min, got.Min), Vec2.Distance(bounds.Max, got.Max));
            double size = Math.Max(1, Math.Max(bounds.Width, bounds.Height));
            string verdict = back.ModelSpace.Entities.Count == count && drift <= size * 1e-6 ? "ok" : "DIFFERS";

            _out.WriteLine($"   {what,-10} {verdict}: entities {count} -> {back.ModelSpace.Entities.Count}, "
                         + $"extents off by {drift:0.######}");
        }
    }
}
