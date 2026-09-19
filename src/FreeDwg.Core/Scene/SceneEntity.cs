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

    /// <summary>Colour and width, with ByLayer already resolved.</summary>
    public DisplayStyle Style { get; set; } = DisplayStyle.Default;

    /// <summary>
    /// Which parts of <see cref="Style"/> are placeholders to be supplied by
    /// the block reference drawing this entity (DWG's ByBlock). Always None for
    /// entities in model space, since there is no reference to inherit from.
    /// </summary>
    public StyleInheritance Inherits { get; set; }

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
    public abstract void Emit(in EmitContext context, in DisplayStyle style);

    /// <summary>Applies this entity's ByBlock placeholders from the enclosing reference's style.</summary>
    public DisplayStyle StyleWithin(in DisplayStyle reference)
    {
        if (Inherits == StyleInheritance.None) return Style;

        var style = Style;
        if (Inherits.HasFlag(StyleInheritance.Color)) style = style with { Color = reference.Color };
        if (Inherits.HasFlag(StyleInheritance.Lineweight)) style = style with { Lineweight = reference.Lineweight };
        return style;
    }
}
