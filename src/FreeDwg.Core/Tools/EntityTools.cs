using FreeDwg.Core.Editing;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;

namespace FreeDwg.Core.Tools;

/// <summary>
/// What an entity-picking tool is told about a click: what was under the
/// cursor, where exactly, and the curves of everything else nearby.
/// </summary>
/// <remarks>
/// The boundaries come in from the canvas rather than being looked up,
/// because a tool holds no reference to a drawing. That is the same rule
/// that stops a draw tool editing the scene, and it is why these are still
/// testable with a handful of entities and no canvas at all.
/// </remarks>
public readonly record struct EntityPick(
    SceneEntity? Entity,
    Vec2 World,
    IReadOnlyList<CurvePiece> Boundaries);

/// <summary>
/// A tool driven by clicking on entities rather than on empty space.
/// </summary>
public abstract class EntityTool : CanvasTool
{
    /// <summary>Entities picked so far, for the canvas to highlight.</summary>
    protected readonly List<SceneEntity> Picked = new();

    public IReadOnlyList<SceneEntity> PickedEntities => Picked;

    /// <summary>
    /// Takes a click. Returns what to do when the tool has everything it
    /// needs, and an empty plan while it wants more or the click missed.
    /// </summary>
    public abstract EditPlan Click(in EntityPick pick);

    /// <summary>
    /// Override rather than hide: the canvas cancels through a
    /// <see cref="CanvasTool"/> reference, so a hidden method would never
    /// run and a half-finished fillet would keep its first line for ever.
    /// </summary>
    public override void Cancel()
    {
        Picked.Clear();
        base.Cancel();
    }
}

/// <summary>Cuts an object back to where its neighbours cross it.</summary>
public sealed class TrimTool : EntityTool
{
    public override string Name => "trim";

    public override string Prompt => "Trim: click the part of a line or arc to cut away";

    public override EditPlan Click(in EntityPick pick) => pick.Entity is null
        ? EditPlan.Nothing
        : Trimming.Trim(pick.Entity, pick.Boundaries, pick.World);
}

/// <summary>Pushes an object out until it reaches its neighbours.</summary>
public sealed class ExtendTool : EntityTool
{
    public override string Name => "extend";

    public override string Prompt => "Extend: click near the end of a line or arc to push out";

    public override EditPlan Click(in EntityPick pick) => pick.Entity is null
        ? EditPlan.Nothing
        : Trimming.Extend(pick.Entity, pick.Boundaries, pick.World);
}

/// <summary>
/// Two lines, then the corner between them is rounded. Where each line is
/// clicked decides which half of it survives.
/// </summary>
public class FilletTool : EntityTool
{
    private Vec2 _firstPick;

    /// <summary>Radius of the arc, or zero to bring the lines to a sharp corner.</summary>
    public double Radius { get; set; }

    public override string Name => "fillet";

    public override string Prompt => Picked.Count == 0
        ? Opening
        : "Fillet: click the second line, on the side to keep";

    protected virtual string Opening => Radius > 0
        ? $"Fillet (radius {Radius:0.###}): click the first line, on the side to keep"
        : "Fillet (radius 0, a sharp corner): click the first line, on the side to keep";

    public override EditPlan Click(in EntityPick pick)
    {
        if (pick.Entity is not SLine line) return EditPlan.Nothing;

        if (Picked.Count == 0)
        {
            Picked.Add(line);
            _firstPick = pick.World;
            return EditPlan.Nothing;
        }

        var first = (SLine)Picked[0];
        Picked.Clear();

        // The same line twice has no corner with itself.
        if (ReferenceEquals(first, line)) return EditPlan.Nothing;

        return Build(first, _firstPick, line, pick.World);
    }

    protected virtual EditPlan Build(SLine first, Vec2 firstPick, SLine second, Vec2 secondPick) =>
        Corners.Fillet(first, firstPick, second, secondPick, Radius);
}

/// <summary>The same two picks as a fillet, cut straight instead of rounded.</summary>
public sealed class ChamferTool : FilletTool
{
    public override string Name => "chamfer";

    protected override string Opening => Radius > 0
        ? $"Chamfer (distance {Radius:0.###}): click the first line, on the side to keep"
        : "Chamfer (distance 0, a sharp corner): click the first line, on the side to keep";

    protected override EditPlan Build(SLine first, Vec2 firstPick, SLine second, Vec2 secondPick) =>
        Corners.Chamfer(first, firstPick, second, secondPick, Radius);
}

/// <summary>
/// Moves a circle until it just touches a line: pick the circle, then the
/// line it should sit against.
/// </summary>
public sealed class TangentMateTool : EntityTool
{
    public override string Name => "tangent";

    public override string Prompt => Picked.Count == 0
        ? "Tangent: click the circle to move"
        : "Tangent: click the line it should touch";

    /// <summary>The move, once both have been picked. Null until then.</summary>
    public Vec2? Offset { get; private set; }

    public override EditPlan Click(in EntityPick pick)
    {
        Offset = null;

        if (Picked.Count == 0)
        {
            if (pick.Entity is SCircle circle) Picked.Add(circle);
            return EditPlan.Nothing;
        }

        if (pick.Entity is not SLine line)
        {
            Picked.Clear();
            return EditPlan.Nothing;
        }

        var target = (SCircle)Picked[0];
        Picked.Clear();

        // A translation, not a replacement: the circle keeps its identity and
        // its handle, so the canvas turns this into a transform command.
        Offset = Tangency.ToLine(target, line);
        Target = target;

        return EditPlan.Nothing;
    }

    /// <summary>The circle the pending <see cref="Offset"/> applies to.</summary>
    public SCircle? Target { get; private set; }
}
