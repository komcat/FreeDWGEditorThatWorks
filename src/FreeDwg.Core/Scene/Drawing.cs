using FreeDwg.Core.Geometry;

namespace FreeDwg.Core.Scene;

/// <summary>
/// A loaded drawing, in the shape the editor wants: layouts holding flat
/// entity lists, plus the tables they share. Deliberately not the file's
/// object graph.
/// </summary>
public sealed class Drawing
{
    public Drawing()
    {
        ModelSpace = new Layout("Model", isPaperSpace: false);
        Layouts.Add(ModelSpace);
        ActiveLayout = ModelSpace;
    }

    public List<Layer> Layers { get; } = new();

    /// <summary>Block definitions, referenced by <see cref="Entities.SInsert"/>.</summary>
    public List<BlockDefinition> Blocks { get; } = new();

    /// <summary>Dash patterns referenced by entity and layer styles.</summary>
    public List<Styling.Linetype> Linetypes { get; } = new();

    /// <summary>Model space first, then the paper space sheets in tab order.</summary>
    public List<Layout> Layouts { get; } = new();

    public Layout ModelSpace { get; }

    /// <summary>
    /// The layout currently being viewed. Layers, blocks and linetypes are
    /// shared across all of them, so switching sheets is just this.
    /// </summary>
    public Layout ActiveLayout { get; set; }

    /// <summary>Path this was imported from, if any.</summary>
    public string? SourcePath { get; set; }

    /// <summary>Entities of the active layout.</summary>
    public List<SceneEntity> Entities => ActiveLayout.Entities;

    /// <summary>Bounds of the active layout.</summary>
    public Bounds2 Bounds => ActiveLayout.Bounds;

    /// <summary>Adds a layer and returns its index.</summary>
    public int AddLayer(Layer layer)
    {
        Layers.Add(layer);
        return Layers.Count - 1;
    }

    public BlockDefinition AddBlock(BlockDefinition block)
    {
        Blocks.Add(block);
        return block;
    }

    public Layout AddLayout(Layout layout)
    {
        Layouts.Add(layout);
        return layout;
    }

    public void Add(SceneEntity entity) => ActiveLayout.Add(entity);
}
