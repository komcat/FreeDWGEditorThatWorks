using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;

namespace FreeDwg.Tests.Fixtures;

/// <summary>
/// A fixture built entirely around INSERT: scaling, rotation, mirroring,
/// non-uniform scale, nesting, a non-zero base point, ByBlock colour, and a
/// layer that has to stay switchable from outside the block.
/// </summary>
public static class BlocksDrawing
{
    public static void Write(string path)
    {
        var doc = new CadDocument();

        var frame = new Layer("FRAME") { Color = new Color(8) };
        var rings = new Layer("RINGS") { Color = new Color(4) };   // cyan, toggled at render time
        doc.Layers.Add(frame);
        doc.Layers.Add(rings);

        // ---- block TICK, base point at its own origin -------------------
        // Deliberately asymmetric (the flag on the +X arm) so that a mirror is
        // detectable, and carrying an arc so arc winding is exercised too.
        var tick = new BlockRecord("TICK");
        tick.Entities.Add(new Line(new XYZ(-5, 0, 0), new XYZ(5, 0, 0)) { Layer = frame });
        tick.Entities.Add(new Line(new XYZ(0, -5, 0), new XYZ(0, 5, 0)) { Layer = frame });
        tick.Entities.Add(new Line(new XYZ(5, 0, 0), new XYZ(5, 5, 0)) { Layer = frame });  // asymmetric flag
        tick.Entities.Add(new Circle { Center = XYZ.Zero, Radius = 3, Layer = rings });
        tick.Entities.Add(new Arc
        {
            Center = XYZ.Zero,
            Radius = 8,
            StartAngle = 0,
            EndAngle = Math.PI / 2,
            Layer = frame,
            Color = Color.ByBlock,   // takes the colour of whichever INSERT draws it
        });
        doc.BlockRecords.Add(tick);

        // ---- block PAIR, two nested TICKs -------------------------------
        var pair = new BlockRecord("PAIR");
        pair.Entities.Add(new Insert(tick) { InsertPoint = XYZ.Zero });
        pair.Entities.Add(new Insert(tick) { InsertPoint = new XYZ(20, 0, 0) });
        doc.BlockRecords.Add(pair);

        // ---- block OFFSET, geometry away from a non-zero base point -----
        var offset = new BlockRecord("OFFSET");
        offset.BlockEntity.BasePoint = new XYZ(10, 10, 0);
        offset.Entities.Add(new Circle { Center = new XYZ(10, 10, 0), Radius = 4, Layer = frame });
        doc.BlockRecords.Add(offset);

        // ---- placements --------------------------------------------------
        Place(doc, tick, 30, 30);                                   // plain
        Place(doc, tick, 80, 30, xs: 2, ys: 2);                     // uniform scale
        Place(doc, tick, 130, 30, rot: Math.PI / 4);                // rotated 45 deg
        Place(doc, tick, 180, 30, xs: -1);                          // mirrored in X
        Place(doc, tick, 30, 80, xs: 3, ys: 1);                     // non-uniform: circle -> ellipse
        Place(doc, pair, 100, 80);                                  // nested
        Place(doc, tick, 180, 80, color: new Color(1));             // ByBlock arc should turn red
        Place(doc, offset, 60, 110);                                // base point (10,10) lands here

        doc.Header.Version = ACadVersion.AC1015;
        DwgWriter.Write(path, doc);
    }

    private static void Place(CadDocument doc, BlockRecord block, double x, double y,
        double xs = 1, double ys = 1, double rot = 0, Color? color = null)
    {
        var insert = new Insert(block)
        {
            InsertPoint = new XYZ(x, y, 0),
            XScale = xs,
            YScale = ys,
            Rotation = rot,
        };
        if (color is not null) insert.Color = color.Value;
        doc.Entities.Add(insert);
    }
}
