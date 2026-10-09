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
using AcadEllipse = ACadSharp.Entities.Ellipse;
using AcadLine = ACadSharp.Entities.Line;
using SceneLayer = FreeDwg.Core.Scene.Layer;
using SceneLayout = FreeDwg.Core.Scene.Layout;
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

        var drawing = Convert(Read(path, diag), diag);
        drawing.SourcePath = path;
        return drawing;
    }

    /// <summary>Reads the file into ACadSharp's model, without converting it.</summary>
    internal static CadDocument Read(string path, ImportDiagnostics diagnostics) =>
        IsDxf(path)
            ? DxfReader.Read(path, (_, e) => diagnostics.OnNotification(e))
            : DwgReader.Read(path, (_, e) => diagnostics.OnNotification(e));

    /// <summary>Converts an already-open document, model space and all sheets.</summary>
    public static Drawing Convert(CadDocument document, ImportDiagnostics diagnostics) =>
        new Converter(document, diagnostics).Run();

    internal static bool IsDxf(string path) =>
        string.Equals(Path.GetExtension(path), ".dxf", StringComparison.OrdinalIgnoreCase);

    /// <summary>The unit an INSUNITS value names, or Unitless for one not modelled.</summary>
    internal static DrawingUnits UnitsOf(ACadSharp.Types.Units.UnitsType insUnits) => insUnits switch
    {
        ACadSharp.Types.Units.UnitsType.Millimeters => DrawingUnits.Millimetres,
        ACadSharp.Types.Units.UnitsType.Centimeters => DrawingUnits.Centimetres,
        ACadSharp.Types.Units.UnitsType.Meters => DrawingUnits.Metres,
        ACadSharp.Types.Units.UnitsType.Kilometers => DrawingUnits.Kilometres,
        ACadSharp.Types.Units.UnitsType.Inches => DrawingUnits.Inches,
        ACadSharp.Types.Units.UnitsType.Feet => DrawingUnits.Feet,
        ACadSharp.Types.Units.UnitsType.Yards => DrawingUnits.Yards,
        ACadSharp.Types.Units.UnitsType.Miles => DrawingUnits.Miles,
        _ => DrawingUnits.Unitless,
    };

    /// <summary>The INSUNITS value for a unit, the inverse of <see cref="UnitsOf"/>.</summary>
    internal static ACadSharp.Types.Units.UnitsType InsUnitsOf(DrawingUnits units) => units switch
    {
        DrawingUnits.Millimetres => ACadSharp.Types.Units.UnitsType.Millimeters,
        DrawingUnits.Centimetres => ACadSharp.Types.Units.UnitsType.Centimeters,
        DrawingUnits.Metres => ACadSharp.Types.Units.UnitsType.Meters,
        DrawingUnits.Kilometres => ACadSharp.Types.Units.UnitsType.Kilometers,
        DrawingUnits.Inches => ACadSharp.Types.Units.UnitsType.Inches,
        DrawingUnits.Feet => ACadSharp.Types.Units.UnitsType.Feet,
        DrawingUnits.Yards => ACadSharp.Types.Units.UnitsType.Yards,
        DrawingUnits.Miles => ACadSharp.Types.Units.UnitsType.Miles,
        _ => ACadSharp.Types.Units.UnitsType.Unitless,
    };

    private sealed class Converter
    {
        /// <summary>A MINSERT grid this large is almost certainly corrupt data.</summary>
        private const int MaxArrayInstances = 10_000;

        /// <summary>Segments per curved boundary edge when flattening a hatch.</summary>
        private const int HatchBoundaryPrecision = 64;

        /// <summary>
        /// A dense pattern over a large region can run to millions of strokes,
        /// which is neither drawable nor useful.
        /// </summary>
        private const int MaxHatchSegments = 200_000;

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
            ConvertUnits();

            ConvertLinetypes();
            _styles = new StyleResolver(_linetypeByHandle, _document.Header?.LineTypeScale ?? 1.0);

            ConvertLayers();
            ConvertBlocks();
            ConvertLayouts();

            foreach (var layout in _drawing.Layouts)
                _diagnostics.ImportedCount += layout.Entities.Count;
            foreach (var (shx, substitute) in _fonts.Substitutions)
                _diagnostics.FontSubstituted(shx, substitute);

            return _drawing;
        }

        /// <summary>
        /// Takes the drawing's unit from the file's INSUNITS header.
        /// </summary>
        /// <remarks>
        /// The scene holds bare numbers, so this changes nothing about the
        /// geometry; it says what those numbers count, which is what lets a
        /// length typed as 2in land correctly in a drawing built in inches.
        /// A file naming a unit this model does not carry, or naming none at
        /// all, reads as Unitless and is reported -- it used to fall back to
        /// millimetres, which put a unit on the readout that nothing in the
        /// file had said.
        /// </remarks>
        private void ConvertUnits()
        {
            var insUnits = _document.Header?.InsUnits ?? ACadSharp.Types.Units.UnitsType.Unitless;

            _drawing.Units = UnitsOf(insUnits);

            if (_drawing.Units != DrawingUnits.Unitless) return;

            _diagnostics.Note(insUnits == ACadSharp.Types.Units.UnitsType.Unitless
                ? "The file does not say what its units are; lengths are shown as bare numbers."
                : $"Unit {insUnits} is not one this editor models; lengths are shown as bare numbers.");
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

        /// <summary>
        /// Model space, then each paper space sheet in tab order. Model space
        /// has to come first so that the viewports on a sheet have something
        /// to point at.
        /// </summary>
        private void ConvertLayouts()
        {
            foreach (var entity in _document.Entities)
                AddConverted(entity, _drawing.ModelSpace.Entities);

            var sheets = _document.Layouts
                .Where(layout => layout.IsPaperSpace)
                .OrderBy(layout => layout.TabOrder);

            foreach (var acadLayout in sheets)
            {
                var layout = _drawing.AddLayout(new SceneLayout(acadLayout.Name, isPaperSpace: true)
                {
                    PaperWidth = acadLayout.PaperWidth,
                    PaperHeight = acadLayout.PaperHeight,
                    SourceHandle = acadLayout.Handle,
                });

                if (acadLayout.AssociatedBlock is null) continue;

                foreach (var entity in acadLayout.AssociatedBlock.Entities)
                    AddConverted(entity, layout.Entities);
            }
        }

        private SceneEntity? ConvertViewport(Viewport viewport)
        {
            // Every sheet carries a pseudo-viewport standing for the sheet
            // itself; it is not a window onto anything.
            if (viewport.RepresentsPaper) return null;
            if (viewport.Width <= 0 || viewport.Height <= 0 || viewport.ViewHeight <= 0) return null;

            var center = ToVec2(viewport.Center);
            var rect = Bounds2.FromCorners(
                new Vec2(center.X - viewport.Width / 2, center.Y - viewport.Height / 2),
                new Vec2(center.X + viewport.Width / 2, center.Y + viewport.Height / 2));

            HashSet<int>? frozen = null;
            foreach (var layer in viewport.FrozenLayers)
            {
                if (!_layerIndexByHandle.TryGetValue(layer.Handle, out int index)) continue;
                (frozen ??= new HashSet<int>()).Add(index);
            }

            return new SViewport(_drawing.ModelSpace, rect,
                new Vec2(viewport.ViewCenter.X, viewport.ViewCenter.Y), viewport.ViewHeight)
            {
                TwistAngle = viewport.TwistAngle,
                IsOn = !viewport.Status.HasFlag(ViewportStatusFlags.ViewportOff),
                FrozenLayers = frozen,
            };
        }

        private void AddConverted(Entity entity, List<SceneEntity> target)
        {
            if (entity.IsInvisible) return;

            // Every sheet carries a pseudo-viewport standing for the sheet
            // itself. Dropping it here rather than in the converter keeps it
            // out of the unsupported tally, which is for things we cannot
            // draw rather than things there is nothing to draw.
            if (entity is Viewport { RepresentsPaper: true }) return;

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
            Polyline2D polyline => ConvertPolyline(polyline.Vertices, polyline.IsClosed),
            Polyline3D polyline => ConvertPolyline(polyline.Vertices, polyline.IsClosed),
            AcadEllipse ellipse => ConvertEllipse(ellipse),
            Spline spline => ConvertSpline(spline),
            Hatch hatch => ConvertHatch(hatch),
            Solid solid => ConvertSolid(solid),
            Viewport viewport => ConvertViewport(viewport),
            Dimension dimension => ConvertDimension(dimension),
            MText mtext => ConvertMText(mtext),
            TextEntity text => ConvertText(text),
            _ => null,
        };

        /// <summary>
        /// A dimension carries its own generated geometry in an anonymous
        /// block: the extension lines, arrowheads and text that AutoCAD
        /// produced when it was last regenerated. Drawing that block renders
        /// the dimension exactly as authored and skips reimplementing
        /// dimension layout, which is only needed once the editor has to
        /// regenerate one after an edit.
        /// </summary>
        private SceneEntity? ConvertDimension(Dimension dimension)
        {
            if (dimension.Block is null ||
                !_blockByHandle.TryGetValue(dimension.Block.Handle, out var block))
            {
                return null;
            }

            var transform = SInsert.BuildTransform(
                block.BasePoint, ToVec2(dimension.InsertionPoint), 1, 1, 0);

            return new SInsert(block, transform);
        }

        /// <summary>
        /// A filled triangle or quadrilateral. Worth supporting early despite
        /// being a primitive nobody draws by hand: it is what dimension
        /// arrowheads are made of, so without it every dimension renders
        /// without its arrows.
        /// </summary>
        private static SHatch? ConvertSolid(Solid solid)
        {
            // The corners are stored in a Z order, not around the outline:
            // the third and fourth are swapped relative to the drawn shape.
            var corners = new List<Vec2>
            {
                ToVec2(solid.FirstCorner),
                ToVec2(solid.SecondCorner),
                ToVec2(solid.FourthCorner),
                ToVec2(solid.ThirdCorner),
            };

            // A triangular SOLID repeats a corner; a duplicate would give the
            // even-odd fill a zero-area spur.
            var ring = new List<Vec2>();
            foreach (var corner in corners)
            {
                if (ring.Count > 0 && Vec2.Distance(ring[^1], corner) < 1e-9) continue;
                ring.Add(corner);
            }
            if (ring.Count > 2 && Vec2.Distance(ring[0], ring[^1]) < 1e-9) ring.RemoveAt(ring.Count - 1);

            if (ring.Count < 3) return null;
            return new SHatch(new[] { (IReadOnlyList<Vec2>)ring }) { IsSolid = true };
        }

        private static SEllipse ConvertEllipse(AcadEllipse ellipse)
        {
            // The major axis is stored as a vector from the centre, so it
            // carries the ellipse's rotation with it.
            var majorAxis = ToVec2(ellipse.MajorAxisEndPoint);

            double sweep = ArcMath.Normalize(ellipse.EndParameter - ellipse.StartParameter);
            if (sweep < 1e-12) sweep = ArcMath.TwoPi;

            return new SEllipse(ToVec2(ellipse.Center), majorAxis, ellipse.RadiusRatio,
                ellipse.StartParameter, sweep);
        }

        private SceneEntity? ConvertSpline(Spline spline)
        {
            var controlPoints = spline.ControlPoints;

            if (controlPoints.Count == 0 && spline.FitPoints.Count > 0)
            {
                // A fit-point spline stores the points it passes through
                // rather than a control polygon; ask the reader to derive one.
                try
                {
                    spline.UpdateFromFitPoints(64);
                    controlPoints = spline.ControlPoints;
                }
                catch (Exception ex)
                {
                    _diagnostics.Note(
                        $"SPLINE {spline.Handle:X}: could not derive control points ({ex.GetType().Name}); drawing its fit points.");
                }
            }

            if (controlPoints.Count == 0)
            {
                if (spline.FitPoints.Count < 2) return null;
                return new SPolyline(ToVertices(spline.FitPoints), spline.IsClosed);
            }

            var points = new Vec2[controlPoints.Count];
            for (int i = 0; i < points.Length; i++) points[i] = ToVec2(controlPoints[i]);

            var knots = spline.Knots;
            if (!BSpline.IsValid(points.Length, knots.Count, spline.Degree))
            {
                _diagnostics.Note(
                    $"SPLINE {spline.Handle:X}: knot vector does not match degree {spline.Degree} with {points.Length} control points; drawing the control polygon.");
            }

            return new SSpline(points, knots, spline.Degree)
            {
                Weights = spline.Weights.Count == points.Length ? spline.Weights : null,
                IsClosed = spline.IsClosed,
            };
        }

        private SceneEntity? ConvertHatch(Hatch hatch)
        {
            var loops = new List<IReadOnlyList<Vec2>>();

            foreach (var path in hatch.Paths)
            {
                var ring = Dedupe(path.GetPoints(HatchBoundaryPrecision));
                if (ring.Count >= 3) loops.Add(ring);
            }

            var scene = new SHatch(loops) { IsSolid = hatch.IsSolid };

            if (hatch.IsSolid) return loops.Count > 0 ? scene : null;

            // The reader generates the pattern strokes already clipped to the
            // boundary, which is the expensive half of hatching.
            try
            {
                var segments = new List<Segment2>();
                foreach (var entity in hatch.ExplodePattern())
                {
                    if (entity is AcadLine line)
                        segments.Add(new Segment2(ToVec2(line.StartPoint), ToVec2(line.EndPoint)));

                    if (segments.Count >= MaxHatchSegments)
                    {
                        _diagnostics.Note($"HATCH {hatch.Handle:X}: pattern exceeds {MaxHatchSegments} strokes; truncated.");
                        break;
                    }
                }
                scene.PatternSegments = segments;
            }
            catch (Exception ex)
            {
                _diagnostics.Note($"HATCH {hatch.Handle:X}: pattern could not be generated ({ex.GetType().Name}).");
            }

            if (scene.PatternSegments.Count == 0 && loops.Count == 0) return null;
            return scene;
        }

        /// <summary>Drops the repeated endpoints that an edge-by-edge boundary produces.</summary>
        private static List<Vec2> Dedupe(IEnumerable<XYZ> points)
        {
            var ring = new List<Vec2>();
            foreach (var p in points)
            {
                var v = ToVec2(p);
                if (ring.Count > 0 && Vec2.Distance(ring[^1], v) < 1e-9) continue;
                ring.Add(v);
            }

            // A closed ring does not need its first point repeated at the end.
            if (ring.Count > 1 && Vec2.Distance(ring[0], ring[^1]) < 1e-9) ring.RemoveAt(ring.Count - 1);
            return ring;
        }

        private static SPolyline? ConvertPolyline(IEnumerable<Vertex> vertices, bool closed)
        {
            var converted = ToVertices(vertices);
            return converted.Length >= 2 ? new SPolyline(converted, closed) : null;
        }

        private static PolyVertex[] ToVertices(IEnumerable<Vertex> vertices)
        {
            var list = new List<PolyVertex>();
            foreach (var v in vertices) list.Add(new PolyVertex(ToVec2(v.Location), v.Bulge));
            return list.ToArray();
        }

        private static PolyVertex[] ToVertices(IEnumerable<XYZ> points)
        {
            var list = new List<PolyVertex>();
            foreach (var p in points) list.Add(new PolyVertex(ToVec2(p)));
            return list.ToArray();
        }

        /// <summary>
        /// How tall the text actually is.
        /// </summary>
        /// <remarks>
        /// A text style with a non-zero height <em>fixes</em> it: AutoCAD
        /// does not even ask when placing text on one, and whatever number
        /// happens to be sitting in the entity is ignored. Reading the
        /// entity's is right only when the style leaves it open, and getting
        /// this backwards draws real drawings at the wrong size -- in one of
        /// the sample files, thirty-seven of thirty-eight labels.
        /// </remarks>
        private static double HeightOf(double entityHeight, TextStyle? style) =>
            style is { Height: > 0 } fixedHeight ? fixedHeight.Height : entityHeight;

        private SText? ConvertText(TextEntity text)
        {
            double height = HeightOf(text.Height, text.Style);
            if (string.IsNullOrEmpty(text.Value) || height <= 0) return null;

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

            return new SText(new[] { text.Value }, position, height)
            {
                Rotation = text.Rotation,
                WidthFactor = widthFactor,
                ObliqueAngle = text.ObliqueAngle != 0 ? text.ObliqueAngle : styleOblique,
                LineStep = height,
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

            double height = HeightOf(mtext.Height, mtext.Style);
            if (lines.Length == 0 || height <= 0) return null;

            var (family, bold, italic, _, _) = _fonts.Resolve(mtext.Style);
            var (anchorX, anchorY) = AnchorFor(mtext.AttachmentPoint);

            double factor = mtext.LineSpacing > 0 ? mtext.LineSpacing : 1.0;

            return new SText(lines, ToVec2(mtext.InsertPoint), height)
            {
                Rotation = mtext.Rotation,
                LineStep = height * MTextLineSpacingBase * factor,
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
