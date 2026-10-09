using FreeDwg.Core.Geometry;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Tools;

/// <summary>Two points.</summary>
public sealed class LineTool : DrawTool
{
    public override string Name => "line";

    public override string Prompt => Points.Count == 0
        ? "Line: pick the start point"
        : "Line: pick the end point";

    protected override SceneEntity? Build(Vec2 last) =>
        Points.Count < 2 ? null : new SLine(Points[0], Points[1]);

    public override void Preview(Vec2 cursor, in EmitContext context, in DisplayStyle style)
    {
        if (Points.Count > 0) Stroke(context, style, [Points[0], cursor], closed: false);
    }
}

/// <summary>
/// Any number of points, ended with Enter or a right click. The first tool
/// where a click does not necessarily finish anything.
/// </summary>
public sealed class PolylineTool : DrawTool
{
    public override string Name => "polyline";

    public override string Prompt => Points.Count == 0
        ? "Polyline: pick the start point"
        : $"Polyline: pick the next point, or press Enter to finish  ({Points.Count} so far)";

    // A click never completes a polyline; only Enter does.
    protected override SceneEntity? Build(Vec2 last) => null;

    protected override SceneEntity? BuildPartial()
    {
        if (Points.Count < 2) return null;

        var vertices = new PolyVertex[Points.Count];
        for (int i = 0; i < vertices.Length; i++) vertices[i] = new PolyVertex(Points[i]);

        return new SPolyline(vertices, closed: false);
    }

    public override void Preview(Vec2 cursor, in EmitContext context, in DisplayStyle style) =>
        Stroke(context, style, WithCursor(cursor), closed: false);
}

/// <summary>
/// Two opposite corners. A rectangle is a closed polyline in DWG -- there is
/// no RECTANGLE entity -- so that is what this makes.
/// </summary>
public sealed class RectangleTool : DrawTool
{
    public override string Name => "rectangle";

    public override string Prompt => Points.Count == 0
        ? "Rectangle: pick the first corner"
        : "Rectangle: pick the opposite corner, or type a width -- Tab for the height";

    protected override SceneEntity? Build(Vec2 last) =>
        Points.Count < 2 ? null : Make(Points[0], Points[1]);

    /// <summary>
    /// The opposite corner, with whichever sides have been typed held to
    /// their size and the rest left where the cursor puts them.
    /// </summary>
    /// <remarks>
    /// A typed size says how big, never which way: the cursor still decides
    /// which side of the first corner the rectangle opens towards, so a
    /// width of 100 typed with the cursor to the left draws to the left. A
    /// cursor exactly level with the corner has no side, and takes the
    /// positive one rather than giving no rectangle at all.
    /// </remarks>
    public static Vec2 Constrain(Vec2 corner, Vec2 cursor, double? width, double? height)
    {
        double x = width is { } w ? corner.X + (cursor.X < corner.X ? -w : w) : cursor.X;
        double y = height is { } h ? corner.Y + (cursor.Y < corner.Y ? -h : h) : cursor.Y;
        return new Vec2(x, y);
    }

    private static SPolyline? Make(Vec2 a, Vec2 b)
    {
        // A rectangle with no width or no height is a line the user did not
        // mean to draw; nothing is better than a degenerate polyline.
        if (Math.Abs(a.X - b.X) < 1e-12 || Math.Abs(a.Y - b.Y) < 1e-12) return null;

        PolyVertex[] corners =
        [
            new(new Vec2(a.X, a.Y)), new(new Vec2(b.X, a.Y)),
            new(new Vec2(b.X, b.Y)), new(new Vec2(a.X, b.Y)),
        ];

        return new SPolyline(corners, closed: true);
    }

    public override void Preview(Vec2 cursor, in EmitContext context, in DisplayStyle style)
    {
        if (Points.Count == 0) return;

        Vec2 a = Points[0];
        Stroke(context, style,
            [a, new Vec2(cursor.X, a.Y), cursor, new Vec2(a.X, cursor.Y)], closed: true);
    }
}

/// <summary>Centre, then a point on the rim.</summary>
public sealed class CircleTool : DrawTool
{
    public override string Name => "circle";

    public override string Prompt => Points.Count == 0
        ? "Circle: pick the centre"
        : "Circle: pick a point on the circle, or type a radius";

    protected override SceneEntity? Build(Vec2 last)
    {
        if (Points.Count < 2) return null;

        double radius = Vec2.Distance(Points[0], Points[1]);
        return radius > 1e-12 ? new SCircle(Points[0], radius) : null;
    }

    public override void Preview(Vec2 cursor, in EmitContext context, in DisplayStyle style)
    {
        if (Points.Count == 0) return;

        double radius = Vec2.Distance(Points[0], cursor);
        if (radius > 1e-12) context.Sink.Circle(Points[0], radius, style);
    }
}

/// <summary>
/// Start, a point the arc passes through, then the end. Three points because
/// it is the only way to give an arc that needs no thinking about direction:
/// the middle point says which way round it goes.
/// </summary>
public sealed class ArcTool : DrawTool
{
    public override string Name => "arc";

    public override string Prompt => Points.Count switch
    {
        0 => "Arc: pick the start point",
        1 => "Arc: pick a point on the arc",
        _ => "Arc: pick the end point",
    };

    protected override SceneEntity? Build(Vec2 last)
    {
        if (Points.Count < 3) return null;

        return ArcMath.TryArcThrough(Points[0], Points[1], Points[2],
            out var center, out double radius, out double start, out double sweep)
            ? new SArc(center, radius, start, sweep)
            : null;
    }

    public override void Preview(Vec2 cursor, in EmitContext context, in DisplayStyle style)
    {
        switch (Points.Count)
        {
            case 0:
                return;

            // Only two points known: show the chord, since any arc through
            // them is still possible.
            case 1:
                Stroke(context, style, [Points[0], cursor], closed: false);
                return;

            default:
                if (!ArcMath.TryArcThrough(Points[0], Points[1], cursor,
                        out var center, out double radius, out double start, out double sweep))
                {
                    Stroke(context, style, [Points[0], cursor], closed: false);
                    return;
                }

                // Flattened rather than emitted as an arc, because the preview
                // is transient and this needs no sink support beyond LineTo.
                Stroke(context, style,
                    ArcMath.Tessellate(center, radius, start, sweep, radius * context.PixelsPerUnit),
                    closed: false);
                return;
        }
    }
}

/// <summary>
/// Centre, the end of the major axis, then a point whose distance from that
/// axis sets the minor one -- DWG's own parameterisation, so nothing is lost
/// converting to it.
/// </summary>
public sealed class EllipseTool : DrawTool
{
    public override string Name => "ellipse";

    public override string Prompt => Points.Count switch
    {
        0 => "Ellipse: pick the centre",
        1 => "Ellipse: pick the end of the major axis",
        _ => "Ellipse: pick a point to set the minor axis",
    };

    protected override SceneEntity? Build(Vec2 last) =>
        Points.Count < 3 ? null : Make(Points[0], Points[1], Points[2]);

    private static SEllipse? Make(Vec2 center, Vec2 majorEnd, Vec2 minorPoint)
    {
        Vec2 major = majorEnd - center;
        double majorLength = major.Length;
        if (majorLength < 1e-12) return null;

        // The minor semi-axis is how far off the major axis the third point
        // is, so the click reads the same whatever angle it comes in at.
        double minorLength = Math.Abs(Vec2.Dot(minorPoint - center, major.Perp.Normalized()));
        double ratio = minorLength / majorLength;
        if (ratio < 1e-9) return null;

        return new SEllipse(center, major, Math.Min(ratio, 1.0), 0, ArcMath.TwoPi);
    }

    public override void Preview(Vec2 cursor, in EmitContext context, in DisplayStyle style)
    {
        switch (Points.Count)
        {
            case 0:
                return;

            case 1:
                Stroke(context, style, [Points[0], cursor], closed: false);
                return;

            default:
                var ellipse = Make(Points[0], Points[1], cursor);
                if (ellipse is null)
                {
                    Stroke(context, style, [Points[0], Points[1]], closed: false);
                    return;
                }

                ellipse.Emit(context, style);
                return;
        }
    }
}
