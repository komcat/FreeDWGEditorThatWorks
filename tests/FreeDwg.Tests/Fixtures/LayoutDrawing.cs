using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;

namespace FreeDwg.Tests.Fixtures;

/// <summary>
/// A paper space sheet with one viewport onto model space.
/// </summary>
/// <remarks>
/// The numbers are chosen so the mapping is exact and checkable: the viewport
/// is 200x120 paper units centred at (150,110) and shows 60 model units of
/// height, so the scale is exactly 2 and the model rectangle lands precisely
/// on the viewport frame. A model line running far past the drawing is there
/// to prove the window clips.
/// </remarks>
public static class LayoutDrawing
{
    public static void Write(string path)
    {
        var doc = new CadDocument();

        var model = new Layer("MODEL") { Color = new Color(3) };
        var vpFrozen = new Layer("VPFROZEN") { Color = new Color(4) };
        var sheet = new Layer("SHEET") { Color = new Color(2) };
        doc.Layers.Add(model);
        doc.Layers.Add(vpFrozen);
        doc.Layers.Add(sheet);

        // ---- model space -------------------------------------------------
        Rect(doc.Entities.Add, model, 0, 0, 100, 60);
        doc.Entities.Add(new Circle { Center = new XYZ(50, 30, 0), Radius = 10, Layer = vpFrozen });

        // Runs well outside the sheet's window; must be clipped at its frame.
        doc.Entities.Add(new Line(new XYZ(-200, 50, 0), new XYZ(300, 50, 0)) { Layer = model });

        // ---- paper space ---------------------------------------------------
        var layout = new Layout("Sheet1") { PaperWidth = 297, PaperHeight = 210 };
        doc.Layouts.Add(layout);

        var block = layout.AssociatedBlock;

        if (block is not null)
        {
            Rect(e => block.Entities.Add(e), sheet, 0, 0, 297, 210);
        }

        var viewport = new Viewport
        {
            Center = new XYZ(150, 110, 0),
            Width = 200,
            Height = 120,
            ViewCenter = new XY(50, 30),
            ViewHeight = 60,
            Layer = sheet,
        };

        layout.AddViewport(viewport);

        doc.Header.Version = ACadVersion.AC1015;
        DwgWriter.Write(path, doc);
    }

    private static void Rect(Action<Entity> add, Layer layer, double x0, double y0, double x1, double y1)
    {
        add(new Line(new XYZ(x0, y0, 0), new XYZ(x1, y0, 0)) { Layer = layer });
        add(new Line(new XYZ(x1, y0, 0), new XYZ(x1, y1, 0)) { Layer = layer });
        add(new Line(new XYZ(x1, y1, 0), new XYZ(x0, y1, 0)) { Layer = layer });
        add(new Line(new XYZ(x0, y1, 0), new XYZ(x0, y0, 0)) { Layer = layer });
    }
}
