using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene;

namespace FreeDwg.Core.Commands;

/// <summary>
/// Moves, rotates, scales or mirrors entities where they stand.
/// </summary>
/// <remarks>
/// The first command that edits rather than adds or deletes, so the first
/// that reports handles as <see cref="ChangeLog.Edited"/> -- a save has to
/// rewrite those objects in place rather than create or remove them.
/// <para>
/// Undo applies the inverse rather than restoring a snapshot. For an affine
/// transform the inverse is exact to within double rounding, and a snapshot
/// would mean every entity knowing how to copy and restore its own geometry.
/// A transform with no inverse is refused at construction instead.
/// </para>
/// </remarks>
public sealed class TransformEntities : IEditCommand
{
    private readonly Layout _layout;
    private readonly SceneEntity[] _entities;
    private readonly Mat3 _transform;
    private readonly Mat3 _inverse;

    public TransformEntities(Layout layout, IEnumerable<SceneEntity> entities, Mat3 transform, string name)
    {
        _layout = layout;
        _entities = entities.ToArray();
        _transform = transform;
        Name = _entities.Length == 1 ? name : $"{name} {_entities.Length} objects";

        if (!transform.TryInvert(out _inverse))
            throw new ArgumentException("A transform that cannot be inverted cannot be undone.", nameof(transform));
    }

    public string Name { get; }

    public void Apply(Drawing drawing) => Move(_transform);

    public void Undo(Drawing drawing) => Move(_inverse);

    private void Move(in Mat3 transform)
    {
        foreach (var entity in _entities) entity.Transform(transform);

        // Each entity drops its own bounds cache; the layout's bounds and its
        // spatial index are a level up and have to be told separately.
        _layout.InvalidateBounds();
    }

    public void Describe(ChangeLog log)
    {
        foreach (var entity in _entities) log.Edited(entity);
    }
}

/// <summary>
/// Copies entities and puts the copies somewhere else. Copy, and the mirror
/// that keeps its original.
/// </summary>
public sealed class CopyEntities : IEditCommand
{
    private readonly Layout _layout;
    private readonly SceneEntity[] _copies;

    public CopyEntities(Layout layout, IEnumerable<SceneEntity> entities, Mat3 transform, string name)
    {
        _layout = layout;

        _copies = entities.Select(entity =>
        {
            var copy = entity.Clone();
            copy.Transform(transform);
            return copy;
        }).ToArray();

        Name = _copies.Length == 1 ? name : $"{name} {_copies.Length} objects";
    }

    public string Name { get; }

    /// <summary>The new entities, so the shell can leave them selected.</summary>
    public IReadOnlyList<SceneEntity> Copies => _copies;

    public void Apply(Drawing drawing)
    {
        foreach (var copy in _copies) _layout.Add(copy);
    }

    public void Undo(Drawing drawing)
    {
        for (int i = _copies.Length - 1; i >= 0; i--) _layout.Remove(_copies[i]);
    }

    public void Describe(ChangeLog log)
    {
        foreach (var copy in _copies) log.Added(copy);
    }
}

/// <summary>
/// Many copies of the selection at once, each under its own transform: the
/// command behind array.
/// </summary>
/// <remarks>
/// One command rather than a copy per item, so a thirty-item array is one
/// press of undo. Every copy is a clone, so it carries no handle and is a
/// new object to a save -- and remembers what it was copied from, which is
/// how a copied hatch keeps its pattern in the file.
/// </remarks>
public sealed class ArrayEntities : IEditCommand
{
    private readonly Layout _layout;
    private readonly SceneEntity[] _copies;

    public ArrayEntities(Layout layout, IEnumerable<SceneEntity> entities, IEnumerable<Mat3> transforms, string name)
    {
        _layout = layout;

        var originals = entities.ToArray();
        _copies = transforms
            .SelectMany(transform => originals.Select(entity =>
            {
                var copy = entity.Clone();
                copy.Transform(transform);
                return copy;
            }))
            .ToArray();

        Name = name;
    }

    public string Name { get; }

    /// <summary>The new entities, so the shell can leave the whole array selected.</summary>
    public IReadOnlyList<SceneEntity> Copies => _copies;

    public void Apply(Drawing drawing)
    {
        foreach (var copy in _copies) _layout.Add(copy);
    }

    public void Undo(Drawing drawing)
    {
        for (int i = _copies.Length - 1; i >= 0; i--) _layout.Remove(_copies[i]);
    }

    public void Describe(ChangeLog log)
    {
        foreach (var copy in _copies) log.Added(copy);
    }
}
