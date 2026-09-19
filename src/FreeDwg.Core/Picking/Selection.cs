using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene;

namespace FreeDwg.Core.Picking;

/// <summary>
/// What is currently selected, in the order it was picked.
/// </summary>
/// <remarks>
/// Order is kept because commands care about it -- the last thing picked is
/// the base object for several of them -- while membership tests happen once
/// per entity per frame, so both a list and a set are held rather than
/// scanning one of them.
/// <para>
/// Entities are compared by reference: two identical lines are two objects.
/// </para>
/// </remarks>
public sealed class Selection
{
    private readonly List<SceneEntity> _ordered = new();
    private readonly HashSet<SceneEntity> _set = new(ReferenceEqualityComparer.Instance);

    /// <summary>Raised after any change, once per change.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<SceneEntity> Ordered => _ordered;

    /// <summary>Membership, for the renderer to consult per entity.</summary>
    public IReadOnlySet<SceneEntity> Entities => _set;

    public int Count => _ordered.Count;
    public bool IsEmpty => _ordered.Count == 0;

    public bool Contains(SceneEntity entity) => _set.Contains(entity);

    public bool Add(SceneEntity entity)
    {
        if (!_set.Add(entity)) return false;

        _ordered.Add(entity);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Remove(SceneEntity entity)
    {
        if (!_set.Remove(entity)) return false;

        _ordered.Remove(entity);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Adds if absent, removes if present. Returns whether it is now selected.</summary>
    public bool Toggle(SceneEntity entity)
    {
        if (Contains(entity))
        {
            Remove(entity);
            return false;
        }

        Add(entity);
        return true;
    }

    public void Clear()
    {
        if (_ordered.Count == 0) return;

        _ordered.Clear();
        _set.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Replaces the whole selection, raising <see cref="Changed"/> once.</summary>
    public void Set(IEnumerable<SceneEntity> entities)
    {
        _ordered.Clear();
        _set.Clear();

        foreach (var entity in entities)
            if (_set.Add(entity)) _ordered.Add(entity);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds several, raising <see cref="Changed"/> once for the lot.</summary>
    public int AddRange(IEnumerable<SceneEntity> entities)
    {
        int added = 0;
        foreach (var entity in entities)
        {
            if (!_set.Add(entity)) continue;
            _ordered.Add(entity);
            added++;
        }

        if (added > 0) Changed?.Invoke(this, EventArgs.Empty);
        return added;
    }

    /// <summary>Bounds of everything selected, for zooming to it.</summary>
    public Bounds2 Bounds
    {
        get
        {
            var bounds = Bounds2.Empty;
            foreach (var entity in _ordered) bounds = bounds.Union(entity.Bounds);
            return bounds;
        }
    }
}
