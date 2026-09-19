using FreeDwg.Core.Geometry;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Scene;

/// <summary>
/// Base class for everything drawable. This is the editor's model, not the
/// file's: styles are already resolved, geometry is 2D, and nothing here knows
/// what a DWG is.
/// </summary>
public abstract class SceneEntity
{
    /// <summary>Index into <see cref="Drawing.Layers"/>.</summary>
    public int LayerIndex { get; set; }

    /// <summary>Colour and width, with ByLayer/ByBlock already resolved.</summary>
    public DisplayStyle Style { get; set; } = DisplayStyle.Default;

    /// <summary>
    /// Handle of the object this came from. Saving applies deltas back onto the
    /// original document rather than regenerating it, so anything we do not
    /// model (xdata, proxies, dictionaries) survives a round trip untouched.
    /// </summary>
    public ulong SourceHandle { get; set; }

    private Bounds2? _bounds;

    /// <summary>World bounds, computed once. Invalidate after mutating geometry.</summary>
    public Bounds2 Bounds => _bounds ??= ComputeBounds();

    public void InvalidateBounds() => _bounds = null;

    protected abstract Bounds2 ComputeBounds();

    /// <summary>Emits this entity's geometry. Double dispatch, so the render loop needs no type switch.</summary>
    public abstract void Emit(IDrawingSink sink, in DisplayStyle style);
}
