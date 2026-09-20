namespace FreeDWGEditorThatWorks.Controls;

/// <summary>
/// What the box beside the cursor is editing.
/// </summary>
/// <remarks>
/// One enum rather than a flag per field, for the reason
/// <see cref="CanvasMode"/> is one: the box shows exactly one number and
/// typing into it has to have exactly one destination.
/// </remarks>
public enum CursorEntry
{
    /// <summary>Nothing to type; the box is hidden.</summary>
    None,

    /// <summary>How far the run being drawn, or the grip being dragged, goes.</summary>
    Length,

    /// <summary>The radius a fillet rounds a corner to.</summary>
    Radius,

    /// <summary>How far back along each edge a chamfer cuts.</summary>
    Distance,
}
