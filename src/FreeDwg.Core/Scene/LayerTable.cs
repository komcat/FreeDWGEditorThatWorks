using FreeDwg.Core.Scene.Entities;

namespace FreeDwg.Core.Scene;

/// <summary>
/// Adding and removing layers, with the renumbering that goes with it.
/// </summary>
/// <remarks>
/// An entity says which layer it is on by its <em>position</em> in
/// <see cref="Drawing.Layers"/>, which makes every lookup an array index and
/// makes removing one from the middle everybody's business: every entity
/// past it, in every layout and inside every block definition, is suddenly
/// pointing at its neighbour. So removal lives here, once, rather than at
/// each call site that thinks it only wants a list operation.
/// </remarks>
public static class LayerTable
{
    /// <summary>
    /// Every entity in the drawing: all layouts, and the contents of every
    /// block definition. Block children carry a layer index of their own, so
    /// anything renumbering layers has to reach them too.
    /// </summary>
    public static IEnumerable<SceneEntity> AllEntities(Drawing drawing)
    {
        foreach (var layout in drawing.Layouts)
            foreach (var entity in layout.Entities)
                yield return entity;

        foreach (var block in drawing.Blocks)
            foreach (var entity in block.Entities)
                yield return entity;
    }

    /// <summary>Whether anything is drawn on the layer at <paramref name="index"/>.</summary>
    public static bool IsInUse(Drawing drawing, int index) =>
        AllEntities(drawing).Any(entity => entity.LayerIndex == index);

    /// <summary>How many top-level entities of the active layout sit on each layer.</summary>
    public static int CountOn(Drawing drawing, int index) =>
        drawing.ActiveLayout.Entities.Count(entity => entity.LayerIndex == index);

    /// <summary>The index of the layer called <paramref name="name"/>, or -1.</summary>
    public static int IndexOf(Drawing drawing, string name)
    {
        for (int i = 0; i < drawing.Layers.Count; i++)
            if (string.Equals(drawing.Layers[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;

        return -1;
    }

    /// <summary>A name not already taken, by adding a number to it.</summary>
    public static string UniqueName(Drawing drawing, string wanted)
    {
        if (IndexOf(drawing, wanted) < 0) return wanted;

        for (int n = 1; ; n++)
        {
            string candidate = $"{wanted} {n}";
            if (IndexOf(drawing, candidate) < 0) return candidate;
        }
    }

    /// <summary>
    /// Removes the layer at <paramref name="index"/>, renumbering everything
    /// that pointed past it. Anything still <em>on</em> it would be orphaned,
    /// so callers check <see cref="IsInUse"/> first.
    /// </summary>
    public static void RemoveAt(Drawing drawing, int index)
    {
        drawing.Layers.RemoveAt(index);
        Renumber(drawing, index, -1);
    }

    /// <summary>Puts one back where it was, renumbering the other way.</summary>
    public static void InsertAt(Drawing drawing, int index, Layer layer)
    {
        drawing.Layers.Insert(index, layer);
        Renumber(drawing, index, +1);
    }

    /// <summary>
    /// Shifts every reference at or past <paramref name="from"/> by
    /// <paramref name="delta"/>: entity layers, the viewport freeze sets, and
    /// whichever layer is current.
    /// </summary>
    private static void Renumber(Drawing drawing, int from, int delta)
    {
        foreach (var entity in AllEntities(drawing))
        {
            if (entity.LayerIndex >= from) entity.LayerIndex += delta;

            // A viewport freezes layers by index as well, and a frozen layer
            // left pointing at its neighbour hides the wrong geometry -- in
            // one viewport only, which is the hardest kind of wrong to spot.
            if (entity is SViewport { FrozenLayers: { } frozen })
                ((SViewport)entity).FrozenLayers = Shift(frozen, from, delta);
        }

        if (drawing.CurrentLayerIndex >= from) drawing.CurrentLayerIndex += delta;
    }

    private static IReadOnlySet<int> Shift(IReadOnlySet<int> layers, int from, int delta)
    {
        var shifted = new HashSet<int>(layers.Count);
        foreach (int layer in layers) shifted.Add(layer >= from ? layer + delta : layer);
        return shifted;
    }
}
