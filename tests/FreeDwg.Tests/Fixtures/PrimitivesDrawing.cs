using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;

namespace FreeDwg.Tests.Fixtures;

/// <summary>
/// Lines, a circle, arcs including one that wraps past zero, polylines with
/// bulges in both directions, a layer that is switched off, and two entity
/// types the importer is expected to skip.
/// </summary>
public static class PrimitivesDrawing
{
    public static void Write(string path)
    {
        var doc = new CadDocument();

        var walls = new Layer("WALLS") { Color = new Color(1) };      // red
        var doors = new Layer("DOORS") { Color = new Color(3) };      // green
        var notes = new Layer("NOTES") { Color = new Color(4), IsOn = false };
        doc.Layers.Add(walls);
        doc.Layers.Add(doors);
        doc.Layers.Add(notes);

        // Outer rectangle, colour ByLayer, so it must come through red.
        AddLine(doc, walls, 0, 0, 200, 0);
        AddLine(doc, walls, 200, 0, 200, 120);
        AddLine(doc, walls, 200, 120, 0, 120);
        AddLine(doc, walls, 0, 120, 0, 0);

        doc.Entities.Add(new Circle { Center = new XYZ(50, 60, 0), Radius = 25, Layer = doors });

        // A quarter arc, and one whose end angle wraps past zero.
        doc.Entities.Add(new Arc
        {
            Center = new XYZ(150, 60, 0),
            Radius = 30,
            StartAngle = 0,
            EndAngle = Math.PI / 2,
            Layer = doors,
        });

        doc.Entities.Add(new Arc
        {
            Center = new XYZ(150, 60, 0),
            Radius = 38,
            StartAngle = Math.PI * 7 / 4,
            EndAngle = Math.PI / 4,
            Layer = doors,
        });

        // Closed polyline with semicircular caps: the bulges must swing
        // outward, past the straight runs.
        var stadium = new LwPolyline { IsClosed = true, Layer = walls, Color = new Color(60, 160, 255) };
        stadium.Vertices.Add(new LwPolyline.Vertex(new XY(40, 15)) { Bulge = 0 });
        stadium.Vertices.Add(new LwPolyline.Vertex(new XY(160, 15)) { Bulge = 1.0 });
        stadium.Vertices.Add(new LwPolyline.Vertex(new XY(160, 35)) { Bulge = 0 });
        stadium.Vertices.Add(new LwPolyline.Vertex(new XY(40, 35)) { Bulge = 1.0 });
        doc.Entities.Add(stadium);

        // Open polyline alternating bulge direction; the sagitta is
        // bulge * chord / 2, so these reach exactly y = 85 and y = 115.
        var wave = new LwPolyline { IsClosed = false, Layer = doors };
        wave.Vertices.Add(new LwPolyline.Vertex(new XY(20, 100)) { Bulge = 0.6 });
        wave.Vertices.Add(new LwPolyline.Vertex(new XY(70, 100)) { Bulge = -0.6 });
        wave.Vertices.Add(new LwPolyline.Vertex(new XY(120, 100)) { Bulge = 0.6 });
        wave.Vertices.Add(new LwPolyline.Vertex(new XY(170, 100)) { Bulge = 0 });
        doc.Entities.Add(wave);

        // On a layer that is switched off, so it must not be drawn.
        AddLine(doc, notes, 0, 0, 200, 120);

        // POINT is unsupported on purpose, to exercise the diagnostics tally.
        doc.Entities.Add(new Point(new XYZ(100, 60, 0)) { Layer = walls });

        // Supported since text landed; here so that the tally distinguishes
        // the two rather than lumping everything unfamiliar together.
        doc.Entities.Add(new TextEntity { Value = "LABEL", InsertPoint = new XYZ(10, 10, 0), Height = 5, Layer = walls });

        doc.Header.Version = ACadVersion.AC1015;
        DwgWriter.Write(path, doc);
    }

    private static void AddLine(CadDocument doc, Layer layer, double x1, double y1, double x2, double y2) =>
        doc.Entities.Add(new Line(new XYZ(x1, y1, 0), new XYZ(x2, y2, 0)) { Layer = layer });
}
