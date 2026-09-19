using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene;

namespace FreeDwg.Core.Snapping;

/// <summary>
/// Turns where the cursor is into where the point should go.
/// </summary>
/// <remarks>
/// Object snap is what separates drafting from drawing: without it, two
/// lines that look joined are joined to within a pixel, which is to say not
/// joined at all, and every downstream operation -- trim, fillet, area,
/// export -- is then working on a drawing full of near misses.
/// </remarks>
public sealed class SnapEngine
{
    /// <summary>Which snaps are switched on. Object snaps default to the useful ones.</summary>
    public SnapModes Modes { get; set; } = SnapModes.Endpoint | SnapModes.Midpoint | SnapModes.Center;

    /// <summary>Whether points are squared up with the previous one.</summary>
    public bool Ortho { get; set; }

    public Grid Grid { get; } = new();

    /// <summary>Reused between frames; snapping runs on every mouse move.</summary>
    private readonly List<SnapCandidate> _candidates = new();
    private readonly List<int> _nearby = new();

    /// <summary>
    /// Where a point picked at <paramref name="cursor"/> should actually go.
    /// </summary>
    /// <param name="from">
    /// The previous point of the object being drawn, for ortho. Null when
    /// there is nothing to be square with.
    /// </param>
    /// <remarks>
    /// The order is object snap, then ortho, then grid, and it is a strict
    /// order rather than a combination. An object snap is the user pointing
    /// at a specific existing point and must win outright -- squaring it up
    /// afterwards would move it off the thing they aimed at, which is the
    /// one outcome nobody wants.
    /// </remarks>
    public SnapResult Resolve(Layout layout, IReadOnlyList<Layer> layers,
        Vec2 cursor, double tolerance, Vec2? from = null, double pixelsPerUnit = 1)
    {
        if (FindObjectSnap(layout, layers, cursor, tolerance) is { Found: true } hit) return hit;

        if (Ortho && from is { } previous) return SnapResult.Miss(Snapping.Ortho.Constrain(previous, cursor));

        if (Modes.HasFlag(SnapModes.Grid))
        {
            double spacing = Grid.EffectiveSpacing(pixelsPerUnit);
            var point = Grid.Nearest(cursor, spacing);

            // Only if the cursor is actually near a crossing; otherwise a
            // coarse grid would drag every point a long way from the aim.
            if (Vec2.Distance(point, cursor) <= Math.Max(tolerance, spacing / 2))
                return new SnapResult(point, SnapKind.Grid);
        }

        return SnapResult.Miss(cursor);
    }

    /// <summary>
    /// The nearest point offered by a nearby entity, or a miss. Public so
    /// that the cursor can show a marker without committing to a point.
    /// </summary>
    public SnapResult FindObjectSnap(Layout layout, IReadOnlyList<Layer> layers, Vec2 cursor, double tolerance)
    {
        var wanted = Modes & SnapModes.Objects;
        if (wanted == SnapModes.None) return SnapResult.Miss(cursor);

        var area = Bounds2.FromPoint(cursor).Inflate(tolerance);
        var index = layout.Index;

        _nearby.Clear();
        index.Query(area, _nearby);

        var best = SnapResult.Miss(cursor);
        double bestDistance = tolerance;

        foreach (int position in _nearby)
        {
            var entity = index[position];

            // A layer you cannot see or select is not one to snap to either.
            if ((uint)entity.LayerIndex < (uint)layers.Count)
            {
                var layer = layers[entity.LayerIndex];
                if (!layer.IsVisible || layer.IsLocked) continue;
            }

            _candidates.Clear();
            entity.CollectSnapPoints(wanted, _candidates);

            foreach (var candidate in _candidates)
            {
                if ((wanted & ToMode(candidate.Kind)) == 0) continue;

                double distance = Vec2.Distance(candidate.Point, cursor);
                if (distance > bestDistance) continue;

                // Strictly nearer wins, so an entity drawn later cannot steal
                // a snap from one the cursor is sitting exactly on.
                bestDistance = distance;
                best = new SnapResult(candidate.Point, candidate.Kind);
            }
        }

        return best;
    }

    private static SnapModes ToMode(SnapKind kind) => kind switch
    {
        SnapKind.Endpoint => SnapModes.Endpoint,
        SnapKind.Midpoint => SnapModes.Midpoint,
        SnapKind.Center => SnapModes.Center,
        SnapKind.Quadrant => SnapModes.Quadrant,
        SnapKind.Grid => SnapModes.Grid,
        _ => SnapModes.None,
    };
}
