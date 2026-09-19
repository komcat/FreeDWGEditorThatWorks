using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene;

namespace FreeDwg.Core.Picking;

/// <summary>Whether a dragged rectangle takes what it encloses or what it touches.</summary>
public enum SelectionMode
{
    /// <summary>Only entities wholly inside the rectangle. Dragged left to right.</summary>
    Window,

    /// <summary>Anything the rectangle touches. Dragged right to left.</summary>
    Crossing,
}

/// <summary>
/// Turns a click or a dragged rectangle into entities, over a layout's
/// spatial index rather than its entity list.
/// </summary>
public static class Picker
{
    /// <summary>
    /// The entity nearest <paramref name="point"/> within
    /// <paramref name="tolerance"/> world units, or null.
    /// </summary>
    /// <remarks>
    /// Ties go to whatever is drawn last, so clicking where two objects
    /// overlap picks the one on top -- the one the user can see.
    /// </remarks>
    public static SceneEntity? At(Layout layout, IReadOnlyList<Layer> layers, Vec2 point, double tolerance)
    {
        var context = new PickContext(tolerance, layers);
        var index = layout.Index;

        var candidates = new List<int>();
        index.Query(Bounds2.FromPoint(point).Inflate(tolerance), candidates);
        candidates.Sort();

        SceneEntity? best = null;
        double bestDistance = double.PositiveInfinity;

        foreach (int position in candidates)
        {
            var entity = index[position];
            if (!context.IsLayerPickable(entity.LayerIndex)) continue;

            double distance = entity.DistanceTo(point, context);
            if (distance > tolerance || distance > bestDistance) continue;

            best = entity;
            bestDistance = distance;
        }

        return best;
    }

    public static SceneEntity? At(Drawing drawing, Vec2 point, double tolerance) =>
        At(drawing.ActiveLayout, drawing.Layers, point, tolerance);

    /// <summary>
    /// Everything taken by a dragged rectangle, in painting order.
    /// </summary>
    /// <remarks>
    /// Window selection tests entity bounds rather than geometry: bounds
    /// inside the rectangle means the geometry is too. The exception is a
    /// spline, whose bounds are its control hull and can reach outside the
    /// curve, so one lying near the edge of the window is occasionally missed.
    /// </remarks>
    public static List<SceneEntity> InRect(Layout layout, IReadOnlyList<Layer> layers,
        Bounds2 rect, SelectionMode mode, double tolerance)
    {
        var context = new PickContext(tolerance, layers);
        var index = layout.Index;
        var results = new List<SceneEntity>();

        if (rect.IsEmpty) return results;

        var candidates = new List<int>();
        index.Query(rect, candidates);
        candidates.Sort();

        foreach (int position in candidates)
        {
            var entity = index[position];
            if (!context.IsLayerPickable(entity.LayerIndex)) continue;

            bool taken = mode == SelectionMode.Window
                ? Intersect.RectContainsRect(rect, entity.Bounds)
                : entity.IntersectsRect(rect, context);

            if (taken) results.Add(entity);
        }

        return results;
    }

    public static List<SceneEntity> InRect(Drawing drawing, Bounds2 rect, SelectionMode mode, double tolerance) =>
        InRect(drawing.ActiveLayout, drawing.Layers, rect, mode, tolerance);
}
