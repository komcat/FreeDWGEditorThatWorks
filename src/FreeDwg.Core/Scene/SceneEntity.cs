using FreeDwg.Core.Geometry;
using FreeDwg.Core.Picking;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Snapping;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Scene;

/// <summary>
/// Base class for everything drawable. This is the editor's model, not the
/// file's: styles are already resolved, geometry is 2D, and nothing here knows
/// what a DWG is.
/// </summary>
public abstract class SceneEntity
{
    /// <summary>Index into <see cref="Drawing.Layers"/>.</summary>
    public int LayerIndex { get; set; }

    /// <summary>Colour and width, with ByLayer already resolved.</summary>
    public DisplayStyle Style { get; set; } = DisplayStyle.Default;

    /// <summary>
    /// Which parts of <see cref="Style"/> are placeholders to be supplied by
    /// the block reference drawing this entity (DWG's ByBlock). Always None for
    /// entities in model space, since there is no reference to inherit from.
    /// </summary>
    public StyleInheritance Inherits { get; set; }

    /// <summary>
    /// Handle of the object this came from. Saving applies deltas back onto the
    /// original document rather than regenerating it, so anything we do not
    /// model (xdata, proxies, dictionaries) survives a round trip untouched.
    /// </summary>
    public ulong SourceHandle { get; set; }

    private Bounds2? _bounds;

    /// <summary>World bounds, computed once. Invalidate after mutating geometry.</summary>
    public Bounds2 Bounds => _bounds ??= ComputeBounds();

    public void InvalidateBounds() => _bounds = null;

    protected abstract Bounds2 ComputeBounds();

    /// <summary>Emits this entity's geometry. Double dispatch, so the render loop needs no type switch.</summary>
    public abstract void Emit(in EmitContext context, in DisplayStyle style);

    /// <summary>
    /// Distance in world units from <paramref name="point"/> to this entity's
    /// geometry, or <see cref="double.PositiveInfinity"/> if it cannot be
    /// picked at all. Double dispatch again, so selection needs no type switch
    /// either.
    /// </summary>
    /// <remarks>
    /// Measured against the geometry as drawn: an outline for an open shape,
    /// the filled area for a hatch. Curves with no closed form are flattened
    /// at <see cref="PickContext.Tolerance"/>, which keeps the error well
    /// inside the radius a click is allowed to miss by.
    /// </remarks>
    public abstract double DistanceTo(Vec2 point, in PickContext context);

    /// <summary>Convenience overload for callers with no layer table to hand.</summary>
    public double DistanceTo(Vec2 point, double tolerance) =>
        DistanceTo(point, new PickContext(tolerance));

    /// <summary>
    /// Whether any part of this entity lies inside <paramref name="rect"/>:
    /// the test behind crossing selection. Window selection, which takes only
    /// what is wholly enclosed, uses <see cref="Bounds"/> instead.
    /// </summary>
    public abstract bool IntersectsRect(Bounds2 rect, in PickContext context);

    public bool IntersectsRect(Bounds2 rect, double tolerance) =>
        IntersectsRect(rect, new PickContext(tolerance));

    /// <summary>
    /// Offers the points on this entity that the cursor may jump to. The
    /// third thing entities do for themselves, after emitting and hit
    /// testing, and for the same reason: no type switch anywhere else.
    /// </summary>
    /// <remarks>
    /// Only the kinds in <paramref name="modes"/> need be offered; anything
    /// extra is filtered out again by the caller, so it is only wasted work.
    /// An entity with no meaningful points -- a hatch, whose boundary is
    /// derived rather than drawn -- offers none.
    /// </remarks>
    public abstract void CollectSnapPoints(SnapModes modes, ICollection<SnapCandidate> into);

    /// <summary>
    /// Offers this entity's geometry as segments and arcs, which is what
    /// intersection works on.
    /// </summary>
    /// <remarks>
    /// Exact for lines, arcs, circles and polylines. Ellipses and splines
    /// flatten at <paramref name="tolerance"/>, so an intersection found
    /// against one is only as good as the flattening -- fine for trimming to
    /// a spline, not something to build a tolerance stack on. An entity with
    /// no curve to speak of offers nothing, and simply cannot be trimmed to.
    /// </remarks>
    public abstract void CollectCurves(ICollection<CurvePiece> into, double tolerance);

    /// <summary>
    /// Moves this entity by an affine transform, in place.
    /// </summary>
    /// <remarks>
    /// Not virtual: the override is <see cref="TransformGeometry"/>, and this
    /// wrapper invalidates the bounds cache afterwards. An entity that moved
    /// without dropping its cached bounds would be culled where it used to be
    /// and picked where it no longer is, which is exactly the class of bug
    /// this milestone was warned about.
    /// <para>
    /// A transform that is not a similarity -- a non-uniform scale or a skew
    /// -- turns a circle into an ellipse, which the analytic entities cannot
    /// represent. They approximate with the uniform scale instead. Every tool
    /// here produces a similarity, so nothing hits that today.
    /// </para>
    /// </remarks>
    public void Transform(in Mat3 transform)
    {
        TransformGeometry(transform);
        InvalidateBounds();
    }

    protected abstract void TransformGeometry(in Mat3 transform);

    /// <summary>
    /// An independent copy, for the tools that duplicate rather than move.
    /// </summary>
    /// <remarks>
    /// The clone carries no <see cref="SourceHandle"/>: it is a new object
    /// that was never in the file, and giving it the original's handle would
    /// have a save overwrite the original with the copy.
    /// <para>
    /// Shallow by default, because most entities are value fields all the way
    /// down. The ones holding arrays or lists deep-copy them in
    /// <see cref="CloneGeometry"/> -- but a block reference is deliberately
    /// *not* copied, since instancing a definition is the whole point of it.
    /// </para>
    /// </remarks>
    public SceneEntity Clone()
    {
        var copy = (SceneEntity)MemberwiseClone();
        copy.SourceHandle = 0;
        copy.InvalidateBounds();
        copy.CloneGeometry();
        return copy;
    }

    /// <summary>Replaces any shared mutable geometry on a fresh clone.</summary>
    protected virtual void CloneGeometry() { }

    /// <summary>Applies this entity's ByBlock placeholders from the enclosing reference's style.</summary>
    public DisplayStyle StyleWithin(in DisplayStyle reference)
    {
        if (Inherits == StyleInheritance.None) return Style;

        var style = Style;
        if (Inherits.HasFlag(StyleInheritance.Color)) style = style with { Color = reference.Color };
        if (Inherits.HasFlag(StyleInheritance.Lineweight)) style = style with { Lineweight = reference.Lineweight };
        return style;
    }
}
