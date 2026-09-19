using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;

namespace FreeDwg.Tests.Fixtures;

/// <summary>
/// Text placement and dash patterns: every TEXT alignment that changes which
/// point DWG treats as the anchor, MTEXT with and without a wrap rectangle,
/// and lines carrying a linetype by entity and by layer.
/// </summary>
public static class TextDrawing
{
    private static TextStyle? _shxStyle;

    public static void Write(string path)
    {
        var doc = new CadDocument();

        var anno = new Layer("ANNO") { Color = new Color(2) };
        doc.Layers.Add(anno);

        // DWG's native stroke font; the renderer has to substitute and say so.
        var shxStyle = new TextStyle("ROMANS") { Filename = "romans.shx" };
        doc.TextStyles.Add(shxStyle);
        _shxStyle = shxStyle;

        // ---- linetypes -------------------------------------------------
        var dashed = new LineType("DASHED5");
        dashed.AddSegment(new LineType.Segment { Length = 5 });
        dashed.AddSegment(new LineType.Segment { Length = -5 });
        doc.LineTypes.Add(dashed);

        var center = new LineType("CENTERLINE");
        center.AddSegment(new LineType.Segment { Length = 12 });
        center.AddSegment(new LineType.Segment { Length = -3 });
        center.AddSegment(new LineType.Segment { Length = 2 });
        center.AddSegment(new LineType.Segment { Length = -3 });
        doc.LineTypes.Add(center);

        var dashedLayer = new Layer("HIDDEN-LT") { Color = new Color(4), LineType = dashed };
        doc.Layers.Add(dashedLayer);

        // ---- lines: solid, dashed by entity, dashed by layer ------------
        doc.Entities.Add(new Line(new XYZ(10, -16, 0), new XYZ(110, -16, 0)) { Layer = anno });
        doc.Entities.Add(new Line(new XYZ(10, -8, 0), new XYZ(110, -8, 0)) { Layer = anno, LineType = dashed });
        doc.Entities.Add(new Line(new XYZ(10, 0, 0), new XYZ(110, 0, 0)) { Layer = dashedLayer });
        doc.Entities.Add(new Line(new XYZ(10, 8, 0), new XYZ(110, 8, 0)) { Layer = anno, LineType = center });

        // ---- TEXT: the alignments that switch DWG to the alignment point -
        Text(doc, anno, "LEFT", 10, 25, 8,
            TextHorizontalAlignment.Left, TextVerticalAlignmentType.Baseline);

        Text(doc, anno, "RIGHT", 110, 25, 8,
            TextHorizontalAlignment.Right, TextVerticalAlignmentType.Baseline);

        Text(doc, anno, "CENTER", 60, 45, 8,
            TextHorizontalAlignment.Center, TextVerticalAlignmentType.Middle);

        // Rotated and stretched.
        var rotated = Text(doc, anno, "ROT30", 10, 60, 8,
            TextHorizontalAlignment.Left, TextVerticalAlignmentType.Baseline);
        rotated.Rotation = Math.PI / 6;

        var wide = Text(doc, anno, "WIDE", 75, 60, 8,
            TextHorizontalAlignment.Left, TextVerticalAlignmentType.Baseline);
        wide.WidthFactor = 2.0;

        // ---- MTEXT ------------------------------------------------------
        doc.Entities.Add(new MText
        {
            Value = "TOP LEFT\\PSECOND LINE\\PTHIRD",
            InsertPoint = new XYZ(10, 100, 0),
            Height = 6,
            AttachmentPoint = AttachmentPointType.TopLeft,
            Layer = anno,
        });

        doc.Entities.Add(new MText
        {
            Value = "wrapped paragraph that should break across several lines",
            InsertPoint = new XYZ(95, 100, 0),
            Height = 5,
            RectangleWidth = 40,
            AttachmentPoint = AttachmentPointType.TopLeft,
            Layer = anno,
        });

        doc.Header.Version = ACadVersion.AC1015;
        DwgWriter.Write(path, doc);
    }

    private static TextEntity Text(CadDocument doc, Layer layer, string value, double x, double y,
        double height, TextHorizontalAlignment horizontal, TextVerticalAlignmentType vertical)
    {
        var text = new TextEntity(value)
        {
            Height = height,
            Layer = layer,
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            InsertPoint = new XYZ(x, y, 0),
            AlignmentPoint = new XYZ(x, y, 0),
            Style = _shxStyle,
        };
        doc.Entities.Add(text);
        return text;
    }
}
