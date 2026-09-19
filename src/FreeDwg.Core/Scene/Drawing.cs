using FreeDwg.Core.Geometry;

namespace FreeDwg.Core.Scene;

/// <summary>
/// A loaded drawing, in the shape the editor wants: a flat entity list, a
/// layer table, and nothing else. Deliberately not the file's object graph.
/// </summary>
public sealed class Drawing
{
    public List<Layer> Layers { get; } = new();
    public List<SceneEntity> Entities { get; } = new();

    /// <summary>Path this was imported from, if any.</summary>
    public string? SourcePath { get; set; }

    private Bounds2? _bounds;

    /// <summary>Union of every entity's bounds, regardless of layer visibility.</summary>
    public Bounds2 Bounds
    {
        get
        {
            if (_bounds is null)
            {
                var b = Bounds2.Empty;
                foreach (var e in Entities) b = b.Union(e.Bounds);
                _bounds = b;
            }
            return _bounds.Value;
        }
    }

    public void InvalidateBounds() => _bounds = null;

    /// <summary>Adds a layer and returns its index.</summary>
    public int AddLayer(Layer layer)
    {
        Layers.Add(layer);
        return Layers.Count - 1;
    }

    public void Add(SceneEntity entity)
    {
        Entities.Add(entity);
        _bounds = null;
    }
}
