using FreeDwg.Core.Editing;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Picking;
using FreeDwg.Core.Snapping;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Scene.Entities;

/// <summary>
/// One placement of a <see cref="BlockDefinition"/>.
/// </summary>
/// <remarks>
/// The block's geometry is not copied here, only referenced and transformed.
/// That keeps a drawing with thousands of identical symbols small, and means
/// that once the editor can modify a block definition, every instance updates
/// with it.
/// </remarks>
public sealed class SInsert : SceneEntity
{
    public SInsert(BlockDefinition block, Mat3 transform)
    {
        Block = block;
        Placement = transform;
    }

    public BlockDefinition Block { get; set; }

    /// <summary>
    /// Block-local space to the space containing this insert.
    /// </summary>
    /// <remarks>
    /// Not called Transform, because <see cref="SceneEntity.Transform"/> is
    /// the verb every entity answers to and a property of the same name would
    /// quietly hide it.
    /// </remarks>
    public Mat3 Placement { get; set; }

    protected override Bounds2 ComputeBounds() => Placement.TransformBounds(Block.Bounds);

    public override void Emit(in EmitContext context, in DisplayStyle style)
    {
        if (context.Depth >= EmitContext.MaxDepth) return;

        context.Sink.PushTransform(Placement);

        // A scaled block changes what a drawing unit is worth on screen, which
        // is what the curve entities inside it use to choose their sampling.
        double scale = Math.Sqrt(Math.Abs(Placement.Determinant));
        var nested = context.Nested() with
        {
            PixelsPerUnit = context.PixelsPerUnit * (scale > 0 ? scale : 1.0),
        };

        foreach (var child in Block.Entities)
        {
            // Layer visibility applies inside blocks too: freezing a layer has
            // to hide the geometry a block instance draws on it.
            if (!nested.IsLayerVisible(child.LayerIndex)) continue;

            child.Emit(nested, child.StyleWithin(style));
        }

        context.Sink.PopTransform();
    }

    /// <summary>
    /// Builds the standard DWG insert transform: block geometry is defined
    /// about the base point, which is then scaled, rotated and moved to the
    /// insertion point.
    /// </summary>
    public static Mat3 BuildTransform(Vec2 basePoint, Vec2 insertPoint, double scaleX, double scaleY, double rotation) =>
        Mat3.Translation(-basePoint)
        * Mat3.Scaling(scaleX, scaleY)
        * Mat3.Rotation(rotation)
        * Mat3.Translation(insertPoint);

    /// <summary>
    /// Hit tested in block-local space, so one definition serves every
    /// instance however it is placed -- the same trade as instancing the
    /// geometry rather than copying it.
    /// </summary>
    public override double DistanceTo(Vec2 point, in PickContext context)
    {
        if (context.Depth >= PickContext.MaxDepth) return double.PositiveInfinity;
        if (!Placement.TryInvert(out var toLocal)) return double.PositiveInfinity;

        double scale = Placement.UniformScale;
        if (scale <= 1e-300) return double.PositiveInfinity;

        var nested = context.Nested(scale);
        Vec2 local = toLocal.Transform(point);

        double best = double.PositiveInfinity;
        foreach (var child in Block.Entities)
        {
            if (!nested.IsLayerPickable(child.LayerIndex)) continue;

            double d = child.DistanceTo(local, nested);
            if (d < best) best = d;
        }

        // Back to the caller's units. Exact for the similarity transforms
        // inserts almost always carry; for a non-uniform scale it is off by
        // at most the ratio of the two axes, which no click can notice.
        return best * scale;
    }

    public override bool IntersectsRect(Bounds2 rect, in PickContext context)
    {
        if (context.Depth >= PickContext.MaxDepth || !Bounds.Intersects(rect)) return false;
        if (!Placement.TryInvert(out var toLocal)) return false;

        double scale = Placement.UniformScale;
        if (scale <= 1e-300) return false;

        // A rotated instance turns the rectangle into a rotated one, which the
        // children cannot test against; its bounds stand in. That over-selects
        // slightly at the corners of a rotated block and never under-selects.
        Bounds2 localRect = toLocal.TransformBounds(rect);
        var nested = context.Nested(scale);

        foreach (var child in Block.Entities)
        {
            if (!nested.IsLayerPickable(child.LayerIndex)) continue;
            if (child.IntersectsRect(localRect, nested)) return true;
        }

        return false;
    }

    /// <summary>
    /// The block's own snap points, moved into place. Symbols are the thing
    /// most worth snapping to in a real drawing, so an insert that offered
    /// nothing would make object snap close to useless.
    /// </summary>
    public override void CollectSnapPoints(SnapModes modes, ICollection<SnapCandidate> into)
    {
        var local = new List<SnapCandidate>();
        foreach (var child in Block.Entities) child.CollectSnapPoints(modes, local);

        foreach (var candidate in local)
            into.Add(candidate with { Point = Placement.Transform(candidate.Point) });
    }

    public override void CollectGrips(ICollection<Grip> into) =>
        // The insertion point. Scale and rotation live in the placement
        // matrix, and a handle that reached into one would be editing the
        // reference rather than moving it.
        into.Add(new Grip(Placement.Transform(Vec2.Zero), GripRole.Move));

    protected override void TransformGeometry(in Mat3 transform)
    {
        // Compose rather than touch the definition: the whole point of an
        // insert is that moving one does not move the other nine hundred.
        Placement = Placement * transform;
    }

    public override void CollectCurves(ICollection<CurvePiece> into, double tolerance)
    {
        // Block geometry can be trimmed *to*, though not trimmed itself: the
        // definition is shared, so cutting it would cut every instance.
        var local = new List<CurvePiece>();
        double scale = Placement.UniformScale;

        foreach (var child in Block.Entities)
            child.CollectCurves(local, scale > 0 ? tolerance / scale : tolerance);

        foreach (var piece in local)
        {
            if (piece.IsArc && Placement.IsSimilarity)
            {
                Vec2 start = Placement.Transform(piece.A);
                Vec2 center = Placement.Transform(piece.Center);

                into.Add(CurvePiece.Arc(center, piece.Radius * scale,
                    (start - center).Angle(), Placement.IsMirror ? -piece.Sweep : piece.Sweep));
                continue;
            }

            if (!piece.IsArc)
            {
                into.Add(CurvePiece.Segment(Placement.Transform(piece.A), Placement.Transform(piece.B)));
                continue;
            }

            // A non-uniform scale turns the arc into an ellipse, which has no
            // CurvePiece; flatten it rather than pretend it is still an arc.
            var points = ArcMath.Tessellate(piece.Center, piece.Radius, piece.StartAngle, piece.Sweep,
                Resolution.DeviceRadius(piece.Radius, tolerance));

            for (int i = 1; i < points.Length; i++)
                into.Add(CurvePiece.Segment(Placement.Transform(points[i - 1]), Placement.Transform(points[i])));
        }
    }
}
