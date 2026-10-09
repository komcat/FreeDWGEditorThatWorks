using ACadSharp.Entities;
using CSMath;
using FreeDwg.Core.Geometry;
using AcadArc = ACadSharp.Entities.Arc;
using AcadCircle = ACadSharp.Entities.Circle;
using AcadEllipse = ACadSharp.Entities.Ellipse;
using AcadLine = ACadSharp.Entities.Line;

namespace FreeDwg.Interop.Acad;

/// <summary>
/// Conversions between the scene's 2D types and ACadSharp's, and moving a
/// file entity by a <see cref="Mat3"/>.
/// </summary>
/// <remarks>
/// ACadSharp has <c>ApplyTransform</c> of its own, but it mirrors an arc or a
/// polyline by turning its normal upside down rather than by reversing it.
/// That is a correct DWG, and it is also one this editor's reader -- which
/// works in the XY plane and ignores normals -- would draw inside out. So the
/// kinds that matter are moved here, keeping +Z and reversing sweeps and
/// bulges instead, the same way the scene's own entities do it.
/// </remarks>
internal static class CadGeometry
{
    public static Vec2 ToVec2(XYZ p) => new(p.X, p.Y);
    public static Vec2 ToVec2(XY p) => new(p.X, p.Y);
    public static XYZ ToXYZ(Vec2 p, double z = 0) => new(p.X, p.Y, z);
    public static XY ToXY(Vec2 p) => new(p.X, p.Y);

    /// <summary>Relative closeness, so a kilometre drawing and a millimetre one are judged alike.</summary>
    public static bool Near(double a, double b) =>
        Math.Abs(a - b) <= 1e-9 * Math.Max(1.0, Math.Max(Math.Abs(a), Math.Abs(b)));

    public static bool Near(Vec2 a, Vec2 b) => Near(a.X, b.X) && Near(a.Y, b.Y);

    public static bool Near(XYZ a, Vec2 b) => Near(a.X, b.X) && Near(a.Y, b.Y);

    /// <summary>Two angles the same direction, whichever turn they were written on.</summary>
    public static bool SameAngle(double a, double b)
    {
        double d = ArcMath.Normalize(a - b);
        return d < 1e-9 || d > ArcMath.TwoPi - 1e-9;
    }

    public static bool Near(in Mat3 a, in Mat3 b) =>
        Near(a.M11, b.M11) && Near(a.M12, b.M12) && Near(a.M21, b.M21) && Near(a.M22, b.M22) &&
        Near(a.OffsetX, b.OffsetX) && Near(a.OffsetY, b.OffsetY);

    /// <summary>What turns <paramref name="from"/> into <paramref name="to"/>: from, then this, is to.</summary>
    public static Mat3 Between(in Mat3 from, in Mat3 to) =>
        from.TryInvert(out var inverse) ? inverse * to : Mat3.Identity;

    /// <summary>
    /// The insert point, scales and rotation of a placement built by
    /// <c>SInsert.BuildTransform</c>.
    /// </summary>
    /// <remarks>
    /// The X scale comes back positive and carries the rotation; a mirror
    /// shows up as a negative Y scale. A file with X scale -1 therefore comes
    /// back as Y scale -1 and half a turn, which draws identically.
    /// </remarks>
    public static (Vec2 Insert, double ScaleX, double ScaleY, double Rotation) Decompose(in Mat3 placement, Vec2 basePoint)
    {
        double rotation = Math.Atan2(placement.M12, placement.M11);
        double scaleX = Math.Sqrt(placement.M11 * placement.M11 + placement.M12 * placement.M12);
        double scaleY = -Math.Sin(rotation) * placement.M21 + Math.Cos(rotation) * placement.M22;
        return (placement.Transform(basePoint), scaleX, scaleY, rotation);
    }

    /// <summary>The direction a text's baseline points after a transform.</summary>
    private static double TurnAngle(in Mat3 m, double angle)
    {
        var v = m.TransformVector(new Vec2(Math.Cos(angle), Math.Sin(angle)));
        return v.LengthSquared > 0 ? v.Angle() : angle;
    }

    /// <summary>
    /// Moves a file entity, in place. False for a kind not handled here, which
    /// the caller reports rather than silently leaves behind.
    /// </summary>
    public static bool Apply(Entity entity, in Mat3 m)
    {
        double scale = m.UniformScale;

        switch (entity)
        {
            case AcadLine line:
                line.StartPoint = Move(m, line.StartPoint);
                line.EndPoint = Move(m, line.EndPoint);
                return true;

            // Arc derives from Circle, so it is asked about first.
            case AcadArc arc:
            {
                var start = m.Transform(ArcMath.PointAt(ToVec2(arc.Center), arc.Radius, arc.StartAngle));
                var end = m.Transform(ArcMath.PointAt(ToVec2(arc.Center), arc.Radius, arc.EndAngle));
                var center = m.Transform(ToVec2(arc.Center));

                arc.Center = ToXYZ(center, arc.Center.Z);
                arc.Radius *= scale;

                // A mirror runs the arc the other way round, and DWG only
                // draws counter-clockwise, so the ends trade places.
                if (m.IsMirror) (start, end) = (end, start);
                arc.StartAngle = ArcMath.Normalize((start - center).Angle());
                arc.EndAngle = ArcMath.Normalize((end - center).Angle());
                return true;
            }

            case AcadCircle circle:
                circle.Center = Move(m, circle.Center);
                circle.Radius *= scale;
                return true;

            case AcadEllipse ellipse:
            {
                ellipse.Center = Move(m, ellipse.Center);
                var axis = m.TransformVector(ToVec2(ellipse.MajorAxisEndPoint));
                ellipse.MajorAxisEndPoint = ToXYZ(axis, ellipse.MajorAxisEndPoint.Z);

                // Mirrored, the minor axis points the other way, so a
                // parameter t lands where -t used to: the span reverses.
                if (m.IsMirror)
                    (ellipse.StartParameter, ellipse.EndParameter) = (-ellipse.EndParameter, -ellipse.StartParameter);
                return true;
            }

            case LwPolyline polyline:
                foreach (var v in polyline.Vertices)
                {
                    v.Location = ToXY(m.Transform(ToVec2(v.Location)));
                    if (m.IsMirror) v.Bulge = -v.Bulge;
                    v.StartWidth *= scale;
                    v.EndWidth *= scale;
                }
                return true;

            case Polyline2D polyline:
                foreach (var v in polyline.Vertices)
                {
                    v.Location = Move(m, v.Location);
                    if (m.IsMirror) v.Bulge = -v.Bulge;
                }
                return true;

            case Polyline3D polyline:
                foreach (var v in polyline.Vertices) v.Location = Move(m, v.Location);
                return true;

            case Spline spline:
                for (int i = 0; i < spline.ControlPoints.Count; i++)
                    spline.ControlPoints[i] = Move(m, spline.ControlPoints[i]);
                for (int i = 0; i < spline.FitPoints.Count; i++)
                    spline.FitPoints[i] = Move(m, spline.FitPoints[i]);
                spline.StartTangent = Turn(m, spline.StartTangent);
                spline.EndTangent = Turn(m, spline.EndTangent);
                return true;

            case Solid solid:
                solid.FirstCorner = Move(m, solid.FirstCorner);
                solid.SecondCorner = Move(m, solid.SecondCorner);
                solid.ThirdCorner = Move(m, solid.ThirdCorner);
                solid.FourthCorner = Move(m, solid.FourthCorner);
                return true;

            case Point point:
                point.Location = Move(m, point.Location);
                return true;

            // Attributes are text too, so this covers them as well. Text is
            // never drawn mirrored -- it would be unreadable -- so a mirror
            // moves it and turns its baseline, the way the scene does.
            case TextEntity text:
                text.InsertPoint = Move(m, text.InsertPoint);
                text.AlignmentPoint = Move(m, text.AlignmentPoint);
                text.Rotation = ArcMath.Normalize(TurnAngle(m, text.Rotation));
                text.Height *= scale;
                return true;

            case MText mtext:
                mtext.InsertPoint = Move(m, mtext.InsertPoint);
                // MTEXT's rotation is read off this direction; it has no other.
                double turned = TurnAngle(m, mtext.Rotation);
                mtext.AlignmentPoint = new XYZ(Math.Cos(turned), Math.Sin(turned), 0);
                mtext.Height *= scale;
                mtext.RectangleWidth *= scale;
                return true;

            case Insert insert:
                return ApplyToInsert(insert, m);

            case Hatch hatch:
                ApplyToHatch(hatch, m);
                return true;

            case Dimension dimension:
                ApplyToDimension(dimension, m);
                return true;

            default:
                // Everything else goes through ACadSharp, which is right for
                // anything that is not turned over.
                if (m.IsMirror) return false;
                entity.ApplyScaling(new XYZ(scale, scale, 1), XYZ.Zero);
                entity.ApplyRotation(XYZ.AxisZ, Math.Atan2(m.M12, m.M11));
                entity.ApplyTranslation(new XYZ(m.OffsetX, m.OffsetY, 0));
                return true;
        }
    }

    private static XYZ Move(in Mat3 m, XYZ p) => ToXYZ(m.Transform(ToVec2(p)), p.Z);

    private static XYZ Turn(in Mat3 m, XYZ v) => ToXYZ(m.TransformVector(ToVec2(v)), v.Z);

    /// <summary>The block-local placement an insert describes.</summary>
    public static Mat3 PlacementOf(Insert insert) =>
        FreeDwg.Core.Scene.Entities.SInsert.BuildTransform(
            ToVec2(insert.Block?.BlockEntity?.BasePoint ?? XYZ.Zero),
            ToVec2(insert.InsertPoint), insert.XScale, insert.YScale, insert.Rotation);

    /// <summary>Sets an insert's own fields to describe <paramref name="placement"/>.</summary>
    public static void Place(Insert insert, in Mat3 placement)
    {
        var basePoint = ToVec2(insert.Block?.BlockEntity?.BasePoint ?? XYZ.Zero);
        var (at, sx, sy, rotation) = Decompose(placement, basePoint);

        insert.InsertPoint = ToXYZ(at, insert.InsertPoint.Z);
        insert.XScale = sx;
        insert.YScale = sy;
        insert.Rotation = ArcMath.Normalize(rotation);
    }

    private static bool ApplyToInsert(Insert insert, in Mat3 m)
    {
        Place(insert, PlacementOf(insert) * m);

        // Attributes are entities of their own, in world space, so they do
        // not follow the insert unless they are taken along.
        foreach (var attribute in insert.Attributes) Apply(attribute, m);
        return true;
    }

    /// <summary>
    /// Moves a hatch's boundary and its pattern definition together, so the
    /// pattern stays registered to the boundary rather than sliding under it.
    /// </summary>
    private static void ApplyToHatch(Hatch hatch, in Mat3 m)
    {
        double scale = m.UniformScale;

        foreach (var path in hatch.Paths)
        {
            foreach (var edge in path.Edges)
            {
                switch (edge)
                {
                    case Hatch.BoundaryPath.Line line:
                        line.Start = ToXY(m.Transform(ToVec2(line.Start)));
                        line.End = ToXY(m.Transform(ToVec2(line.End)));
                        break;

                    case Hatch.BoundaryPath.Arc arc:
                    {
                        // Stored angles are negated for a clockwise edge;
                        // work in the real ones and store back the same way.
                        double sweep = arc.EndAngle - arc.StartAngle;
                        double start = arc.CounterClockWise ? arc.StartAngle : -arc.StartAngle;
                        var center = ToVec2(arc.Center);
                        var startPoint = m.Transform(ArcMath.PointAt(center, arc.Radius, start));

                        center = m.Transform(center);
                        arc.Center = ToXY(center);
                        arc.Radius *= scale;

                        if (m.IsMirror) arc.CounterClockWise = !arc.CounterClockWise;
                        double real = (startPoint - center).Angle();
                        arc.StartAngle = arc.CounterClockWise ? real : -real;
                        arc.EndAngle = arc.StartAngle + sweep;
                        break;
                    }

                    case Hatch.BoundaryPath.Ellipse ellipse:
                        // Parameters are measured from the major axis, which
                        // turns with everything else; a mirror only reverses
                        // the direction the span is walked.
                        ellipse.Center = ToXY(m.Transform(ToVec2(ellipse.Center)));
                        ellipse.MajorAxisEndPoint = ToXY(m.TransformVector(ToVec2(ellipse.MajorAxisEndPoint)));
                        if (m.IsMirror) ellipse.CounterClockWise = !ellipse.CounterClockWise;
                        break;

                    case Hatch.BoundaryPath.Polyline polyline:
                        for (int i = 0; i < polyline.Vertices.Count; i++)
                        {
                            // Z is the bulge here, not a height.
                            var v = polyline.Vertices[i];
                            var p = m.Transform(new Vec2(v.X, v.Y));
                            polyline.Vertices[i] = new XYZ(p.X, p.Y, m.IsMirror ? -v.Z : v.Z);
                        }
                        break;

                    case Hatch.BoundaryPath.Spline spline:
                        for (int i = 0; i < spline.ControlPoints.Count; i++)
                        {
                            // And here Z is the weight.
                            var v = spline.ControlPoints[i];
                            var p = m.Transform(new Vec2(v.X, v.Y));
                            spline.ControlPoints[i] = new XYZ(p.X, p.Y, v.Z);
                        }
                        for (int i = 0; i < spline.FitPoints.Count; i++)
                            spline.FitPoints[i] = ToXY(m.Transform(ToVec2(spline.FitPoints[i])));
                        spline.StartTangent = ToXY(m.TransformVector(ToVec2(spline.StartTangent)));
                        spline.EndTangent = ToXY(m.TransformVector(ToVec2(spline.EndTangent)));
                        break;
                }
            }
        }

        for (int i = 0; i < hatch.SeedPoints.Count; i++)
            hatch.SeedPoints[i] = ToXY(m.Transform(ToVec2(hatch.SeedPoints[i])));

        if (hatch.IsSolid || hatch.Pattern is null) return;

        // The pattern lines are in world space and are what gets drawn, so
        // they are the ones that have to move. The angle and scale on the
        // hatch are only a record of how they were made; setting them through
        // their properties would rotate the lines a second time, so the
        // pattern is held aside while they are updated.
        var pattern = hatch.Pattern;
        foreach (var line in pattern.Lines)
        {
            line.Angle = ArcMath.Normalize(TurnAngle(m, line.Angle));
            line.BasePoint = ToXY(m.Transform(ToVec2(line.BasePoint)));
            line.Offset = ToXY(m.TransformVector(ToVec2(line.Offset)));
            for (int i = 0; i < line.DashLengths.Count; i++) line.DashLengths[i] *= scale;
        }

        hatch.Pattern = null!;
        hatch.PatternAngle = ArcMath.Normalize(TurnAngle(m, hatch.PatternAngle));
        hatch.PatternScale *= scale;
        hatch.Pattern = pattern;
    }

    /// <summary>
    /// Moves a dimension's definition points and the picture in its block.
    /// </summary>
    /// <remarks>
    /// The picture is what AutoCAD shows until something regenerates the
    /// dimension, and it is what this editor's reader draws, so it has to go
    /// with the points. The block is drawn at the dimension's insertion
    /// point, which is left alone; the block's contents absorb the move.
    /// </remarks>
    private static void ApplyToDimension(Dimension dimension, in Mat3 m)
    {
        dimension.DefinitionPoint = Move(m, dimension.DefinitionPoint);
        dimension.TextMiddlePoint = Move(m, dimension.TextMiddlePoint);

        switch (dimension)
        {
            case DimensionLinear linear:
                linear.FirstPoint = Move(m, linear.FirstPoint);
                linear.SecondPoint = Move(m, linear.SecondPoint);
                linear.Rotation = ArcMath.Normalize(TurnAngle(m, linear.Rotation));
                break;
            case DimensionAligned aligned:
                aligned.FirstPoint = Move(m, aligned.FirstPoint);
                aligned.SecondPoint = Move(m, aligned.SecondPoint);
                break;
            case DimensionRadius radius:
                radius.AngleVertex = Move(m, radius.AngleVertex);
                break;
            case DimensionDiameter diameter:
                diameter.AngleVertex = Move(m, diameter.AngleVertex);
                break;
            case DimensionAngular2Line angular:
                angular.AngleVertex = Move(m, angular.AngleVertex);
                angular.FirstPoint = Move(m, angular.FirstPoint);
                angular.SecondPoint = Move(m, angular.SecondPoint);
                angular.DimensionArc = Move(m, angular.DimensionArc);
                break;
            case DimensionAngular3Pt angular:
                angular.AngleVertex = Move(m, angular.AngleVertex);
                angular.FirstPoint = Move(m, angular.FirstPoint);
                angular.SecondPoint = Move(m, angular.SecondPoint);
                break;
            case DimensionOrdinate ordinate:
                ordinate.FeatureLocation = Move(m, ordinate.FeatureLocation);
                ordinate.LeaderEndpoint = Move(m, ordinate.LeaderEndpoint);
                break;
        }

        if (dimension.Block is null) return;

        // Contents in block space: out to the world, moved, and back.
        var basePoint = ToVec2(dimension.Block.BlockEntity?.BasePoint ?? XYZ.Zero);
        var toWorld = Mat3.Translation(ToVec2(dimension.InsertionPoint) - basePoint);
        toWorld.TryInvert(out var toBlock);
        var local = toWorld * m * toBlock;

        foreach (var entity in dimension.Block.Entities) Apply(entity, local);
    }
}
