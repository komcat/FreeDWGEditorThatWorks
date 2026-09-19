using FreeDwg.Core.Rendering;
using FreeDwg.Core.Scene;

namespace FreeDwg.Core.Picking;

/// <summary>
/// What an entity needs while being hit tested: how close counts as a hit,
/// which layers may be picked at all, and how deep inside nested blocks we
/// are. The mirror of <see cref="EmitContext"/>, and for the same reason --
/// block definitions hold entities on their own layers.
/// </summary>
/// <param name="Tolerance">
/// The pick radius in the space being tested, which is also the resolution
/// curves are flattened at. Inside a block instance it is divided by the
/// instance's scale, so a tolerance the user set in world units stays the
/// same size on screen however the block is scaled.
/// </param>
public readonly record struct PickContext(
    double Tolerance,
    IReadOnlyList<Layer>? Layers = null,
    int Depth = 0)
{
    /// <summary>Same cap as emitting, and for the same self-referencing blocks.</summary>
    public const int MaxDepth = EmitContext.MaxDepth;

    /// <summary>
    /// Whether geometry on a layer can be hit. Invisible geometry cannot be
    /// clicked, and neither can a locked layer -- that is what locking is for.
    /// </summary>
    public bool IsLayerPickable(int layerIndex)
    {
        if (Layers is null || (uint)layerIndex >= (uint)Layers.Count) return true;

        var layer = Layers[layerIndex];
        return layer.IsVisible && !layer.IsLocked;
    }

    /// <summary>Descends into a block instance scaled by <paramref name="scale"/>.</summary>
    public PickContext Nested(double scale) => this with
    {
        Depth = Depth + 1,
        Tolerance = scale > 1e-300 ? Tolerance / scale : Tolerance,
    };

    /// <summary>How finely curves should be flattened for this tolerance.</summary>
    public double Samples => Geometry.Resolution.UnitsToSamples(Tolerance);
}
