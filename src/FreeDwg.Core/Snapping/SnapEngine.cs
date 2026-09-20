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
    /// <remarks>
    /// A constraint rather than an attraction, so it takes precedence over
    /// polar tracking out of the same point: with ortho on, the answer is
    /// always on an axis and polar has nothing left to offer.
    /// </remarks>
    public bool Ortho { get; set; }

    /// <summary>
    /// The angle between polar rays, in degrees. Forty-five gives the
    /// diagonals as well as the axes; fifteen is the other common answer.
    /// </summary>
    public double PolarAngle { get; set; } = Polar.DefaultIncrement;

    /// <summary>
    /// Whether polar angles are measured from the previous segment rather
    /// than from east.
    /// </summary>
    /// <remarks>
    /// Relative is what drafting usually means by an angle: the next run of
    /// a polyline turns thirty degrees from the last one, not thirty degrees
    /// from the horizon. With nothing drawn yet there is no previous segment
    /// to be relative to, and it falls back to east on its own.
    /// <para>
    /// It applies only to the rays out of the point being drawn from.
    /// Tracking rays stay absolute: lining up level with a corner is the
    /// whole point of them, and rotating them with the last segment would
    /// take that away.
    /// </para>
    /// </remarks>
    public bool PolarRelative { get; set; } = true;

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
    private readonly List<Vec2> _origins = new();
    private readonly List<SnapKind> _originKinds = new();
    private readonly List<double> _originBases = new();

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
        Vec2 cursor, double tolerance, Vec2? from = null, double pixelsPerUnit = 1,
        Vec2? before = null)
    {
        if (FindObjectSnap(layout, layers, cursor, tolerance, from) is { Found: true } hit) return hit;

        // Alignment sits under the object snaps and over ortho: it is a
        // deliberate line-up with something real, where ortho is only a
        // constraint on the direction of travel.
        if (TryAlign(cursor, tolerance, from, before) is { Found: true } aligned) return aligned;

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
        double bestPoint = double.PositiveInfinity;

        // Snaps whose answer is under the cursor rank above the ones worked
        // out from it, whatever the distances say.
        int bestTier = int.MaxValue;

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
            if (from is { } origin) AddProjected(entity, origin, cursor, wanted, tolerance);

            if (wanted.HasFlag(SnapModes.Intersection)) CollectNearbyPieces(entity, position, area, tolerance);

            foreach (var candidate in _candidates)
            {
                if ((wanted & ToMode(candidate.Kind)) == 0) continue;

                double reach = candidate.ReachFrom(cursor);
                if (reach > tolerance) continue;

                int tier = candidate.IsProjected ? 1 : 0;
                if (tier > bestTier) continue;

                double toPoint = Vec2.Distance(candidate.Point, cursor);

                if (tier == bestTier)
                {
                    if (reach > bestDistance) continue;

                    // Equally good aims are settled by which answer is nearer,
                    // so an entity drawn later cannot steal a snap from one
                    // the cursor is sitting exactly on.
                    if (Math.Abs(reach - bestDistance) < 1e-12 && toPoint >= bestPoint) continue;
                }

                bestTier = tier;
                bestDistance = reach;
                bestPoint = toPoint;
                best = new SnapResult(candidate.Point, candidate.Kind);
            }
        }

        if (wanted.HasFlag(SnapModes.Intersection))
        {
            foreach (var crossing in Crossings())
            {
                double distance = Vec2.Distance(crossing, cursor);
                if (distance > tolerance) continue;

                // A crossing is where it says it is, so it outranks anything
                // worked out from elsewhere however near that one aimed.
                if (bestTier == 0 && distance > bestDistance) continue;

                bestTier = 0;
                bestDistance = distance;
                bestPoint = distance;
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
    private void AddProjected(SceneEntity entity, Vec2 from, Vec2 cursor, SnapModes wanted, double tolerance)
    {
        bool perpendicular = wanted.HasFlag(SnapModes.Perpendicular);
        bool tangent = wanted.HasFlag(SnapModes.Tangent);
        if (!perpendicular && !tangent) return;

        _curves.Clear();
        entity.CollectCurves(_curves, Math.Max(tolerance / 4, 1e-9));

        foreach (var piece in _curves)
        {
            // What the cursor is actually aiming at is the curve. The foot
            // of a right angle, and a tangent's touch point, can both be a
            // long way along it from where you are pointing.
            double reach = Reach(piece, cursor);
            if (reach > tolerance) continue;

            if (perpendicular)
            {
                if (!piece.IsArc)
                {
                    if (Projection.PerpendicularToSegment(from, piece.A, piece.B, out var foot))
                        _candidates.Add(new SnapCandidate(foot, SnapKind.Perpendicular) { Reach = reach });
                }
                else
                {
                    _feet.Clear();
                    Projection.PerpendicularToArc(from, piece, _feet);

                    foreach (var foot in _feet)
                        _candidates.Add(new SnapCandidate(foot, SnapKind.Perpendicular) { Reach = reach });
                }
            }

            // A straight line is its own tangent everywhere, so there is no
            // single point to offer; only arcs have one.
            if (!tangent || !piece.IsArc) continue;

            _feet.Clear();
            Projection.TangentToArc(from, piece, _feet);

            foreach (var touch in _feet)
                _candidates.Add(new SnapCandidate(touch, SnapKind.Tangent) { Reach = reach });
        }
    }

    /// <summary>How far the cursor is from a curve piece itself.</summary>
    private static double Reach(in CurvePiece piece, Vec2 cursor) => piece.IsArc
        ? Distance.PointToArc(cursor, piece.Center, piece.Radius, piece.StartAngle, piece.Sweep)
        : Distance.PointToSegment(cursor, piece.A, piece.B);

    /// <summary>
    /// Lines the cursor up along the polar rays out of the points it has
    /// rested on, and out of the point being drawn from.
    /// </summary>
    /// <remarks>
    /// One mechanism for two features. Tracking runs the rays out of acquired
    /// points; polar runs them out of the point the line started at. They
    /// differ only in where the rays begin, so a crossing between one of each
    /// falls out for free -- and that crossing, a known height met at a known
    /// angle, is the most useful thing here.
    /// </remarks>
    private SnapResult TryAlign(Vec2 cursor, double tolerance, Vec2? from, Vec2? before)
    {
        _origins.Clear();
        _originKinds.Clear();
        _originBases.Clear();

        if (Modes.HasFlag(SnapModes.Tracking))
        {
            foreach (var point in _tracked)
            {
                _origins.Add(point);
                _originKinds.Add(SnapKind.Tracking);
                _originBases.Add(0);
            }
        }

        // Ortho already pins the direction out of this point, so polar has
        // nothing left to say about it.
        if (Modes.HasFlag(SnapModes.Polar) && !Ortho && from is { } start)
        {
            _origins.Add(start);
            _originKinds.Add(SnapKind.Polar);
            _originBases.Add(BaseAngle(start, before));
        }

        if (_origins.Count == 0) return SnapResult.Miss(cursor);

        int rays = Polar.Count(PolarAngle);

        // A crossing first: it pins the point outright, where a single ray
        // leaves it free to slide along.
        for (int a = 0; a < _origins.Count; a++)
        {
            for (int b = a + 1; b < _origins.Count; b++)
            {
                for (int i = 0; i < rays; i++)
                {
                    for (int j = 0; j < rays; j++)
                    {
                        if (!Polar.Cross(_origins[a], Polar.Direction(PolarAngle, i, _originBases[a]),
                                _origins[b], Polar.Direction(PolarAngle, j, _originBases[b]), out var crossing))
                        {
                            continue;
                        }

                        if (Vec2.Distance(crossing, cursor) > tolerance) continue;

                        return new SnapResult(crossing, SnapKind.Tracking)
                        {
                            Guides = [_origins[a], _origins[b]],
                        };
                    }
                }
            }
        }

        var best = SnapResult.Miss(cursor);
        double nearest = tolerance;

        for (int origin = 0; origin < _origins.Count; origin++)
        {
            for (int i = 0; i < rays; i++)
            {
                if (!Polar.Project(_origins[origin], Polar.Direction(PolarAngle, i, _originBases[origin]),
                        cursor, tolerance, out var point, out double offset))
                {
                    continue;
                }

                if (offset >= nearest) continue;

                nearest = offset;
                best = new SnapResult(point, _originKinds[origin]) { Guides = [_origins[origin]] };
            }
        }

        return best;
    }

    /// <summary>
    /// Which way step zero points out of the point being drawn from: along
    /// the previous segment when there is one and relative angles are
    /// wanted, and east otherwise.
    /// </summary>
    private double BaseAngle(Vec2 from, Vec2? before)
    {
        if (!PolarRelative || before is not { } previous) return 0;

        Vec2 run = from - previous;
        return run.LengthSquared < 1e-24 ? 0 : run.Angle();
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
        SnapKind.Polar => SnapModes.Polar,
        SnapKind.Tracking => SnapModes.Tracking,
        _ => SnapModes.None,
    };
}
