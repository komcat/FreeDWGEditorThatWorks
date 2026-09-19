using FreeDwg.Core.Scene;

namespace FreeDwg.Core.Commands;

/// <summary>
/// The only way the drawing changes: commands go on here, and undo and redo
/// walk the list.
/// </summary>
/// <remarks>
/// Held per drawing rather than per application, because an undo stack
/// belongs to the document it describes and a new document has to start with
/// an empty one.
/// </remarks>
public sealed class CommandStack
{
    private readonly Drawing _drawing;
    private readonly List<IEditCommand> _done = new();
    private readonly List<IEditCommand> _undone = new();

    /// <summary>How deep the stack was when the drawing was last saved.</summary>
    private int _savedDepth;

    public CommandStack(Drawing drawing) => _drawing = drawing;

    /// <summary>Raised after anything is done, undone or redone.</summary>
    public event EventHandler? Changed;

    public bool CanUndo => _done.Count > 0;
    public bool CanRedo => _undone.Count > 0;

    public string? UndoName => CanUndo ? _done[^1].Name : null;
    public string? RedoName => CanRedo ? _undone[^1].Name : null;

    /// <summary>Whether there is anything worth saving.</summary>
    public bool IsModified => _done.Count != _savedDepth;

    public void Do(IEditCommand command)
    {
        command.Apply(_drawing);
        _done.Add(command);

        // A new edit after an undo abandons the branch that was undone, which
        // is what every editor does and what everyone expects.
        _undone.Clear();

        // Saved at a depth that is no longer reachable: nothing can return the
        // drawing to the state on disk, so it is modified from here on.
        if (_savedDepth > _done.Count) _savedDepth = -1;

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Undo()
    {
        if (!CanUndo) return false;

        var command = _done[^1];
        _done.RemoveAt(_done.Count - 1);
        command.Undo(_drawing);
        _undone.Add(command);

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo) return false;

        var command = _undone[^1];
        _undone.RemoveAt(_undone.Count - 1);
        command.Apply(_drawing);
        _done.Add(command);

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void MarkSaved()
    {
        _savedDepth = _done.Count;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// What has changed since the file was opened, replayed from the stack.
    /// Undone commands are not in it, so this always describes the drawing as
    /// it stands rather than everything that was ever tried.
    /// </summary>
    public ChangeLog Summarize()
    {
        var log = new ChangeLog();
        foreach (var command in _done) command.Describe(log);
        return log;
    }
}
