namespace FreeDWGEditorThatWorks.Controls;

/// <summary>
/// What a left click on the canvas does. Exactly one of these is in force at
/// a time, which is the whole point of it being an enum rather than a set of
/// flags on the canvas.
/// </summary>
public enum CanvasMode
{
    /// <summary>Click to pick, drag to band.</summary>
    Select,

    /// <summary>Click to feed the active tool its next point.</summary>
    Draw,

    /// <summary>Drag once to frame the view, then back to <see cref="Select"/>.</summary>
    ZoomWindow,
}
