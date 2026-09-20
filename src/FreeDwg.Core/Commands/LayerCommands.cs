using FreeDwg.Core.Scene;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Commands;

/// <summary>
/// The editable half of a layer, so a change can be applied and put back
/// without a command needing a field per property.
/// </summary>
public readonly record struct LayerProperties(
    string Name,
    Rgb Color,
    Lineweight Lineweight,
    Linetype? Linetype,
    bool IsOn,
    bool IsFrozen,
    bool IsLocked)
{
    public static LayerProperties Of(Layer layer) => new(
        layer.Name, layer.Color, layer.Lineweight, layer.Linetype,
        layer.IsOn, layer.IsFrozen, layer.IsLocked);

    /// <summary>What an entity following this layer is drawn with.</summary>
    public DisplayStyle Style => new(Color, Lineweight, Linetype);

    public void ApplyTo(Layer layer)
    {
        layer.Name = Name;
        layer.Color = Color;
        layer.Lineweight = Lineweight;
        layer.Linetype = Linetype;
        layer.IsOn = IsOn;
        layer.IsFrozen = IsFrozen;
        layer.IsLocked = IsLocked;
    }
}

/// <summary>Several commands as one step on the undo stack.</summary>
/// <remarks>
/// Drawing a dimension on a drawing that has no dimension layer yet creates
/// the layer and the dimension. Those are two changes and one action, and an
/// undo that took the dimension back but left the layer behind would be
/// telling the truth about the wrong thing.
/// </remarks>
public sealed class Composite : IEditCommand
{
    private readonly IEditCommand[] _parts;

    public Composite(string name, params IEditCommand[] parts)
    {
        Name = name;
        _parts = parts;
    }

    public string Name { get; }

    public void Apply(Drawing drawing)
    {
        foreach (var part in _parts) part.Apply(drawing);
    }

    public void Undo(Drawing drawing)
    {
        // Backwards, so each part is taken back in a world that still looks
        // the way it did when that part was applied.
        for (int i = _parts.Length - 1; i >= 0; i--) _parts[i].Undo(drawing);
    }

    public void Describe(ChangeLog log)
    {
        foreach (var part in _parts) part.Describe(log);
    }
}

/// <summary>Adds a layer to the table.</summary>
public sealed class AddLayer : IEditCommand
{
    public AddLayer(Layer layer) => Layer = layer;

    public Layer Layer { get; }

    /// <summary>Where it landed, so the caller can put entities on it.</summary>
    public int Index { get; private set; } = -1;

    public string Name => $"New layer {Layer.Name}";

    public void Apply(Drawing drawing)
    {
        Index = drawing.Layers.Count;
        drawing.Layers.Add(Layer);
    }

    public void Undo(Drawing drawing)
    {
        // Added at the end, and undo is last in first out, so it is still
        // there -- but the renumbering path is used anyway rather than a bare
        // RemoveAt, because being right by luck is how this breaks later.
        int at = drawing.Layers.LastIndexOf(Layer);
        if (at >= 0) LayerTable.RemoveAt(drawing, at);
    }

    public void Describe(ChangeLog log) => log.LayerAdded(Layer);
}

/// <summary>
/// Removes a layer, and everything that pointed past it moves down.
/// </summary>
/// <remarks>
/// Refused while anything is drawn on it: the alternative is deciding on the
/// user's behalf whether their geometry is deleted or quietly moved to
/// another layer, and neither is a decision a delete key should make.
/// </remarks>
public sealed class DeleteLayer : IEditCommand
{
    private readonly int _index;
    private readonly Layer _layer;

    public DeleteLayer(Drawing drawing, int index)
    {
        if ((uint)index >= (uint)drawing.Layers.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (index == 0)
            throw new ArgumentException("Layer 0 is the one every DWG has and cannot be removed.", nameof(index));

        if (LayerTable.IsInUse(drawing, index))
            throw new InvalidOperationException($"Layer {drawing.Layers[index].Name} still has geometry on it.");

        _index = index;
        _layer = drawing.Layers[index];
    }

    public string Name => $"Delete layer {_layer.Name}";

    public void Apply(Drawing drawing) => LayerTable.RemoveAt(drawing, _index);

    public void Undo(Drawing drawing) => LayerTable.InsertAt(drawing, _index, _layer);

    public void Describe(ChangeLog log) => log.LayerRemoved(_layer);
}

/// <summary>
/// Renames a layer, or changes its colour, width or dash pattern.
/// </summary>
/// <remarks>
/// Styles are resolved at import, so an entity does not say "ByLayer" any
/// more -- it holds the colour the layer had at the time. Changing the layer
/// therefore has to go and change them, or the swatch and the geometry
/// disagree and the layer panel is lying.
/// <para>
/// Only the entities still drawn in the layer's <em>old</em> style are
/// touched. That is exactly the set that was following the layer: anything
/// given a colour of its own differs from it, and keeps what it was given.
/// It is a heuristic and it is the right one, since the alternative is
/// either ignoring the override or losing it.
/// </para>
/// </remarks>
public sealed class ChangeLayer : IEditCommand
{
    private readonly int _index;
    private readonly Layer _layer;
    private readonly LayerProperties _after;
    private LayerProperties _before;
    private (SceneEntity Entity, DisplayStyle Style)[] _restyled = [];

    public ChangeLayer(Drawing drawing, int index, LayerProperties properties)
    {
        _index = index;
        _layer = drawing.Layers[index];
        _after = properties;
        _before = LayerProperties.Of(_layer);
    }

    public string Name => _before.Name == _after.Name
        ? $"Change layer {_after.Name}"
        : $"Rename layer {_before.Name}";

    public void Apply(Drawing drawing)
    {
        var layer = drawing.Layers[_index];
        _before = LayerProperties.Of(layer);

        _restyled = _before.Style.Equals(_after.Style) ? [] : Restyle(drawing, _before.Style, _after.Style);
        _after.ApplyTo(layer);
    }

    private (SceneEntity, DisplayStyle)[] Restyle(Drawing drawing, DisplayStyle was, DisplayStyle now)
    {
        var changed = new List<(SceneEntity, DisplayStyle)>();

        foreach (var entity in LayerTable.AllEntities(drawing))
        {
            if (entity.LayerIndex != _index || !entity.Style.Equals(was)) continue;

            changed.Add((entity, entity.Style));
            entity.Style = now;
        }

        return changed.ToArray();
    }

    public void Undo(Drawing drawing)
    {
        _before.ApplyTo(drawing.Layers[_index]);
        foreach (var (entity, style) in _restyled) entity.Style = style;
    }

    public void Describe(ChangeLog log)
    {
        log.LayerEdited(_layer);

        // The entities that followed it were rewritten too, and a save that
        // wrote the layer but not them would put the file back the way it
        // was the moment it was reopened.
        foreach (var (entity, _) in _restyled) log.Edited(entity);
    }
}
