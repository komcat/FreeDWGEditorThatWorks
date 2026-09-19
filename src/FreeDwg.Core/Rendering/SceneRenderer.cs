using FreeDwg.Core.Scene;

namespace FreeDwg.Core.Rendering;

public readonly record struct RenderStats(int Drawn, int CulledByBounds, int HiddenByLayer)
{
    /// <summary>Of those drawn, how many painted in the selection colour.</summary>
    public int Highlighted { get; init; }
}

/// <summary>
/// Walks the scene once per frame: ask the layout's index what is on screen,
/// skip hidden layers, hand the rest to the sink. No type switching --
/// entities emit themselves, and block instances recurse through the sink's
/// transform stack.
/// </summary>
public static class SceneRenderer
{
    [ThreadStatic] private static List<int>? _visible;

    public static RenderStats Render(Drawing drawing, Camera camera, IDrawingSink sink,
        IReadOnlySet<SceneEntity>? highlighted = null, RenderSettings? settings = null)
    {
        var index = drawing.ActiveLayout.Index;
        var context = new EmitContext(sink, drawing.Layers, camera.Scale);

        // The index answers with whatever order the tree walk reached things
        // in; painting order is the entity order, so sort the positions back.
        var visible = _visible ??= new List<int>();
        visible.Clear();
        index.Query(camera.VisibleWorldBounds, visible);
        visible.Sort();

        IDrawingSink? highlightSink = null;
        int drawn = 0, hidden = 0, selected = 0;

        foreach (int position in visible)
        {
            var entity = index[position];

            if ((uint)entity.LayerIndex < (uint)drawing.Layers.Count &&
                !drawing.Layers[entity.LayerIndex].IsVisible)
            {
                hidden++;
                continue;
            }

            if (highlighted is not null && highlighted.Contains(entity))
            {
                // Built lazily: a frame with nothing selected allocates nothing
                // and pays nothing.
                highlightSink ??= new StyleOverrideSink(sink,
                    (settings ?? DefaultSettings).SelectionColor);

                entity.Emit(context with { Sink = highlightSink }, entity.Style);
                selected++;
            }
            else
            {
                // Contrast adaptation happens in the sink, which is also where
                // block children end up -- they never pass back through here.
                entity.Emit(context, entity.Style);
            }

            drawn++;
        }

        int culled = index.Count - drawn - hidden;
        return new RenderStats(drawn, culled, hidden) { Highlighted = selected };
    }

    private static readonly RenderSettings DefaultSettings = new();
}
