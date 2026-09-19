using FreeDwg.Core.Geometry;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Tools;

/// <summary>
/// A tool that builds one entity out of a series of picked points.
/// </summary>
/// <remarks>
/// Deliberately knows nothing about mice, keys or WPF: it takes world points
/// and gives back a scene entity, which is what makes the whole set testable
/// as arithmetic. The shell turns clicks into points and hands whatever comes
/// back to the command stack.
/// <para>
/// A tool never touches the drawing. It cannot: it has no reference to one.
/// That is the rule E2 exists to enforce, expressed as a type signature
/// rather than as a comment nobody reads.
/// </para>
/// </remarks>
public abstract class DrawTool
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
    /// Takes a picked point. Returns the finished entity when this point
    /// completes it, and null while the tool still wants more.
    /// </summary>
    public SceneEntity? Click(Vec2 point)
    {
        _points.Add(point);

        var entity = Build(point);
        if (entity is not null) _points.Clear();

        return entity;
    }

    /// <summary>
    /// Ends a tool early -- Enter, or a right click. Only the open-ended
    /// tools have anything to give back here.
    /// </summary>
    public virtual SceneEntity? Finish()
    {
        var entity = _points.Count > 0 ? BuildPartial() : null;
        _points.Clear();
        return entity;
    }

    public void Cancel() => _points.Clear();

    /// <summary>
    /// Draws the entity as it would be if the next point were
    /// <paramref name="cursor"/>. Goes through the same sink the scene does,
    /// so a previewed arc bulges the way the real one will.
    /// </summary>
    public abstract void Preview(Vec2 cursor, in EmitContext context, in DisplayStyle style);

    /// <summary>The entity, if the points now in hand are enough for one.</summary>
    protected abstract SceneEntity? Build(Vec2 last);

    /// <summary>The entity for an early finish; null for tools with a fixed point count.</summary>
    protected virtual SceneEntity? BuildPartial() => null;

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
