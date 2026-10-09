using ACadSharp;
using ACadSharp.Entities;
using CSMath;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Styling;
using static FreeDwg.Interop.Acad.CadGeometry;
using AcadCircle = ACadSharp.Entities.Circle;
using AcadLine = ACadSharp.Entities.Line;

namespace FreeDwg.Interop.Acad;

/// <summary>
/// Records what an entity draws as file entities: lines, polylines, solids
/// and text.
/// </summary>
/// <remarks>
/// A DIMENSION in a DWG carries a picture of itself in an anonymous block,
/// and that picture is what AutoCAD shows until something regenerates it.
/// Drawing the picture through the dimension's own <c>Emit</c> means the file
/// shows exactly what the editor showed, with no second layout to disagree
/// with the first. Everything is ByBlock, as a dimension block's contents
/// are, so the dimension's own colour and width apply.
/// </remarks>
internal sealed class BlockPictureSink : IDrawingSink
{
    private readonly List<Entity> _entities = new();
    private readonly Stack<Mat3> _transforms = new();
    private Mat3 _current = Mat3.Identity;

    private readonly List<LwPolyline.Vertex> _figure = new();
    private bool _figureClosed;

    public IReadOnlyList<Entity> Entities => _entities;

    private Vec2 World(Vec2 p) => _current.Transform(p);

    private T ByBlock<T>(T entity) where T : Entity
    {
        entity.Color = Color.ByBlock;
        entity.LineWeight = LineWeightType.ByBlock;
        _entities.Add(entity);
        return entity;
    }

    public void BeginFigure(Vec2 start, bool closed, in DisplayStyle style)
    {
        _figure.Clear();
        _figureClosed = closed;
        _figure.Add(new LwPolyline.Vertex(ToXY(World(start))));
    }

    public void LineTo(Vec2 point) => _figure.Add(new LwPolyline.Vertex(ToXY(World(point))));

    public void ArcTo(Vec2 end, double radius, bool largeArc, bool clockwise)
    {
        if (_figure.Count == 0) return;

        var from = ToVec2(_figure[^1].Location);
        var to = World(end);
        radius *= _current.UniformScale;
        if (_current.IsMirror) clockwise = !clockwise;

        // The bulge is the tangent of a quarter of the sweep, signed
        // counter-clockwise positive, and the sweep follows from the chord.
        double half = Math.Asin(Math.Clamp(Vec2.Distance(from, to) / (2 * radius), 0, 1));
        double sweep = largeArc ? ArcMath.TwoPi - 2 * half : 2 * half;
        _figure[^1].Bulge = (clockwise ? -1 : 1) * Math.Tan(sweep / 4);

        _figure.Add(new LwPolyline.Vertex(ToXY(to)));
    }

    public void EndFigure()
    {
        if (_figure.Count < 2) return;

        if (_figure.Count == 2 && _figure[0].Bulge == 0 && !_figureClosed)
        {
            ByBlock(new AcadLine(ToXYZ(ToVec2(_figure[0].Location)), ToXYZ(ToVec2(_figure[1].Location))));
        }
        else
        {
            var polyline = ByBlock(new LwPolyline { IsClosed = _figureClosed });
            polyline.Vertices.AddRange(_figure);
        }

        _figure.Clear();
    }

    public void Circle(Vec2 center, double radius, in DisplayStyle style) =>
        ByBlock(new AcadCircle { Center = ToXYZ(World(center)), Radius = radius * _current.UniformScale });

    public void PushTransform(in Mat3 transform)
    {
        _transforms.Push(_current);
        _current = transform * _current;
    }

    public void PopTransform() => _current = _transforms.Count > 0 ? _transforms.Pop() : Mat3.Identity;

    public void PushClip(Bounds2 rectangle) { }

    public void PopClip() { }

    public void FillLoops(IReadOnlyList<IReadOnlyList<Vec2>> loops, in DisplayStyle style)
    {
        foreach (var loop in loops)
        {
            if (loop.Count < 3) continue;

            if (loop.Count <= 4)
            {
                // A SOLID's corners go in Z order, not round the outline:
                // the third and fourth are the other way about.
                var p = loop.Select(World).ToArray();
                ByBlock(new Solid
                {
                    FirstCorner = ToXYZ(p[0]),
                    SecondCorner = ToXYZ(p[1]),
                    ThirdCorner = ToXYZ(p.Length == 4 ? p[3] : p[2]),
                    FourthCorner = ToXYZ(p[2]),
                });
                continue;
            }

            var hatch = ByBlock(new Hatch { IsSolid = true, Pattern = ACadSharp.Entities.HatchPattern.Solid });
            var edge = new Hatch.BoundaryPath.Polyline { IsClosed = true };
            foreach (var point in loop) edge.Vertices.Add(ToXYZ(World(point)));
            var path = new Hatch.BoundaryPath();
            path.Edges.Add(edge);
            hatch.Paths.Add(path);
        }
    }

    public void Segments(IReadOnlyList<Segment2> segments, in DisplayStyle style)
    {
        foreach (var segment in segments)
            ByBlock(new AcadLine(ToXYZ(World(segment.A)), ToXYZ(World(segment.B))));
    }

    public void Text(in TextRun run, in DisplayStyle style)
    {
        if (run.Lines.Count == 0) return;

        var at = World(run.Position);
        double rotation = ArcMath.Normalize(run.Rotation + Math.Atan2(_current.M12, _current.M11));
        double height = run.Height * _current.UniformScale;

        ByBlock(TextFactory.Build(run.Lines, at, height, rotation, run.AnchorX, run.AnchorY,
            run.WidthFactor, run.LineStep * _current.UniformScale, run.WrapWidth * _current.UniformScale));
    }
}

/// <summary>
/// TEXT or MTEXT for a set of lines and an anchor, the inverse of how the
/// reader maps the two of them onto the scene's one kind of text.
/// </summary>
internal static class TextFactory
{
    public static Entity Build(IReadOnlyList<string> lines, Vec2 position, double height, double rotation,
        TextAnchorX anchorX, TextAnchorY anchorY, double widthFactor, double lineStep, double wrapWidth)
    {
        // One line with no wrap is TEXT: it is what the reader turns into a
        // one-line scene text, and it is the only kind with a true baseline.
        if (lines.Count == 1 && wrapWidth <= 0)
        {
            var (horizontal, vertical) = TextAlignmentFor(anchorX, anchorY);
            return new TextEntity
            {
                Value = lines[0],
                InsertPoint = ToXYZ(position),
                AlignmentPoint = ToXYZ(position),
                Height = height,
                Rotation = rotation,
                WidthFactor = widthFactor > 0 ? widthFactor : 1.0,
                HorizontalAlignment = horizontal,
                VerticalAlignment = vertical,
            };
        }

        // The reader spaces MTEXT at 5/3 of the height per unit of factor.
        double factor = height > 0 && lineStep > 0 ? lineStep / (height * 5.0 / 3.0) : 1.0;

        return new MText
        {
            Value = MTextValue(lines),
            InsertPoint = ToXYZ(position),
            AlignmentPoint = new XYZ(Math.Cos(rotation), Math.Sin(rotation), 0),
            Height = height,
            AttachmentPoint = AttachmentFor(anchorX, anchorY),
            RectangleWidth = wrapWidth > 0 ? wrapWidth : 0,
            LineSpacing = Math.Clamp(factor, 0.25, 4.0),
        };
    }

    /// <summary>MTEXT with its control characters escaped and its lines as paragraph breaks.</summary>
    public static string MTextValue(IEnumerable<string> lines) =>
        string.Join("\\P", lines.Select(line => line
            .Replace("\\", "\\\\")
            .Replace("{", "\\{")
            .Replace("}", "\\}")));

    public static (TextHorizontalAlignment, TextVerticalAlignmentType) TextAlignmentFor(TextAnchorX x, TextAnchorY y)
    {
        var horizontal = x switch
        {
            TextAnchorX.Center => TextHorizontalAlignment.Center,
            TextAnchorX.Right => TextHorizontalAlignment.Right,
            _ => TextHorizontalAlignment.Left,
        };

        var vertical = y switch
        {
            TextAnchorY.Bottom => TextVerticalAlignmentType.Bottom,
            TextAnchorY.Middle => TextVerticalAlignmentType.Middle,
            TextAnchorY.Top => TextVerticalAlignmentType.Top,
            _ => TextVerticalAlignmentType.Baseline,
        };

        return (horizontal, vertical);
    }

    public static AttachmentPointType AttachmentFor(TextAnchorX x, TextAnchorY y) => (x, y) switch
    {
        (TextAnchorX.Center, TextAnchorY.Top) => AttachmentPointType.TopCenter,
        (TextAnchorX.Right, TextAnchorY.Top) => AttachmentPointType.TopRight,
        (TextAnchorX.Left, TextAnchorY.Middle) => AttachmentPointType.MiddleLeft,
        (TextAnchorX.Center, TextAnchorY.Middle) => AttachmentPointType.MiddleCenter,
        (TextAnchorX.Right, TextAnchorY.Middle) => AttachmentPointType.MiddleRight,
        (TextAnchorX.Left, TextAnchorY.Bottom or TextAnchorY.Baseline) => AttachmentPointType.BottomLeft,
        (TextAnchorX.Center, TextAnchorY.Bottom or TextAnchorY.Baseline) => AttachmentPointType.BottomCenter,
        (TextAnchorX.Right, TextAnchorY.Bottom or TextAnchorY.Baseline) => AttachmentPointType.BottomRight,
        _ => AttachmentPointType.TopLeft,
    };
}
