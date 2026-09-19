using FreeDwg.Core.Geometry;

namespace FreeDwg.Core.Scene;

/// <summary>
/// One drawing surface: either model space, where the design is drafted at
/// full size, or a paper space sheet, which is laid out at plot size and
/// shows model space through viewports.
/// </summary>
public sealed class Layout
{
    public Layout(string name, bool isPaperSpace)
    {
        Name = name;
        IsPaperSpace = isPaperSpace;
    }

    public string Name { get; }
    public bool IsPaperSpace { get; }

    public List<SceneEntity> Entities { get; } = new();

    /// <summary>Sheet size in millimetres, when the layout declares one.</summary>
    public double PaperWidth { get; init; }
    public double PaperHeight { get; init; }

    public ulong SourceHandle { get; init; }

    private Bounds2? _bounds;

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

    public void Add(SceneEntity entity)
    {
        Entities.Add(entity);
        _bounds = null;
    }

    public override string ToString() => Name;
}
