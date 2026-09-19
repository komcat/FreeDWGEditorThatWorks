using FreeDwg.Core.Geometry;
using FreeDwg.Core.Picking;

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
    private SpatialIndex? _index;

    /// <summary>
    /// Spatial index over <see cref="Entities"/>, built on first use.
    /// Culling and picking both go through it instead of scanning.
    /// </summary>
    /// <remarks>
    /// Like <see cref="Bounds"/>, it is invalidated by <see cref="Add"/> and
    /// <see cref="InvalidateBounds"/>; a caller that reaches into
    /// <see cref="Entities"/> and mutates it has to say so.
    /// </remarks>
    public SpatialIndex Index => _index ??= SpatialIndex.Build(Entities);

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

    /// <summary>Drops both caches: moving an entity invalidates the index too.</summary>
    public void InvalidateBounds()
    {
        _bounds = null;
        _index = null;
    }

    public void Add(SceneEntity entity)
    {
        Entities.Add(entity);
        InvalidateBounds();
    }

    /// <summary>
    /// Puts an entity back where it was. Undoing a delete has to restore the
    /// position as well as the entity: entity order is painting order, and an
    /// entity that comes back on top of what it used to sit under is a
    /// different picture.
    /// </summary>
    public void Insert(int index, SceneEntity entity)
    {
        Entities.Insert(Math.Clamp(index, 0, Entities.Count), entity);
        InvalidateBounds();
    }

    public bool Remove(SceneEntity entity)
    {
        if (!Entities.Remove(entity)) return false;

        InvalidateBounds();
        return true;
    }

    public override string ToString() => Name;
}
