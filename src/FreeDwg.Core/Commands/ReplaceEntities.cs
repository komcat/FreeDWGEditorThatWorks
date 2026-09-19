using FreeDwg.Core.Editing;
using FreeDwg.Core.Scene;

namespace FreeDwg.Core.Commands;

/// <summary>
/// Swaps some entities for others: the command behind trim, extend, fillet
/// and chamfer.
/// </summary>
/// <remarks>
/// These operations change shape and sometimes type -- a trimmed circle is
/// an arc, a line trimmed through the middle is two lines -- so none of them
/// is a transform and none is purely an add or a delete. Expressing all of
/// them as a swap means one command rather than four, and means no entity
/// needs a way to have its geometry overwritten and put back.
/// </remarks>
public sealed class ReplaceEntities : IEditCommand
{
    private readonly Layout _layout;
    private readonly SceneEntity[] _removed;
    private readonly SceneEntity[] _added;
    private int[] _positions = [];

    public ReplaceEntities(Layout layout, EditPlan plan, string name)
    {
        _layout = layout;
        _removed = plan.Removed.ToArray();
        _added = plan.Added.ToArray();
        Name = name;
    }

    public string Name { get; }

    /// <summary>What ended up in the drawing, so the shell can select it.</summary>
    public IReadOnlyList<SceneEntity> Added => _added;

    public void Apply(Drawing drawing)
    {
        // Remembered on the way out so undo can put the originals back in
        // their old places: entity order is painting order.
        _positions = _removed.Select(entity => _layout.Entities.IndexOf(entity)).ToArray();

        int at = _positions.Where(position => position >= 0).DefaultIfEmpty(_layout.Entities.Count).Min();

        for (int i = _removed.Length - 1; i >= 0; i--) _layout.Remove(_removed[i]);

        // The replacements go in where the originals were, rather than on top
        // of the drawing, so a trimmed line stays under whatever covered it.
        for (int i = 0; i < _added.Length; i++) _layout.Insert(at + i, _added[i]);
    }

    public void Undo(Drawing drawing)
    {
        for (int i = _added.Length - 1; i >= 0; i--) _layout.Remove(_added[i]);

        for (int i = 0; i < _removed.Length; i++)
        {
            int position = i < _positions.Length ? _positions[i] : -1;
            if (position >= 0) _layout.Insert(position, _removed[i]);
            else _layout.Add(_removed[i]);
        }
    }

    public void Describe(ChangeLog log)
    {
        foreach (var entity in _removed) log.Removed(entity);

        // A replacement that inherited the original's handle reads as an edit
        // rather than a birth, which is what the change log works out for us.
        foreach (var entity in _added) log.Added(entity);
    }
}
