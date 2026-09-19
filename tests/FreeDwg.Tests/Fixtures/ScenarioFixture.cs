using FreeDwg.Core.Rendering;
using FreeDwg.Interop.Acad;
using FreeDwg.Tests.Rendering;
using SceneDrawing = FreeDwg.Core.Scene.Drawing;

namespace FreeDwg.Tests.Fixtures;

/// <summary>
/// Writes a fixture drawing, reads it back, and renders it once.
/// </summary>
/// <remarks>
/// Used as an xunit class fixture so the whole write-read-render cycle runs
/// once per scenario while each probe stays its own test case, reported and
/// diagnosed separately.
/// </remarks>
public abstract class ScenarioFixture
{
    protected ScenarioFixture(string name, Action<string> write, Action<RenderSettings>? configure = null)
    {
        Name = name;
        Path = FixtureFiles.Ensure(name, write);

        Diagnostics = new ImportDiagnostics();
        Drawing = DwgLoader.Load(Path, Diagnostics);

        Frame = StaRenderer.Render(Drawing, configure: configure, saveAs: name);
        Probe = new Probe(Frame);
    }

    public string Name { get; }
    public string Path { get; }
    public SceneDrawing Drawing { get; }
    public ImportDiagnostics Diagnostics { get; }
    public RenderResult Frame { get; }
    public Probe Probe { get; }
}

public sealed class PrimitivesFixture : ScenarioFixture
{
    public PrimitivesFixture() : base("primitives", PrimitivesDrawing.Write) { }
}

public sealed class TextFixture : ScenarioFixture
{
    public TextFixture() : base("text", TextDrawing.Write) { }
}

public sealed class CurvesFixture : ScenarioFixture
{
    public CurvesFixture() : base("curves", CurvesDrawing.Write) { }
}

/// <summary>
/// Blocks, rendered twice: once as authored and once with the layer the ring
/// lives on switched off, to prove layer visibility reaches inside a block
/// definition.
/// </summary>
public sealed class BlocksFixture : ScenarioFixture
{
    public BlocksFixture() : base("blocks", BlocksDrawing.Write)
    {
        var rings = Drawing.Layers.First(layer => layer.Name == "RINGS");

        rings.IsOn = false;
        RingsOff = new Probe(StaRenderer.Render(Drawing, saveAs: "blocks_rings_off"));
        rings.IsOn = true;
    }

    public Probe RingsOff { get; }
}

/// <summary>
/// A sheet and the model space it looks at, rendered three ways: model space,
/// the sheet, and the sheet with a layer switched off to check that the
/// change reaches through the viewport.
/// </summary>
public sealed class LayoutFixture : ScenarioFixture
{
    public LayoutFixture() : base("layout", LayoutDrawing.Write)
    {
        // The base render is model space, since that is the active layout.
        Model = Probe;

        Sheet = Drawing.Layouts.FirstOrDefault(layout => layout.IsPaperSpace);
        if (Sheet is null) return;

        Drawing.ActiveLayout = Sheet;
        Paper = new Probe(StaRenderer.Render(Drawing, saveAs: "layout_paper"));

        var frozen = Drawing.Layers.First(layer => layer.Name == "VPFROZEN");
        frozen.IsOn = false;
        PaperLayerOff = new Probe(StaRenderer.Render(Drawing, saveAs: "layout_paper_layer_off"));
        frozen.IsOn = true;

        Drawing.ActiveLayout = Drawing.ModelSpace;
    }

    public Probe Model { get; }
    public Core.Scene.Layout? Sheet { get; }
    public Probe? Paper { get; }
    public Probe? PaperLayerOff { get; }
}
