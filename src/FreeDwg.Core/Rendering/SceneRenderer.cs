using FreeDwg.Core.Scene;

namespace FreeDwg.Core.Rendering;

public readonly record struct RenderStats(int Drawn, int CulledByBounds, int HiddenByLayer);

/// <summary>
/// Walks the scene once per frame: skip hidden layers, skip anything off
/// screen, hand the rest to the sink. No type switching -- entities emit
/// themselves, and block instances recurse through the sink's transform stack.
/// </summary>
public static class SceneRenderer
{
    public static RenderStats Render(Drawing drawing, Camera camera, IDrawingSink sink)
    {
        var visible = camera.VisibleWorldBounds;
        var context = new EmitContext(sink, drawing.Layers);
        int drawn = 0, culled = 0, hidden = 0;

        foreach (var entity in drawing.Entities)
        {
            if ((uint)entity.LayerIndex < (uint)drawing.Layers.Count &&
                !drawing.Layers[entity.LayerIndex].IsVisible)
            {
                hidden++;
                continue;
            }

            // Linear scan is fine at M1 sizes. This is the seam where a spatial
            // index drops in: replace the loop with an index query over `visible`.
            var bounds = entity.Bounds;
            if (!bounds.IsEmpty && !bounds.Intersects(visible))
            {
                culled++;
                continue;
            }

            // Contrast adaptation happens in the sink, which is also where
            // block children end up -- they never pass back through here.
            entity.Emit(context, entity.Style);
            drawn++;
        }

        return new RenderStats(drawn, culled, hidden);
    }
}
