using FreeDwg.Core.Geometry;

namespace FreeDwg.Core.Tools;

/// <summary>
/// A tool that moves what is already selected, by picking the points that
/// define a transform.
/// </summary>
/// <remarks>
/// Gives back a <see cref="Mat3"/> rather than touching anything, so one
/// command -- transform in place, or copy and transform the copy -- covers
/// every tool here. Move, rotate, scale and mirror differ only in how they
/// read their points.
/// </remarks>
public abstract class ModifyTool : CanvasTool
{
    public override bool NeedsSelection => true;

    /// <summary>
    /// Whether the result is a copy of the selection rather than a move of
    /// it. Copy and mirror leave the original where it was.
    /// </summary>
    public virtual bool Duplicates => false;

    /// <summary>
    /// Takes a picked point. Returns the transform when this point completes
    /// it, and null while the tool still wants more.
    /// </summary>
    public Mat3? Click(Vec2 point)
    {
        AddPoint(point);

        var transform = Build();
        if (transform is not null) ClearPoints();

        return transform;
    }

    /// <summary>
    /// The transform as it stands with the cursor here, for the preview.
    /// Null when there is not enough to show yet.
    /// </summary>
    public abstract Mat3? Pending(Vec2 cursor);

    /// <summary>The transform, if the points now in hand are enough for one.</summary>
    protected abstract Mat3? Build();
}

/// <summary>Base point, then where it should end up.</summary>
public sealed class MoveTool : ModifyTool
{
    public override string Name => "move";

    public override string Prompt => Points.Count == 0
        ? "Move: pick the base point"
        : "Move: pick where it goes";

    protected override Mat3? Build() =>
        Points.Count < 2 ? null : Mat3.Translation(Points[1] - Points[0]);

    public override Mat3? Pending(Vec2 cursor) =>
        Points.Count == 0 ? null : Mat3.Translation(cursor - Points[0]);
}

/// <summary>The same two points as a move, but the original stays put.</summary>
public sealed class CopyTool : ModifyTool
{
    public override string Name => "copy";

    public override bool Duplicates => true;

    public override string Prompt => Points.Count == 0
        ? "Copy: pick the base point"
        : "Copy: pick where the copy goes";

    protected override Mat3? Build() =>
        Points.Count < 2 ? null : Mat3.Translation(Points[1] - Points[0]);

    public override Mat3? Pending(Vec2 cursor) =>
        Points.Count == 0 ? null : Mat3.Translation(cursor - Points[0]);
}

/// <summary>
/// Centre of rotation, then a point whose direction from it is the angle.
/// </summary>
/// <remarks>
/// Two points rather than three because there is nothing to be relative to
/// yet: the angle is read straight off the second point, so dragging right
/// is zero and dragging up is a quarter turn.
/// </remarks>
public sealed class RotateTool : ModifyTool
{
    public override string Name => "rotate";

    public override string Prompt => Points.Count == 0
        ? "Rotate: pick the centre of rotation"
        : "Rotate: pick the angle";

    protected override Mat3? Build() => Points.Count < 2 ? null : Turn(Points[0], Points[1]);

    public override Mat3? Pending(Vec2 cursor) =>
        Points.Count == 0 ? null : Turn(Points[0], cursor);

    private static Mat3? Turn(Vec2 center, Vec2 towards)
    {
        Vec2 arm = towards - center;
        return arm.LengthSquared < 1e-18 ? null : Mat3.RotationAbout(arm.Angle(), center);
    }
}

/// <summary>
/// Base point, a reference point, then where that reference point should
/// end up: the scale is the ratio of the two distances.
/// </summary>
/// <remarks>
/// Three points rather than a typed factor, because there is no coordinate
/// entry yet and a dragged factor with no reference is arbitrary. Picking
/// the reference makes "twice as long as that edge" a thing you can do by
/// pointing at the edge.
/// </remarks>
public sealed class ScaleTool : ModifyTool
{
    public override string Name => "scale";

    public override string Prompt => Points.Count switch
    {
        0 => "Scale: pick the base point",
        1 => "Scale: pick a reference point to measure from",
        _ => "Scale: pick where that reference point should reach",
    };

    protected override Mat3? Build() =>
        Points.Count < 3 ? null : Resize(Points[0], Points[1], Points[2]);

    public override Mat3? Pending(Vec2 cursor) => Points.Count switch
    {
        < 2 => null,
        _ => Resize(Points[0], Points[1], cursor),
    };

    private static Mat3? Resize(Vec2 basePoint, Vec2 reference, Vec2 target)
    {
        double from = Vec2.Distance(basePoint, reference);
        double to = Vec2.Distance(basePoint, target);

        if (from < 1e-12 || to < 1e-12) return null;

        return Mat3.ScalingAbout(to / from, basePoint);
    }
}

/// <summary>
/// Two points defining the mirror line. The original is kept, which is what
/// mirroring is nearly always for.
/// </summary>
public sealed class MirrorTool : ModifyTool
{
    public override string Name => "mirror";

    public override bool Duplicates => true;

    public override string Prompt => Points.Count == 0
        ? "Mirror: pick the first point of the mirror line"
        : "Mirror: pick the second point of the mirror line";

    protected override Mat3? Build() => Points.Count < 2 ? null : Flip(Points[0], Points[1]);

    public override Mat3? Pending(Vec2 cursor) =>
        Points.Count == 0 ? null : Flip(Points[0], cursor);

    private static Mat3? Flip(Vec2 from, Vec2 to)
    {
        Vec2 axis = to - from;
        return axis.LengthSquared < 1e-18 ? null : Mat3.Reflection(from, axis);
    }
}
