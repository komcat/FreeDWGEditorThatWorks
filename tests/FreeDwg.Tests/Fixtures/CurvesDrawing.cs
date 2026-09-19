using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;

namespace FreeDwg.Tests.Fixtures;

/// <summary>
/// The hard tail: ellipses, a NURBS spline with analytically known points, a
/// solid hatch with an island, a pattern hatch, and a dimension that should
/// render from its anonymous block.
/// </summary>
public static class CurvesDrawing
{
    public static void Write(string path)
    {
        var doc = new CadDocument();

        var curves = new Layer("CURVES") { Color = new Color(3) };
        var fills = new Layer("FILLS") { Color = new Color(5) };
        doc.Layers.Add(curves);
        doc.Layers.Add(fills);

        // ---- ellipses ---------------------------------------------------
        // Axis-aligned: rx 20, ry 8.
        doc.Entities.Add(new Ellipse
        {
            Center = new XYZ(30, 30, 0),
            MajorAxisEndPoint = new XYZ(20, 0, 0),
            RadiusRatio = 0.4,
            StartParameter = 0,
            EndParameter = Math.PI * 2,
            Layer = curves,
        });

        // Major axis along +Y, so the ellipse stands on end: rx 7.5, ry 15.
        doc.Entities.Add(new Ellipse
        {
            Center = new XYZ(90, 30, 0),
            MajorAxisEndPoint = new XYZ(0, 15, 0),
            RadiusRatio = 0.5,
            StartParameter = 0,
            EndParameter = Math.PI * 2,
            Layer = curves,
        });

        // ---- spline ------------------------------------------------------
        // Degree 3 with four control points and a clamped knot vector is a
        // plain cubic Bezier, so the midpoint is known exactly:
        //   B(0.5) = (P0 + 3*P1 + 3*P2 + P3) / 8 = (30, 82.5)
        // The curve must pass through it and must NOT touch P1 or P2, which
        // is what separates real evaluation from a control-polygon fallback.
        var spline = new Spline { Degree = 3, Layer = curves };
        spline.ControlPoints.Add(new XYZ(10, 60, 0));
        spline.ControlPoints.Add(new XYZ(20, 90, 0));
        spline.ControlPoints.Add(new XYZ(40, 90, 0));
        spline.ControlPoints.Add(new XYZ(50, 60, 0));
        foreach (double k in new double[] { 0, 0, 0, 0, 1, 1, 1, 1 }) spline.Knots.Add(k);
        doc.Entities.Add(spline);

        // ---- solid hatch with an island ----------------------------------
        var solid = new Hatch { IsSolid = true, Layer = fills };
        solid.Paths.Add(Rectangle(70, 60, 110, 100));
        solid.Paths.Add(Rectangle(80, 70, 100, 90));
        doc.Entities.Add(solid);

        // ---- pattern hatch -----------------------------------------------
        var pattern = new HatchPattern("ANSI31");
        pattern.Lines.Add(new HatchPattern.Line
        {
            Angle = Math.PI / 4,
            BasePoint = new XY(0, 0),
            Offset = new XY(0, 4),
        });

        var hatched = new Hatch
        {
            IsSolid = false,
            Pattern = pattern,
            PatternScale = 1.0,
            Layer = fills,
        };
        hatched.Paths.Add(Rectangle(130, 60, 170, 100));
        doc.Entities.Add(hatched);

        // ---- dimension ----------------------------------------------------
        var dimension = new DimensionLinear
        {
            FirstPoint = new XYZ(10, 115, 0),
            SecondPoint = new XYZ(60, 115, 0),
            DefinitionPoint = new XYZ(60, 128, 0),
            Offset = 13,
            Layer = curves,
        };
        doc.Entities.Add(dimension);

        // Generate the dimension's geometry into its anonymous block, which
        // is what the importer then draws in place of laying it out itself.
        dimension.UpdateBlock();

        doc.Header.Version = ACadVersion.AC1015;
        DwgWriter.Write(path, doc);
    }

    private static Hatch.BoundaryPath Rectangle(double x0, double y0, double x1, double y1) =>
        new(new List<Hatch.BoundaryPath.Edge>
        {
            new Hatch.BoundaryPath.Line { Start = new XY(x0, y0), End = new XY(x1, y0) },
            new Hatch.BoundaryPath.Line { Start = new XY(x1, y0), End = new XY(x1, y1) },
            new Hatch.BoundaryPath.Line { Start = new XY(x1, y1), End = new XY(x0, y1) },
            new Hatch.BoundaryPath.Line { Start = new XY(x0, y1), End = new XY(x0, y0) },
        });
}
