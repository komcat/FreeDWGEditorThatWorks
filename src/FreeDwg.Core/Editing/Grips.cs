using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene;

namespace FreeDwg.Core.Editing;

/// <summary>What dragging a grip does.</summary>
/// <remarks>
/// Two roles rather than one per handle kind, because this is the question
/// the shell has to answer: a move is a transform of the whole entity and
/// goes through <c>TransformEntities</c>, a shape change is a new geometry
/// and goes through <c>ReplaceEntities</c>. Everything else about a grip is
/// the entity's own business.
/// </remarks>
public enum GripRole
{
    /// <summary>Drags the whole entity along with it: a circle's centre, a text's position.</summary>
    Move,

    /// <summary>Changes the shape: a vertex, an endpoint, a radius.</summary>
    Shape,
}

/// <summary>
/// One handle on an entity, in world coordinates.
/// </summary>
/// <param name="Index">
/// Which handle this is, in whatever terms the entity chose. It is handed
/// straight back to <see cref="SceneEntity.MoveGrip"/>, so only the entity
/// that offered it needs to know what it means.
/// </param>
public readonly record struct Grip(Vec2 Point, GripRole Role, int Index = 0);

/// <summary>A grip together with the entity that offered it.</summary>
public readonly record struct EntityGrip(SceneEntity Entity, Grip Grip)
{
    public Vec2 Point => Grip.Point;
}

/// <summary>
/// The grips of a selection, and which one a point is near.
/// </summary>
/// <remarks>
/// Grip positions are world geometry and grip hit testing is arithmetic, so
/// both live here rather than in the canvas: what the shell is left with is
/// painting a few squares at a fixed pixel size, which is all a device-space
/// overlay should ever have been.
/// </remarks>
public sealed class GripSet
{
    /// <summary>
    /// How many selected entities still get grips.
    /// </summary>
    /// <remarks>
    /// A crossing window over a drawing can select tens of thousands of
    /// objects, and grips on all of them would be a screenful of squares
    /// with no geometry visible between them -- and a per-frame cost for
    /// handles nobody could aim at. AutoCAD draws the same line at a hundred
    /// objects, for the same reason.
    /// </remarks>
    public const int MaxEntities = 100;

    private readonly List<EntityGrip> _grips = new();
    private readonly List<Grip> _scratch = new();

    public IReadOnlyList<EntityGrip> Grips => _grips;

    public int Count => _grips.Count;

    public bool IsEmpty => _grips.Count == 0;

    public void Clear() => _grips.Clear();

    /// <summary>Collects the grips of everything in <paramref name="selection"/>.</summary>
    public void Rebuild(IReadOnlyList<SceneEntity> selection)
    {
        _grips.Clear();
        if (selection.Count is 0 or > MaxEntities) return;

        foreach (var entity in selection)
        {
            _scratch.Clear();
            entity.CollectGrips(_scratch);

            foreach (var grip in _scratch) _grips.Add(new EntityGrip(entity, grip));
        }
    }

    /// <summary>
    /// The grip nearest <paramref name="point"/> within
    /// <paramref name="tolerance"/> world units, or null.
    /// </summary>
    /// <remarks>
    /// Nearest rather than first, and ties go to the later one, which is the
    /// same rule picking uses: where two grips coincide -- the shared corner
    /// of two selected polylines -- the one on top is the one being aimed at.
    /// </remarks>
    public EntityGrip? Nearest(Vec2 point, double tolerance)
    {
        EntityGrip? best = null;
        double bestDistance = tolerance;

        foreach (var candidate in _grips)
        {
            double distance = Vec2.Distance(candidate.Point, point);
            if (distance > bestDistance) continue;

            best = candidate;
            bestDistance = distance;
        }

        return best;
    }
}
