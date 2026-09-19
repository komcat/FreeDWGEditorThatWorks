using FreeDwg.Core.Scene;

namespace FreeDwg.Core.Commands;

/// <summary>Adds entities to a layout. What every draw tool ends with.</summary>
public sealed class AddEntities : IEditCommand
{
    private readonly Layout _layout;
    private readonly SceneEntity[] _entities;

    public AddEntities(Layout layout, params SceneEntity[] entities)
    {
        _layout = layout;
        _entities = entities;
    }

    public AddEntities(Layout layout, IEnumerable<SceneEntity> entities)
        : this(layout, entities.ToArray()) { }

    public string Name => _entities.Length == 1
        ? $"Draw {Describe(_entities[0])}"
        : $"Draw {_entities.Length} objects";

    public void Apply(Drawing drawing)
    {
        foreach (var entity in _entities) _layout.Add(entity);
    }

    public void Undo(Drawing drawing)
    {
        // Backwards, so each removal leaves the positions of the rest alone.
        for (int i = _entities.Length - 1; i >= 0; i--) _layout.Remove(_entities[i]);
    }

    public void Describe(ChangeLog log)
    {
        foreach (var entity in _entities) log.Added(entity);
    }

    internal static string Describe(SceneEntity entity) =>
        entity.GetType().Name.TrimStart('S').ToLowerInvariant();
}

/// <summary>Deletes entities, remembering where they were.</summary>
public sealed class DeleteEntities : IEditCommand
{
    private readonly Layout _layout;
    private readonly SceneEntity[] _entities;
    private readonly int[] _positions;

    public DeleteEntities(Layout layout, IEnumerable<SceneEntity> entities)
    {
        _layout = layout;

        // Sorted by position so that undo can put them back front to back and
        // have every index still mean what it meant when they were taken out.
        var ordered = entities
            .Select(entity => (Entity: entity, Index: layout.Entities.IndexOf(entity)))
            .Where(pair => pair.Index >= 0)
            .OrderBy(pair => pair.Index)
            .ToArray();

        _entities = ordered.Select(pair => pair.Entity).ToArray();
        _positions = ordered.Select(pair => pair.Index).ToArray();
    }

    public int Count => _entities.Length;

    public string Name => _entities.Length == 1
        ? $"Erase {AddEntities.Describe(_entities[0])}"
        : $"Erase {_entities.Length} objects";

    public void Apply(Drawing drawing)
    {
        for (int i = _entities.Length - 1; i >= 0; i--) _layout.Remove(_entities[i]);
    }

    public void Undo(Drawing drawing)
    {
        for (int i = 0; i < _entities.Length; i++) _layout.Insert(_positions[i], _entities[i]);
    }

    public void Describe(ChangeLog log)
    {
        foreach (var entity in _entities) log.Removed(entity);
    }
}
