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
    private readonly List<CurvePiece> _curves = new();
    private readonly List<Vec2> _feet = new();
    private readonly List<Vec2> _tracked = new();

    /// <summary>Curve pieces near the cursor, with the entity each came from.</summary>
    private readonly List<CurvePiece> _pieces = new();
    private readonly List<int> _owners = new();
    private readonly List<Vec2> _crossings = new();

    /// <summary>
    /// A ceiling on the pieces considered for crossings. Pairing them is
    /// quadratic, and a dense hatch under the cursor can offer thousands --
    /// only the ones nearest the cursor could win anyway, so the rest are
    /// work with no possible outcome.
    /// </summary>
    public const int MaxCrossingPieces = 192;

    /// <summary>
    /// How many points stay acquired for tracking. Two, because the useful
    /// case is lining up with one across and another up, and a longer memory
    /// turns the screen into a cat's cradle.
    /// </summary>
    public const int MaxTracked = 2;

    /// <summary>Points the cursor has rested on, newest last.</summary>
    public IReadOnlyList<Vec2> Tracked => _tracked;

    /// <summary>
    /// Remembers a point to line up with later.
    /// </summary>
    /// <remarks>
    /// AutoCAD makes you hover for about a second before a point is
    /// acquired. Here it is taken as soon as the cursor is on one, which is
    /// less deliberate but needs no timer and no explaining -- and the guides
    /// only appear when the cursor is actually lined up with something, so
    /// the extra points cost nothing on screen.
    /// </remarks>
    public void Acquire(Vec2 point)
    {
        for (int i = 0; i < _tracked.Count; i++)
        {
            if (Vec2.Distance(_tracked[i], point) > 1e-9) continue;

            // Already held: move it to the front of the queue rather than
            // letting a repeat push out the other one.
            _tracked.RemoveAt(i);
            break;
        }

        _tracked.Add(point);
        while (_tracked.Count > MaxTracked) _tracked.RemoveAt(0);
    }

    public void ClearTracking() => _tracked.Clear();

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
        if (FindObjectSnap(layout, layers, cursor, tolerance, from) is { Found: true } hit) return hit;

        // Tracking sits under the object snaps and over ortho: it is a
        // deliberate alignment with something real, where ortho is only a
        // constraint on the direction of travel.
        if (Modes.HasFlag(SnapModes.Tracking) && TryTrack(cursor, tolerance) is { Found: true } tracked)
            return tracked;

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
    public SnapResult FindObjectSnap(Layout layout, IReadOnlyList<Layer> layers, Vec2 cursor,
        double tolerance, Vec2? from = null)
    {
        var wanted = Modes & SnapModes.Objects;
        if (wanted == SnapModes.None) return SnapResult.Miss(cursor);

        var area = Bounds2.FromPoint(cursor).Inflate(tolerance);
        var index = layout.Index;

        _nearby.Clear();
        index.Query(area, _nearby);

        _pieces.Clear();
        _owners.Clear();

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

            // Perpendicular and tangent depend on where the line is coming
            // from, so they cannot be offered by an entity on its own.
            if (from is { } origin) AddProjected(entity, origin, wanted, tolerance);

            if (wanted.HasFlag(SnapModes.Intersection)) CollectNearbyPieces(entity, position, area, tolerance);

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

        if (wanted.HasFlag(SnapModes.Intersection))
        {
            foreach (var crossing in Crossings())
            {
                double distance = Vec2.Distance(crossing, cursor);
                if (distance > bestDistance) continue;

                bestDistance = distance;
                best = new SnapResult(crossing, SnapKind.Intersection);
            }
        }

        return best;
    }

    /// <summary>
    /// Keeps the curve pieces of one entity that come near the cursor, for
    /// the crossing pass to pair up afterwards.
    /// </summary>
    private void CollectNearbyPieces(SceneEntity entity, int owner, Bounds2 area, double tolerance)
    {
        if (_pieces.Count >= MaxCrossingPieces) return;

        _curves.Clear();
        entity.CollectCurves(_curves, Math.Max(tolerance / 4, 1e-9));

        foreach (var piece in _curves)
        {
            if (_pieces.Count >= MaxCrossingPieces) return;
            if (!piece.Bounds.Inflate(tolerance).Intersects(area)) continue;

            _pieces.Add(piece);
            _owners.Add(owner);
        }
    }

    /// <summary>
    /// Every point where two of the collected pieces cross.
    /// </summary>
    /// <remarks>
    /// Pieces of the same entity are not paired with each other. Adjacent
    /// segments of a polyline meet at every vertex, and reporting those as
    /// crossings would offer each vertex twice -- once correctly as an
    /// endpoint and once under a marker that means something else. The cost
    /// is that a polyline crossing itself offers nothing, which is rare and
    /// far less confusing than the alternative.
    /// </remarks>
    private List<Vec2> Crossings()
    {
        _crossings.Clear();

        for (int i = 0; i < _pieces.Count; i++)
        {
            for (int j = i + 1; j < _pieces.Count; j++)
            {
                if (_owners[i] == _owners[j]) continue;

                Intersection.Between(_pieces[i], _pieces[j], _crossings);
            }
        }

        return _crossings;
    }

    /// <summary>
    /// Adds the right-angle and tangent points this entity offers from a
    /// given origin, working on its curves rather than its snap points.
    /// </summary>
    private void AddProjected(SceneEntity entity, Vec2 from, SnapModes wanted, double tolerance)
    {
        bool perpendicular = wanted.HasFlag(SnapModes.Perpendicular);
        bool tangent = wanted.HasFlag(SnapModes.Tangent);
        if (!perpendicular && !tangent) return;

        _curves.Clear();
        entity.CollectCurves(_curves, Math.Max(tolerance / 4, 1e-9));

        foreach (var piece in _curves)
        {
            if (perpendicular)
            {
                if (!piece.IsArc)
                {
                    if (Projection.PerpendicularToSegment(from, piece.A, piece.B, out var foot))
                        _candidates.Add(new SnapCandidate(foot, SnapKind.Perpendicular));
                }
                else
                {
                    _feet.Clear();
                    Projection.PerpendicularToArc(from, piece, _feet);

                    foreach (var foot in _feet)
                        _candidates.Add(new SnapCandidate(foot, SnapKind.Perpendicular));
                }
            }

            // A straight line is its own tangent everywhere, so there is no
            // single point to offer; only arcs have one.
            if (!tangent || !piece.IsArc) continue;

            _feet.Clear();
            Projection.TangentToArc(from, piece, _feet);

            foreach (var touch in _feet) _candidates.Add(new SnapCandidate(touch, SnapKind.Tangent));
        }
    }

    /// <summary>
    /// Lines the cursor up with a point it rested on earlier: level with it,
    /// directly above it, or where two such lines cross.
    /// </summary>
    private SnapResult TryTrack(Vec2 cursor, double tolerance)
    {
        if (_tracked.Count == 0) return SnapResult.Miss(cursor);

        // A crossing first: it pins both coordinates and is what someone
        // reaching for the corner of two existing features actually wants.
        foreach (var across in _tracked)
        {
            if (Math.Abs(cursor.Y - across.Y) > tolerance) continue;

            foreach (var up in _tracked)
            {
                if (Math.Abs(cursor.X - up.X) > tolerance) continue;

                return new SnapResult(new Vec2(up.X, across.Y), SnapKind.Tracking)
                {
                    Guides = ReferenceEquals(across, up) ? [across] : [across, up],
                };
            }
        }

        var best = SnapResult.Miss(cursor);
        double nearest = tolerance;

        foreach (var point in _tracked)
        {
            double horizontal = Math.Abs(cursor.Y - point.Y);
            if (horizontal < nearest)
            {
                nearest = horizontal;
                best = new SnapResult(new Vec2(cursor.X, point.Y), SnapKind.Tracking) { Guides = [point] };
            }

            double vertical = Math.Abs(cursor.X - point.X);
            if (vertical < nearest)
            {
                nearest = vertical;
                best = new SnapResult(new Vec2(point.X, cursor.Y), SnapKind.Tracking) { Guides = [point] };
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
        SnapKind.Perpendicular => SnapModes.Perpendicular,
        SnapKind.Tangent => SnapModes.Tangent,
        SnapKind.Intersection => SnapModes.Intersection,
        SnapKind.Tracking => SnapModes.Tracking,
        _ => SnapModes.None,
    };
}
