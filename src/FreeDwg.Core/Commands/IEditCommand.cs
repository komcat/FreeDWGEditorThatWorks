using FreeDwg.Core.Scene;

namespace FreeDwg.Core.Commands;

/// <summary>
/// One undoable change to a drawing.
/// </summary>
/// <remarks>
/// Every mutation goes through one of these, without exception. That is not
/// bookkeeping for its own sake: saving has to apply deltas back onto the
/// document that was loaded rather than regenerate it, or everything the
/// scene model does not understand -- xdata, extension dictionaries, proxy
/// objects from vertical products -- is silently destroyed on the first save.
/// The delta is the list of commands, so a tool that reaches past this and
/// edits an entity directly is invisible to the writer.
/// </remarks>
public interface IEditCommand
{
    /// <summary>What to call this on an undo menu. Sentence case, no verb tense games.</summary>
    string Name { get; }

    void Apply(Drawing drawing);

    /// <summary>Puts the drawing back exactly as it was, including entity order.</summary>
    void Undo(Drawing drawing);

    /// <summary>Reports what this touched, so the writer knows what to write.</summary>
    void Describe(ChangeLog log);
}
