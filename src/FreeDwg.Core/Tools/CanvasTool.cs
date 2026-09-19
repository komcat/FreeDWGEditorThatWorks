using FreeDwg.Core.Geometry;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Tools;

/// <summary>
/// Anything the canvas can be put into that collects picked points.
/// </summary>
/// <remarks>
/// Draw tools and modify tools share this so that the canvas holds exactly
/// one of them. Two fields -- one per family -- would be the same mistake as
/// the tool flag and the zoom-window flag that could both be set: two ways to
/// answer what a click does is one too many.
/// <para>
/// Deliberately knows nothing about mice, keys or WPF, and holds no reference
/// to a drawing. It takes world points and gives back a result; the shell
/// turns that into a command. A tool therefore cannot reach past the command
/// stack even by accident.
/// </para>
/// </remarks>
public abstract class CanvasTool
{
    private readonly List<Vec2> _points = new();

    /// <summary>Points picked so far.</summary>
    public IReadOnlyList<Vec2> Points => _points;

    public bool InProgress => _points.Count > 0;

    /// <summary>What to call this in the status bar and on the undo stack.</summary>
    public abstract string Name { get; }

    /// <summary>What to ask for next, given what has been picked already.</summary>
    public abstract string Prompt { get; }

    /// <summary>
    /// Whether this tool acts on a selection and so needs one before it can
    /// start. True for everything in Modify, false for everything in Draw.
    /// </summary>
    public virtual bool NeedsSelection => false;

    public virtual void Cancel() => _points.Clear();

    protected void AddPoint(Vec2 point) => _points.Add(point);

    protected void ClearPoints() => _points.Clear();

    /// <summary>
    /// Draws what the result would be if the next point were
    /// <paramref name="cursor"/>. Modify tools leave this alone: the canvas
    /// previews them by drawing the selection under the pending transform,
    /// which needs the selection and so cannot live here.
    /// </summary>
    public virtual void Preview(Vec2 cursor, in EmitContext context, in DisplayStyle style) { }

    /// <summary>Convenience for previews: the points so far plus the cursor.</summary>
    protected Vec2[] WithCursor(Vec2 cursor)
    {
        var points = new Vec2[_points.Count + 1];
        for (int i = 0; i < _points.Count; i++) points[i] = _points[i];
        points[^1] = cursor;
        return points;
    }

    protected static void Stroke(in EmitContext context, in DisplayStyle style,
        IReadOnlyList<Vec2> points, bool closed)
    {
        if (points.Count < 2) return;

        var sink = context.Sink;
        sink.BeginFigure(points[0], closed, style);
        for (int i = 1; i < points.Count; i++) sink.LineTo(points[i]);
        sink.EndFigure();
    }
}
