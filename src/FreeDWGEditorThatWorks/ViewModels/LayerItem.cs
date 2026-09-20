using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using SceneLayer = FreeDwg.Core.Scene.Layer;

namespace FreeDWGEditorThatWorks.ViewModels;

/// <summary>
/// Presents one <see cref="SceneLayer"/> to the layers panel. The scene layer
/// stays free of UI concerns; toggling here mutates it and asks for a redraw.
/// </summary>
public sealed class LayerItem : INotifyPropertyChanged
{
    private readonly SceneLayer _layer;
    private readonly Action _invalidate;

    public LayerItem(SceneLayer layer, int index, int entityCount, Action invalidate)
    {
        _layer = layer;
        _invalidate = invalidate;
        Index = index;
        EntityCount = entityCount;

        var swatch = new SolidColorBrush(Color.FromRgb(layer.Color.R, layer.Color.G, layer.Color.B));
        swatch.Freeze();
        Swatch = swatch;
    }

    public string Name => _layer.Name;
    public Brush Swatch { get; }
    public int EntityCount { get; }

    /// <summary>
    /// Where this sits in <c>Drawing.Layers</c>, which is what an entity
    /// names its layer by.
    /// </summary>
    /// <remarks>
    /// Carried on the row rather than read back off the list, because the
    /// list can be filtered and a row's <em>position</em> then has nothing
    /// to do with the layer's index. That is a bug which quietly renames,
    /// recolours or deletes the wrong layer, and it is why nothing here
    /// asks the ListBox where a row is any more.
    /// </remarks>
    public int Index { get; }

    /// <summary>Whether this row survives the filter box.</summary>
    public bool Matches(string filter) =>
        filter.Length == 0 || Name.Contains(filter, StringComparison.OrdinalIgnoreCase);

    public bool IsOn
    {
        get => _layer.IsOn;
        set
        {
            if (_layer.IsOn == value) return;
            _layer.IsOn = value;
            _invalidate();
            OnPropertyChanged();
        }
    }

    public bool IsFrozen
    {
        get => _layer.IsFrozen;
        set
        {
            if (_layer.IsFrozen == value) return;
            _layer.IsFrozen = value;
            _invalidate();
            OnPropertyChanged();
        }
    }

    public string Tooltip =>
        $"{Name}\n{EntityCount} top-level entities\n{_layer.Lineweight}{(_layer.IsLocked ? "\nlocked" : "")}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
