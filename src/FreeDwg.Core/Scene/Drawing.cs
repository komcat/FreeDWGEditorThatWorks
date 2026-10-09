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

    /// <summary>Path this was imported from, if any. Null for a new drawing.</summary>
    public string? SourcePath { get; set; }

    /// <summary>
    /// What one drawing unit means. Millimetres unless a file says otherwise.
    /// </summary>
    /// <remarks>
    /// Metadata, not a scale: the coordinates are bare numbers and this says
    /// what they count, which is how DWG's INSUNITS works. Changing it
    /// re-labels the drawing rather than resizing it.
    /// </remarks>
    public DrawingUnits Units { get; set; } = DrawingUnits.Millimetres;

    /// <summary>Decimal places shown for a length.</summary>
    public int LinearPrecision { get; set; } = 3;

    /// <summary>
    /// What new dimensions are drawn with, if the drawing says. Null means
    /// ISO sizes in this drawing's units, which follow a change of unit;
    /// anything set here -- from the file's DIM variables, or the dimension
    /// style dialog -- is taken as it is. Changed through
    /// <c>ChangeDimensionSettings</c>, so it is undoable and it is saved.
    /// </summary>
    public DimensionSettings? Dimensions { get; set; }

    /// <summary>
    /// The layer new entities are created on. Index into <see cref="Layers"/>;
    /// out of range reads as layer 0, which is the one every DWG has.
    /// </summary>
    public int CurrentLayerIndex { get; set; }

    public Layer? CurrentLayer =>
        (uint)CurrentLayerIndex < (uint)Layers.Count ? Layers[CurrentLayerIndex] : Layers.FirstOrDefault();

    /// <summary>
    /// An empty drawing with the layer every DWG is required to have.
    /// </summary>
    public static Drawing CreateEmpty()
    {
        var drawing = new Drawing();
        drawing.AddLayer(new Layer("0") { Color = Styling.Rgb.White });
        return drawing;
    }

    /// <summary>Stamps a new entity with the current layer and its style.</summary>
    public T Place<T>(T entity) where T : SceneEntity
    {
        entity.LayerIndex = CurrentLayerIndex;
        entity.Style = CurrentLayer?.Style ?? Styling.DisplayStyle.Default;
        return entity;
    }

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
