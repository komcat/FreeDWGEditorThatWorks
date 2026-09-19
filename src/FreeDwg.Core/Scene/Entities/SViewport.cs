using FreeDwg.Core.Geometry;
using FreeDwg.Core.Picking;
using FreeDwg.Core.Snapping;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Scene.Entities;

/// <summary>
/// A window in a paper space sheet showing model space at a fixed scale.
/// </summary>
/// <remarks>
/// Structurally this is a block instance whose "definition" is model space:
/// the same geometry, placed under a transform and clipped to a rectangle.
/// The differences that matter are that the transform is derived from a view
/// (a centre and a height in model units) rather than stored directly, and
/// that a viewport can freeze layers for itself alone.
/// </remarks>
public sealed class SViewport : SceneEntity
{
    public SViewport(Layout modelSpace, Bounds2 paperRect, Vec2 viewCenter, double viewHeight)
    {
        ModelSpace = modelSpace;
        PaperRect = paperRect;
        ViewCenter = viewCenter;
        ViewHeight = viewHeight;
    }

    /// <summary>The layout this window looks into; always model space.</summary>
    public Layout ModelSpace { get; }

    /// <summary>The window itself, in paper space units.</summary>
    public Bounds2 PaperRect { get; set; }

    /// <summary>Model point shown at the centre of the window.</summary>
    public Vec2 ViewCenter { get; set; }

    /// <summary>Model units spanned vertically by the window; sets the scale.</summary>
    public double ViewHeight { get; set; }

    public double TwistAngle { get; set; }

    public bool IsOn { get; set; } = true;

    /// <summary>Layer indices frozen in this viewport alone, or null for none.</summary>
    public IReadOnlySet<int>? FrozenLayers { get; set; }

    /// <summary>Whether to stroke the window outline, as AutoCAD does.</summary>
    public bool ShowBorder { get; set; } = true;

    /// <summary>Paper units per model unit.</summary>
    public double Scale => ViewHeight > 0 ? PaperRect.Height / ViewHeight : 0;

    /// <summary>Model space to paper space for this window.</summary>
    public Mat3 ModelToPaper =>
        Mat3.Translation(-ViewCenter)
        * Mat3.Rotation(-TwistAngle)
        * Mat3.Scaling(Scale)
        * Mat3.Translation(PaperRect.Center);

    protected override Bounds2 ComputeBounds() => PaperRect;

    public override void Emit(in EmitContext context, in DisplayStyle style)
    {
        if (PaperRect.IsEmpty) return;

        var sink = context.Sink;

        if (ShowBorder) EmitBorder(sink, style);

        if (!IsOn || ViewHeight <= 0 || context.Depth >= EmitContext.MaxDepth) return;

        var toPaper = ModelToPaper;
        if (!toPaper.TryInvert(out var toModel)) return;

        // Only the slice of model space framed by the window can appear, so
        // cull against it here rather than relying on the top-level pass,
        // which never sees inside a viewport.
        Bounds2 visibleModel = toModel.TransformBounds(PaperRect);

        sink.PushClip(PaperRect);
        sink.PushTransform(toPaper);

        var nested = context.Nested() with
        {
            PixelsPerUnit = context.PixelsPerUnit * Scale,
            FrozenLayers = FrozenLayers,
        };

        foreach (var entity in ModelSpace.Entities)
        {
            if (!nested.IsLayerVisible(entity.LayerIndex)) continue;

            var bounds = entity.Bounds;
            if (!bounds.IsEmpty && !bounds.Intersects(visibleModel)) continue;

            entity.Emit(nested, entity.StyleWithin(style));
        }

        sink.PopTransform();
        sink.PopClip();
    }

    private void EmitBorder(IDrawingSink sink, in DisplayStyle style)
    {
        sink.BeginFigure(new Vec2(PaperRect.MinX, PaperRect.MinY), closed: true, style);
        sink.LineTo(new Vec2(PaperRect.MaxX, PaperRect.MinY));
        sink.LineTo(new Vec2(PaperRect.MaxX, PaperRect.MaxY));
        sink.LineTo(new Vec2(PaperRect.MinX, PaperRect.MaxY));
        sink.EndFigure();
    }

    /// <summary>
    /// Picked by its frame alone. What is inside belongs to model space and is
    /// selected there; on a sheet, the object under the cursor is the window.
    /// </summary>
    public override double DistanceTo(Vec2 point, in PickContext context) =>
        Distance.PointToRectEdge(point, PaperRect);

    public override bool IntersectsRect(Bounds2 rect, in PickContext context) =>
        !PaperRect.IsEmpty && Intersect.PolylineWithRect(Distance.Corners(PaperRect), closed: true, rect);

    public override void CollectSnapPoints(SnapModes modes, ICollection<SnapCandidate> into)
    {
        if (PaperRect.IsEmpty) return;

        if (modes.HasFlag(SnapModes.Endpoint))
            foreach (var corner in Distance.Corners(PaperRect))
                into.Add(new SnapCandidate(corner, SnapKind.Endpoint));

        if (modes.HasFlag(SnapModes.Center))
            into.Add(new SnapCandidate(PaperRect.Center, SnapKind.Center));
    }

    protected override void TransformGeometry(in Mat3 transform) =>
        PaperRect = transform.TransformBounds(PaperRect);
}
