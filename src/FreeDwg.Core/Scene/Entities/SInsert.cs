using FreeDwg.Core.Geometry;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Scene.Entities;

/// <summary>
/// One placement of a <see cref="BlockDefinition"/>.
/// </summary>
/// <remarks>
/// The block's geometry is not copied here, only referenced and transformed.
/// That keeps a drawing with thousands of identical symbols small, and means
/// that once the editor can modify a block definition, every instance updates
/// with it.
/// </remarks>
public sealed class SInsert : SceneEntity
{
    public SInsert(BlockDefinition block, Mat3 transform)
    {
        Block = block;
        Transform = transform;
    }

    public BlockDefinition Block { get; set; }

    /// <summary>Block-local space to the space containing this insert.</summary>
    public Mat3 Transform { get; set; }

    protected override Bounds2 ComputeBounds() => Transform.TransformBounds(Block.Bounds);

    public override void Emit(in EmitContext context, in DisplayStyle style)
    {
        if (context.Depth >= EmitContext.MaxDepth) return;

        context.Sink.PushTransform(Transform);
        var nested = context.Nested();

        foreach (var child in Block.Entities)
        {
            // Layer visibility applies inside blocks too: freezing a layer has
            // to hide the geometry a block instance draws on it.
            if (!nested.IsLayerVisible(child.LayerIndex)) continue;

            child.Emit(nested, child.StyleWithin(style));
        }

        context.Sink.PopTransform();
    }

    /// <summary>
    /// Builds the standard DWG insert transform: block geometry is defined
    /// about the base point, which is then scaled, rotated and moved to the
    /// insertion point.
    /// </summary>
    public static Mat3 BuildTransform(Vec2 basePoint, Vec2 insertPoint, double scaleX, double scaleY, double rotation) =>
        Mat3.Translation(-basePoint)
        * Mat3.Scaling(scaleX, scaleY)
        * Mat3.Rotation(rotation)
        * Mat3.Translation(insertPoint);
}
