using FreeDwg.Core.Scene;

namespace FreeDwg.Core.Rendering;

/// <summary>
/// What an entity needs while emitting itself: where to draw, which layers are
/// visible, and how deep inside nested blocks we are.
/// </summary>
/// <remarks>
/// Layers travel with the context because block definitions contain entities
/// on their own layers -- freezing a layer has to hide geometry nested inside
/// a block instance, not just top-level geometry.
/// </remarks>
public readonly record struct EmitContext(IDrawingSink Sink, IReadOnlyList<Layer> Layers, int Depth)
{
    /// <summary>
    /// Depth cap. A block that references itself is invalid but does occur in
    /// damaged files, and without a cap it is an immediate stack overflow.
    /// </summary>
    public const int MaxDepth = 32;

    public EmitContext(IDrawingSink sink, IReadOnlyList<Layer> layers) : this(sink, layers, 0) { }

    public bool IsLayerVisible(int layerIndex) =>
        (uint)layerIndex >= (uint)Layers.Count || Layers[layerIndex].IsVisible;

    public EmitContext Nested() => this with { Depth = Depth + 1 };
}
