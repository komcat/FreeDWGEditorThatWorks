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

        var order = Enumerable.Range(0, _removed.Length)
            .OrderBy(i => _positions[i])
            .ToArray();

        // Descending, so each removal leaves the positions of the rest alone.
        for (int i = order.Length - 1; i >= 0; i--) _layout.Remove(_removed[order[i]]);

        if (_added.Length == _removed.Length)
        {
            // A one-for-one swap: each replacement belongs exactly where its
            // original was. Restoring them ascending makes every recorded
            // index mean what it meant before anything was taken out, which
            // matters once a selection spread through the drawing is edited
            // in one go.
            foreach (int i in order) _layout.Insert(_positions[i], _added[i]);
            return;
        }

        // Counts differ -- a trim leaving two pieces, a fillet adding an arc
        // -- so there is no one-for-one place to put them. They go where the
        // first original was, which keeps them under whatever covered it.
        int at = _positions.Where(position => position >= 0).DefaultIfEmpty(_layout.Entities.Count).Min();
        for (int i = 0; i < _added.Length; i++) _layout.Insert(at + i, _added[i]);
    }

    public void Undo(Drawing drawing)
    {
        for (int i = _added.Length - 1; i >= 0; i--) _layout.Remove(_added[i]);

        // Ascending by the position each one came from, for the same reason
        // Apply is: a selection is in the order it was picked, which is not
        // the order the entities sit in, and putting them back in pick order
        // would shuffle the drawing.
        foreach (int i in Enumerable.Range(0, _removed.Length).OrderBy(i => _positions[i]))
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
