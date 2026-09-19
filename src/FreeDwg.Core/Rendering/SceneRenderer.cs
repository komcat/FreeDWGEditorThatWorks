using FreeDwg.Core.Scene;

namespace FreeDwg.Core.Rendering;

public readonly record struct RenderStats(int Drawn, int CulledByBounds, int HiddenByLayer);

/// <summary>
/// Walks the scene once per frame: skip hidden layers, skip anything off
/// screen, hand the rest to the sink. No type switching -- entities emit
/// themselves.
/// </summary>
public static class SceneRenderer
{
    public static RenderStats Render(Drawing drawing, Camera camera, IDrawingSink sink, RenderSettings options)
    {
        var visible = camera.VisibleWorldBounds;
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

            entity.Emit(sink, options.Adapt(entity.Style));
            drawn++;
        }

        return new RenderStats(drawn, culled, hidden);
    }
}
