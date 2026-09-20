using System.IO;
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
}
