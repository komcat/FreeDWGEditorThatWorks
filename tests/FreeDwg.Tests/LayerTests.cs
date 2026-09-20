using FreeDwg.Core.Commands;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Styling;

namespace FreeDwg.Tests;

/// <summary>
/// Making, deleting and editing layers, all of it undoable.
/// </summary>
/// <remarks>
/// The trap here is that an entity names its layer by <em>position</em> in
/// the table, so removing one from the middle leaves every entity past it
/// pointing at its neighbour -- in every layout, and inside every block
/// definition, and in the frozen-layer set of every viewport. That is a bug
/// which draws a perfectly ordinary drawing in the wrong colours, so it is
/// the thing most of these tests are about.
/// </remarks>
public sealed class LayerTests
{
    private static readonly Rgb Red = new(255, 0, 0);
    private static readonly Rgb Green = new(0, 200, 0);

    private static Drawing WithLayers(params string[] names)
    {
        var drawing = Drawing.CreateEmpty();
        foreach (string name in names) drawing.AddLayer(new Layer(name) { Color = Red });

        return drawing;
    }

    /// <summary>A line on a layer, drawn in that layer's style as the editor draws one.</summary>
    private static SLine On(Drawing drawing, int layer)
    {
        var line = new SLine(new Vec2(0, layer * 10), new Vec2(10, layer * 10))
        {
            LayerIndex = layer,
            Style = drawing.Layers[layer].Style,
        };

        drawing.Add(line);
        return line;
    }

    // ---- adding ------------------------------------------------------------

    [Fact]
    public void ANewLayerGoesOnTheEndAndComesOffAgainOnUndo()
    {
        var drawing = Drawing.CreateEmpty();
        var stack = new CommandStack(drawing);

        var add = new AddLayer(new Layer("Dimensions") { Color = Green });
        stack.Do(add);

        Assert.Equal(2, drawing.Layers.Count);
        Assert.Equal(1, add.Index);
        Assert.Equal("New layer Dimensions", stack.UndoName);

        stack.Undo();
        Assert.Single(drawing.Layers);
    }

    // ---- deleting ----------------------------------------------------------

    [Fact]
    public void LayerZeroCannotBeDeleted()
    {
        var drawing = WithLayers("Walls");

        // Every DWG has it, and a drawing without it is not one.
        Assert.Throws<ArgumentException>(() => new DeleteLayer(drawing, 0));
    }

    [Fact]
    public void ALayerWithGeometryOnItCannotBeDeleted()
    {
        var drawing = WithLayers("Walls");
        On(drawing, 1);

        Assert.Throws<InvalidOperationException>(() => new DeleteLayer(drawing, 1));
    }

    [Fact]
    public void GeometryInsideABlockCountsAsUsingALayerToo()
    {
        var drawing = WithLayers("Symbols");

        var block = drawing.AddBlock(new BlockDefinition("TICK"));
        block.Entities.Add(new SLine(Vec2.Zero, new Vec2(1, 0)) { LayerIndex = 1 });

        // Nothing in the layout is on it, and deleting it anyway would leave
        // the block's own line work pointing past the end of the table.
        Assert.True(LayerTable.IsInUse(drawing, 1));
        Assert.Throws<InvalidOperationException>(() => new DeleteLayer(drawing, 1));
    }

    [Fact]
    public void DeletingALayerRenumbersEverythingAboveIt()
    {
        var drawing = WithLayers("Hidden", "Walls", "Text");

        // Nothing on layer 1, and a line on each of the two above it.
        var walls = On(drawing, 2);
        var text = On(drawing, 3);

        var stack = new CommandStack(drawing);
        stack.Do(new DeleteLayer(drawing, 1));

        Assert.Equal(3, drawing.Layers.Count);
        Assert.Equal("Walls", drawing.Layers[1].Name);

        // The whole point: the lines still name the layers they are on.
        Assert.Equal(1, walls.LayerIndex);
        Assert.Equal(2, text.LayerIndex);
        Assert.Equal("Walls", drawing.Layers[walls.LayerIndex].Name);
        Assert.Equal("Text", drawing.Layers[text.LayerIndex].Name);
    }

    [Fact]
    public void UndoingADeleteRenumbersBack()
    {
        var drawing = WithLayers("Hidden", "Walls");
        var walls = On(drawing, 2);

        var stack = new CommandStack(drawing);
        stack.Do(new DeleteLayer(drawing, 1));
        stack.Undo();

        Assert.Equal(3, drawing.Layers.Count);
        Assert.Equal("Hidden", drawing.Layers[1].Name);
        Assert.Equal(2, walls.LayerIndex);
    }

    [Fact]
    public void DeletingALayerRenumbersABlocksContentsAndAViewportsFreezeSet()
    {
        var drawing = WithLayers("Hidden", "Walls");

        var block = drawing.AddBlock(new BlockDefinition("TICK"));
        var inside = new SLine(Vec2.Zero, new Vec2(1, 0)) { LayerIndex = 2 };
        block.Entities.Add(inside);

        var sheet = drawing.AddLayout(new Layout("Sheet", isPaperSpace: true));
        var viewport = new SViewport(drawing.ModelSpace, Bounds2.FromCorners(Vec2.Zero, new Vec2(100, 60)),
            Vec2.Zero, viewHeight: 60)
        {
            FrozenLayers = new HashSet<int> { 2 },
        };
        sheet.Add(viewport);

        new CommandStack(drawing).Do(new DeleteLayer(drawing, 1));

        Assert.Equal(1, inside.LayerIndex);

        // A freeze left pointing at the neighbour hides the wrong geometry in
        // one viewport only, which is the hardest kind of wrong to notice.
        Assert.Equal([1], viewport.FrozenLayers!.Order());
    }

    [Fact]
    public void DeletingALayerBelowTheCurrentOneKeepsTheCurrentOne()
    {
        var drawing = WithLayers("Hidden", "Walls");
        drawing.CurrentLayerIndex = 2;

        new CommandStack(drawing).Do(new DeleteLayer(drawing, 1));

        Assert.Equal(1, drawing.CurrentLayerIndex);
        Assert.Equal("Walls", drawing.CurrentLayer!.Name);
    }

    // ---- editing -----------------------------------------------------------

    [Fact]
    public void RecolouringALayerRecoloursTheGeometryThatWasFollowingIt()
    {
        var drawing = WithLayers("Walls");
        var line = On(drawing, 1);

        var stack = new CommandStack(drawing);
        stack.Do(new ChangeLayer(drawing, 1, LayerProperties.Of(drawing.Layers[1]) with { Color = Green }));

        // ByLayer is resolved away at import, so an entity holds the colour
        // rather than a reference to it. If the command did not go and change
        // them, the panel swatch and the drawing would disagree.
        Assert.Equal(Green, drawing.Layers[1].Color);
        Assert.Equal(Green, line.Style.Color);

        stack.Undo();
        Assert.Equal(Red, line.Style.Color);
    }

    [Fact]
    public void AnObjectWithAColourOfItsOwnKeepsIt()
    {
        var drawing = WithLayers("Walls");

        var following = On(drawing, 1);
        var overridden = On(drawing, 1);
        overridden.Style = overridden.Style with { Color = new Rgb(0, 0, 255) };

        new CommandStack(drawing).Do(
            new ChangeLayer(drawing, 1, LayerProperties.Of(drawing.Layers[1]) with { Color = Green }));

        Assert.Equal(Green, following.Style.Color);
        Assert.Equal(new Rgb(0, 0, 255), overridden.Style.Color);
    }

    [Fact]
    public void RenamingALayerChangesNothingElse()
    {
        var drawing = WithLayers("Walls");
        var line = On(drawing, 1);

        var stack = new CommandStack(drawing);
        stack.Do(new ChangeLayer(drawing, 1, LayerProperties.Of(drawing.Layers[1]) with { Name = "Partitions" }));

        Assert.Equal("Partitions", drawing.Layers[1].Name);
        Assert.Equal(Red, line.Style.Color);
        Assert.Equal("Rename layer Walls", stack.UndoName);

        stack.Undo();
        Assert.Equal("Walls", drawing.Layers[1].Name);
    }

    [Fact]
    public void TurningALayerOffThroughTheCommandStackIsUndoable()
    {
        var drawing = WithLayers("Walls");

        var stack = new CommandStack(drawing);
        stack.Do(new ChangeLayer(drawing, 1, LayerProperties.Of(drawing.Layers[1]) with { IsOn = false }));

        Assert.False(drawing.Layers[1].IsVisible);

        stack.Undo();
        Assert.True(drawing.Layers[1].IsVisible);
    }

    // ---- what a save would have to write -----------------------------------

    [Fact]
    public void TheChangeLogReportsLayersAsWellAsGeometry()
    {
        var drawing = WithLayers("Walls");

        // A layer that came from a file, so it has a handle to report.
        var fromFile = drawing.Layers[1];
        var stack = new CommandStack(drawing);

        var made = new Layer("Dimensions") { Color = Green };
        stack.Do(new AddLayer(made));
        stack.Do(new ChangeLayer(drawing, 1, LayerProperties.Of(fromFile) with { Color = Green }));

        var log = stack.Summarize();

        Assert.Contains(made, log.CreatedLayers);
        Assert.False(log.IsEmpty);

        // Undone, and the log stops claiming it: it is replayed from the
        // stack rather than tracked as edits happen.
        stack.Undo();
        stack.Undo();
        Assert.True(stack.Summarize().IsEmpty);
    }

    [Fact]
    public void ACompositeIsOneStepAndComesApartBackwards()
    {
        var drawing = Drawing.CreateEmpty();
        var stack = new CommandStack(drawing);

        var layer = new Layer("Dimensions") { Color = Green };
        var line = new SLine(Vec2.Zero, new Vec2(10, 0)) { LayerIndex = 1 };

        stack.Do(new Composite("Draw dimension",
            new AddLayer(layer),
            new AddEntities(drawing.ActiveLayout, line)));

        Assert.Equal(2, drawing.Layers.Count);
        Assert.Single(drawing.Entities);
        Assert.Equal("Draw dimension", stack.UndoName);

        // One press, and the layer goes with it. The entity has to come out
        // first, or the layer is still in use when its own undo runs.
        stack.Undo();

        Assert.Single(drawing.Layers);
        Assert.Empty(drawing.Entities);
    }

    // ---- naming ------------------------------------------------------------

    [Fact]
    public void ANameAlreadyTakenGetsANumber()
    {
        var drawing = WithLayers("Walls", "Walls 1");

        Assert.Equal("Walls 2", LayerTable.UniqueName(drawing, "Walls"));
        Assert.Equal("Doors", LayerTable.UniqueName(drawing, "Doors"));
    }

    [Fact]
    public void LayersAreFoundByNameWithoutRegardToCase()
    {
        var drawing = WithLayers("Dimensions");

        Assert.Equal(1, LayerTable.IndexOf(drawing, "DIMENSIONS"));
        Assert.Equal(-1, LayerTable.IndexOf(drawing, "Doors"));
    }
}
