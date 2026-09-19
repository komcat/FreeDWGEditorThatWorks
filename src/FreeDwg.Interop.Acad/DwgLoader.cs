using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Styling;
using AcadArc = ACadSharp.Entities.Arc;
using AcadCircle = ACadSharp.Entities.Circle;
using AcadLine = ACadSharp.Entities.Line;
using SceneLayer = FreeDwg.Core.Scene.Layer;
using SceneLinetype = FreeDwg.Core.Styling.Linetype;

namespace FreeDwg.Interop.Acad;

/// <summary>
/// Translates an ACadSharp <see cref="CadDocument"/> into the editor's scene
/// model. This is the anti-corruption layer: it is the only assembly that
/// references ACadSharp, and nothing downstream of it can tell which library
/// (or hand-written parser) produced the drawing.
/// </summary>
public static class DwgLoader
{
    /// <summary>Reads a DWG or DXF file and converts it to a scene.</summary>
    public static Drawing Load(string path, ImportDiagnostics? diagnostics = null)
    {
        var diag = diagnostics ?? new ImportDiagnostics();

        CadDocument document = IsDxf(path)
            ? DxfReader.Read(path, (_, e) => diag.OnNotification(e))
            : DwgReader.Read(path, (_, e) => diag.OnNotification(e));

        var drawing = Convert(document, diag);
        drawing.SourcePath = path;
        return drawing;
    }

    /// <summary>Converts an already-open document. Model space only, for now.</summary>
    public static Drawing Convert(CadDocument document, ImportDiagnostics diagnostics) =>
        new Converter(document, diagnostics).Run();

    private static bool IsDxf(string path) =>
        string.Equals(Path.GetExtension(path), ".dxf", StringComparison.OrdinalIgnoreCase);

    private sealed class Converter
    {
        /// <summary>A MINSERT grid this large is almost certainly corrupt data.</summary>
        private const int MaxArrayInstances = 10_000;

        /// <summary>
        /// AutoCAD spaces MTEXT lines at 5/3 of the text height when the line
        /// spacing factor is 1. TEXT has no such notion; it is one line.
        /// </summary>
        private const double MTextLineSpacingBase = 5.0 / 3.0;

        private readonly CadDocument _document;
        private readonly ImportDiagnostics _diagnostics;
        private readonly Drawing _drawing = new();
        private readonly Dictionary<ulong, int> _layerIndexByHandle = new();
        private readonly Dictionary<ulong, BlockDefinition> _blockByHandle = new();
        private readonly Dictionary<ulong, SceneLinetype> _linetypeByHandle = new();
        private readonly FontResolver _fonts = new();
        private StyleResolver _styles = null!;
        private int _fallbackLayer;

        public Converter(CadDocument document, ImportDiagnostics diagnostics)
        {
            _document = document;
            _diagnostics = diagnostics;
        }

        public Drawing Run()
        {
            ConvertLinetypes();
            _styles = new StyleResolver(_linetypeByHandle, _document.Header?.LineTypeScale ?? 1.0);

            ConvertLayers();
            ConvertBlocks();

            foreach (var entity in _document.Entities)
                AddConverted(entity, _drawing.Entities);

            _diagnostics.ImportedCount = _drawing.Entities.Count;
            foreach (var (shx, substitute) in _fonts.Substitutions)
                _diagnostics.FontSubstituted(shx, substitute);

            return _drawing;
        }

        private void ConvertLinetypes()
        {
            foreach (var lineType in _document.LineTypes)
            {
                var converted = StyleResolver.ToLinetype(lineType);
                _linetypeByHandle[lineType.Handle] = converted;
                if (!converted.IsContinuous) _drawing.Linetypes.Add(converted);
            }
        }

        private void ConvertLayers()
        {
            _fallbackLayer = -1;

            foreach (var acadLayer in _document.Layers)
            {
                int index = _drawing.AddLayer(new SceneLayer(acadLayer.Name)
                {
                    Color = StyleResolver.ToRgb(acadLayer.Color, Rgb.White),
                    Lineweight = StyleResolver.ToLineweight(acadLayer.LineWeight, Lineweight.Default),
                    Linetype = _styles.LinetypeOf(acadLayer.LineType),
                    SourceHandle = acadLayer.Handle,
                    IsOn = acadLayer.IsOn,
                    IsFrozen = acadLayer.Flags.HasFlag(LayerFlags.Frozen),
                    IsLocked = acadLayer.Flags.HasFlag(LayerFlags.Locked),
                });

                _layerIndexByHandle[acadLayer.Handle] = index;

                // Layer "0" always exists in a well-formed drawing and is the
                // natural home for entities whose own layer we cannot resolve.
                if (_fallbackLayer < 0 && acadLayer.Name == "0") _fallbackLayer = index;
            }

            if (_fallbackLayer < 0) _fallbackLayer = _drawing.AddLayer(new SceneLayer("0"));
        }

        /// <summary>
        /// Two passes: create every block definition first, then fill them.
        /// A block can reference another block that appears later in the table,
        /// so the references have to exist before any of them are populated.
        /// </summary>
        private void ConvertBlocks()
        {
            var records = new List<BlockRecord>();

            foreach (var record in _document.BlockRecords)
            {
                // Model and paper space are block records too, but their
                // contents are the drawing itself, not reusable definitions.
                if (ReferenceEquals(record, _document.ModelSpace)) continue;
                if (record.Layout is not null) continue;

                var block = new BlockDefinition(record.Name)
                {
                    BasePoint = ToVec2(record.BlockEntity?.BasePoint ?? XYZ.Zero),
                    SourceHandle = record.Handle,
                    IsAnonymous = record.IsAnonymous,
                };

                _blockByHandle[record.Handle] = _drawing.AddBlock(block);
                records.Add(record);
            }

            foreach (var record in records)
            {
                var block = _blockByHandle[record.Handle];
                foreach (var entity in record.Entities)
                    AddConverted(entity, block.Entities);
            }
        }

        private void AddConverted(Entity entity, List<SceneEntity> target)
        {
            if (entity.IsInvisible) return;

            // An INSERT may expand into a grid of instances (MINSERT).
            if (entity is Insert insert)
            {
                AddInserts(insert, target);
                return;
            }

            var converted = ConvertEntity(entity);
            if (converted is null)
            {
                _diagnostics.Unsupported(entity);
                return;
            }

            Decorate(converted, entity);
            target.Add(converted);
        }

        private void AddInserts(Insert insert, List<SceneEntity> target)
        {
            if (insert.Block is null || !_blockByHandle.TryGetValue(insert.Block.Handle, out var block))
            {
                _diagnostics.Unsupported(insert);
                return;
            }

            int columns = Math.Max(1, (int)insert.ColumnCount);
            int rows = Math.Max(1, (int)insert.RowCount);

            if ((long)columns * rows > MaxArrayInstances)
            {
                _diagnostics.Note($"INSERT {insert.Handle:X}: {columns}x{rows} array exceeds {MaxArrayInstances} instances; drawing one.");
                columns = 1;
                rows = 1;
            }

            var origin = ToVec2(insert.InsertPoint);
            double rotation = insert.Rotation;

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    // Array spacing runs along the block's own axes, so the
                    // offset rotates with the insert.
                    var offset = Mat3.Rotation(rotation).TransformVector(
                        new Vec2(column * insert.ColumnSpacing, row * insert.RowSpacing));

                    var transform = SInsert.BuildTransform(
                        block.BasePoint, origin + offset, insert.XScale, insert.YScale, rotation);

                    var scene = new SInsert(block, transform);
                    Decorate(scene, insert);
                    target.Add(scene);
                }
            }
        }

        private void Decorate(SceneEntity scene, Entity source)
        {
            int layerIndex = source.Layer is not null &&
                             _layerIndexByHandle.TryGetValue(source.Layer.Handle, out int index)
                ? index
                : _fallbackLayer;

            var layer = _drawing.Layers[layerIndex];
            var (style, inherits) = _styles.Resolve(source, layer, layer.Linetype);

            scene.LayerIndex = layerIndex;
            scene.Style = style;
            scene.Inherits = inherits;
            scene.SourceHandle = source.Handle;
        }

        private SceneEntity? ConvertEntity(Entity entity) => entity switch
        {
            // Arc derives from Circle in ACadSharp, so it has to be matched first.
            AcadArc arc => ConvertArc(arc),
            AcadCircle circle => new SCircle(ToVec2(circle.Center), circle.Radius),
            AcadLine line => new SLine(ToVec2(line.StartPoint), ToVec2(line.EndPoint)),
            LwPolyline polyline => ConvertLwPolyline(polyline),
            MText mtext => ConvertMText(mtext),
            TextEntity text => ConvertText(text),
            _ => null,
        };

        private SText? ConvertText(TextEntity text)
        {
            if (string.IsNullOrEmpty(text.Value) || text.Height <= 0) return null;

            var (family, bold, italic, styleWidth, styleOblique) = _fonts.Resolve(text.Style);

            var (anchorX, anchorY) = AnchorFor(text.HorizontalAlignment, text.VerticalAlignment);

            // DWG keeps the insertion point in group 10 but switches to the
            // alignment point in group 11 as soon as the text is not plain
            // left-baseline. Using the wrong one shifts every aligned label.
            bool usesAlignmentPoint =
                text.HorizontalAlignment != TextHorizontalAlignment.Left ||
                text.VerticalAlignment != TextVerticalAlignmentType.Baseline;

            var position = ToVec2(usesAlignmentPoint ? text.AlignmentPoint : text.InsertPoint);

            double widthFactor = text.WidthFactor > 0 ? text.WidthFactor : styleWidth;

            return new SText(new[] { text.Value }, position, text.Height)
            {
                Rotation = text.Rotation,
                WidthFactor = widthFactor,
                ObliqueAngle = text.ObliqueAngle != 0 ? text.ObliqueAngle : styleOblique,
                LineStep = text.Height,
                AnchorX = anchorX,
                AnchorY = anchorY,
                FontFamily = family,
                Bold = bold,
                Italic = italic,
            };
        }

        private SText? ConvertMText(MText mtext)
        {
            // ACadSharp already strips MTEXT's inline formatting codes and
            // splits on the paragraph breaks, which is the bulk of the work.
            string[] lines = mtext.GetPlainTextLines();
            if (lines.Length == 0 || mtext.Height <= 0) return null;

            var (family, bold, italic, _, _) = _fonts.Resolve(mtext.Style);
            var (anchorX, anchorY) = AnchorFor(mtext.AttachmentPoint);

            double factor = mtext.LineSpacing > 0 ? mtext.LineSpacing : 1.0;

            return new SText(lines, ToVec2(mtext.InsertPoint), mtext.Height)
            {
                Rotation = mtext.Rotation,
                LineStep = mtext.Height * MTextLineSpacingBase * factor,
                AnchorX = anchorX,
                AnchorY = anchorY,
                WrapWidth = mtext.RectangleWidth > 0 ? mtext.RectangleWidth : 0,
                FontFamily = family,
                Bold = bold,
                Italic = italic,
            };
        }

        private static (TextAnchorX, TextAnchorY) AnchorFor(
            TextHorizontalAlignment horizontal, TextVerticalAlignmentType vertical)
        {
            // Aligned and Fit stretch the text between two points; treated as
            // left-aligned here, so such text is placed but not stretched.
            TextAnchorX x = horizontal switch
            {
                TextHorizontalAlignment.Center or TextHorizontalAlignment.Middle => TextAnchorX.Center,
                TextHorizontalAlignment.Right => TextAnchorX.Right,
                _ => TextAnchorX.Left,
            };

            TextAnchorY y = horizontal == TextHorizontalAlignment.Middle
                ? TextAnchorY.Middle
                : vertical switch
                {
                    TextVerticalAlignmentType.Bottom => TextAnchorY.Bottom,
                    TextVerticalAlignmentType.Middle => TextAnchorY.Middle,
                    TextVerticalAlignmentType.Top => TextAnchorY.Top,
                    _ => TextAnchorY.Baseline,
                };

            return (x, y);
        }

        private static (TextAnchorX, TextAnchorY) AnchorFor(AttachmentPointType attachment) => attachment switch
        {
            AttachmentPointType.TopLeft => (TextAnchorX.Left, TextAnchorY.Top),
            AttachmentPointType.TopCenter => (TextAnchorX.Center, TextAnchorY.Top),
            AttachmentPointType.TopRight => (TextAnchorX.Right, TextAnchorY.Top),
            AttachmentPointType.MiddleLeft => (TextAnchorX.Left, TextAnchorY.Middle),
            AttachmentPointType.MiddleCenter => (TextAnchorX.Center, TextAnchorY.Middle),
            AttachmentPointType.MiddleRight => (TextAnchorX.Right, TextAnchorY.Middle),
            AttachmentPointType.BottomLeft => (TextAnchorX.Left, TextAnchorY.Bottom),
            AttachmentPointType.BottomCenter => (TextAnchorX.Center, TextAnchorY.Bottom),
            AttachmentPointType.BottomRight => (TextAnchorX.Right, TextAnchorY.Bottom),
            _ => (TextAnchorX.Left, TextAnchorY.Top),
        };

        private static SArc ConvertArc(AcadArc arc)
        {
            // DWG stores start and end angles counter-clockwise; the arc is
            // always drawn CCW from start to end, so a wrapped end angle is
            // normal rather than an error.
            double sweep = ArcMath.Normalize(arc.EndAngle - arc.StartAngle);
            if (sweep < 1e-12) sweep = ArcMath.TwoPi;

            return new SArc(ToVec2(arc.Center), arc.Radius, arc.StartAngle, sweep);
        }

        private static SPolyline ConvertLwPolyline(LwPolyline polyline)
        {
            var vertices = new PolyVertex[polyline.Vertices.Count];
            for (int i = 0; i < vertices.Length; i++)
            {
                var v = polyline.Vertices[i];
                vertices[i] = new PolyVertex(new Vec2(v.Location.X, v.Location.Y), v.Bulge);
            }
            return new SPolyline(vertices, polyline.IsClosed);
        }

        /// <summary>
        /// Projects to the XY plane. A 2D editor has no use for Z, and
        /// model-space drafting keeps everything at elevation zero anyway.
        /// </summary>
        private static Vec2 ToVec2(XYZ p) => new(p.X, p.Y);
    }
}
