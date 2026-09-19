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

    public LayerItem(SceneLayer layer, int entityCount, Action invalidate)
    {
        _layer = layer;
        _invalidate = invalidate;
        EntityCount = entityCount;

        var swatch = new SolidColorBrush(Color.FromRgb(layer.Color.R, layer.Color.G, layer.Color.B));
        swatch.Freeze();
        Swatch = swatch;
    }

    public string Name => _layer.Name;
    public Brush Swatch { get; }
    public int EntityCount { get; }

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
