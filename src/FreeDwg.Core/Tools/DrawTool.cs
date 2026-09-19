using FreeDwg.Core.Geometry;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Tools;

/// <summary>
/// A tool that builds one new entity out of a series of picked points.
/// </summary>
public abstract class DrawTool : CanvasTool
{
    /// <summary>
    /// Takes a picked point. Returns the finished entity when this point
    /// completes it, and null while the tool still wants more.
    /// </summary>
    public SceneEntity? Click(Vec2 point)
    {
        AddPoint(point);

        var entity = Build(point);
        if (entity is not null) ClearPoints();

        return entity;
    }

    /// <summary>
    /// Ends a tool early -- Enter, or a right click. Only the open-ended
    /// tools have anything to give back here.
    /// </summary>
    public virtual SceneEntity? Finish()
    {
        var entity = Points.Count > 0 ? BuildPartial() : null;
        ClearPoints();
        return entity;
    }

    /// <summary>The entity, if the points now in hand are enough for one.</summary>
    protected abstract SceneEntity? Build(Vec2 last);

    /// <summary>The entity for an early finish; null for tools with a fixed point count.</summary>
    protected virtual SceneEntity? BuildPartial() => null;
}
