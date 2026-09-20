using FreeDwg.Core.Geometry;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Tools;

/// <summary>
/// Anything that draws a dimension.
/// </summary>
/// <remarks>
/// The sizes come from the canvas rather than from a figure baked in here,
/// the way the fillet radius does and for the same reason: two copies of a
/// setting drift, and text sized for a millimetre drawing is invisible in a
/// drawing measured in metres. A tool started before or after the document
/// units are changed draws the same marks.
/// </remarks>
public abstract class DimensionTool : DrawTool
{
    /// <summary>Text height, arrow size and number format, set by the canvas.</summary>
    public DimensionStyle Sizes { get; set; } = DimensionStyle.Iso;

    protected abstract string Title { get; }

    /// <summary>Stamps a freshly built dimension with those sizes.</summary>
    protected SDimension Sized(SDimension dimension)
    {
        dimension.DimensionStyle = Sizes;
        return dimension;
    }
}

/// <summary>
/// Three points: the two things being measured, then how far out the
/// dimension line sits.
/// </summary>
public abstract class SpanDimensionTool : DimensionTool
{
    public override string Prompt => Points.Count switch
    {
        0 => $"{Title}: pick the first point to measure from",
        1 => $"{Title}: pick the second point",
        _ => $"{Title}: pick where the dimension line goes",
    };

    protected override SceneEntity? Build(Vec2 last) =>
        Points.Count < 3 ? null : Make(Points[0], Points[1], Points[2]);

    public override void Preview(Vec2 cursor, in EmitContext context, in DisplayStyle style)
    {
        switch (Points.Count)
        {
            case 0:
                return;

            // Only one origin so far: show the span being measured, since
            // there is no dimension line to place yet.
            case 1:
                Stroke(context, style, [Points[0], cursor], closed: false);
                return;

            default:
                // Through the entity's own Emit, so the preview cannot
                // disagree with what lands -- including the number, which is
                // the part a user will read off the screen before clicking.
                Make(Points[0], Points[1], cursor)?.Emit(context, style);
                return;
        }
    }

    /// <summary>The dimension these three points describe, or null if they describe none.</summary>
    protected SDimension? Make(Vec2 first, Vec2 second, Vec2 through) =>
        Vec2.Distance(first, second) < 1e-12 ? null : Sized(Build(first, second, through));

    protected abstract SDimension Build(Vec2 first, Vec2 second, Vec2 through);
}

/// <summary>
/// A dimension along one axis: the horizontal gap between two points, or the
/// vertical one.
/// </summary>
/// <remarks>
/// Which of the two is decided by the third point rather than by a mode,
/// which is how every CAD does it and needs no explaining: pull the line out
/// above or below the points and it measures across, pull it out to one side
/// and it measures up.
/// </remarks>
public sealed class LinearDimensionTool : SpanDimensionTool
{
    public override string Name => "dimension";

    protected override string Title => "Dimension";

    protected override SDimension Build(Vec2 first, Vec2 second, Vec2 through) =>
        new(DimensionKind.Linear, first, second, through, AxisFor(first, second, through));

    /// <summary>
    /// Horizontal or vertical, read off where the dimension line was pulled
    /// to. Public because it is the rule the tool is judged by, and it is
    /// arithmetic: two points and a third in, an axis out.
    /// </summary>
    public static double AxisFor(Vec2 first, Vec2 second, Vec2 through)
    {
        Vec2 offset = through - Vec2.Lerp(first, second, 0.5);

        // Ties go to horizontal, which is the one people reach for first and
        // the one a dimension pulled straight off a corner usually wants.
        return Math.Abs(offset.Y) >= Math.Abs(offset.X) ? 0 : Math.PI / 2;
    }
}

/// <summary>
/// A dimension parallel to the two points it measures, so it gives the true
/// distance between them rather than its shadow on an axis.
/// </summary>
public sealed class AlignedDimensionTool : SpanDimensionTool
{
    public override string Name => "aligned dimension";

    protected override string Title => "Aligned";

    protected override SDimension Build(Vec2 first, Vec2 second, Vec2 through) =>
        new(DimensionKind.Aligned, first, second, through);
}

/// <summary>
/// A dimension that measures an object rather than two picked points: click
/// the circle or the arc, then say where the number goes.
/// </summary>
/// <remarks>
/// The first click is aimed at something and the second is a point, which is
/// why <see cref="CanvasTool.WantsEntity"/> is a question the canvas asks
/// before every click rather than a kind of tool. Nothing else here needed
/// that: a draw tool only ever takes points and an entity tool only ever
/// takes objects.
/// <para>
/// It holds the centre and the radius rather than the object, so it still
/// has no reference to anything in the drawing -- the same rule that stops
/// every other tool reaching past the command stack.
/// </para>
/// </remarks>
public abstract class CircleDimensionTool : DimensionTool
{
    private Vec2 _center;
    private double _radius;

    /// <summary>Whether a circle has been clicked yet.</summary>
    public bool HasSubject => _radius > 0;

    public override bool WantsEntity => !HasSubject;

    public override string Prompt => HasSubject
        ? $"{Title}: pick where the number goes"
        : $"{Title}: click a circle or an arc";

    /// <summary>
    /// Takes the object that was clicked. False if it was not one with a
    /// radius, which leaves the tool waiting for one that is.
    /// </summary>
    public bool Take(SceneEntity? entity)
    {
        switch (entity)
        {
            case SCircle circle when circle.Radius > 0:
                _center = circle.Center;
                _radius = circle.Radius;
                return true;

            case SArc arc when arc.Radius > 0:
                _center = arc.Center;
                _radius = arc.Radius;
                return true;

            default:
                return false;
        }
    }

    public override void Cancel()
    {
        _radius = 0;
        base.Cancel();
    }

    protected override SceneEntity? Build(Vec2 last)
    {
        if (!HasSubject) return null;

        var dimension = Make(last);

        // Ready for the next one: the circle it was measuring is finished
        // with, and a tool that kept it would put the next dimension on the
        // wrong object.
        _radius = 0;
        return dimension;
    }

    public override void Preview(Vec2 cursor, in EmitContext context, in DisplayStyle style)
    {
        if (HasSubject) Make(cursor)?.Emit(context, style);
    }

    private SDimension Make(Vec2 at) =>
        Sized(new SDimension(Kind, _center, _center + new Vec2(_radius, 0), at));

    protected abstract DimensionKind Kind { get; }
}

/// <summary>Out from the centre to the rim, written with an R.</summary>
public sealed class RadiusDimensionTool : CircleDimensionTool
{
    public override string Name => "radius dimension";

    protected override string Title => "Radius";

    protected override DimensionKind Kind => DimensionKind.Radius;
}

/// <summary>Right across, and twice the number.</summary>
public sealed class DiameterDimensionTool : CircleDimensionTool
{
    public override string Name => "diameter dimension";

    protected override string Title => "Diameter";

    protected override DimensionKind Kind => DimensionKind.Diameter;
}
