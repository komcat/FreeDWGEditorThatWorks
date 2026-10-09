using System.IO;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using ACadSharp.XData;
using CSMath;
using FreeDwg.Core.Commands;
using FreeDwg.Core.Editing;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Styling;
using FreeDwg.Interop.Acad;
using AcadArc = ACadSharp.Entities.Arc;
using AcadCircle = ACadSharp.Entities.Circle;
using AcadLayer = ACadSharp.Tables.Layer;
using AcadLine = ACadSharp.Entities.Line;
using SceneDrawing = FreeDwg.Core.Scene.Drawing;
using SceneLayer = FreeDwg.Core.Scene.Layer;

namespace FreeDwg.Tests;

/// <summary>
/// Saving: the scene's edits written onto the document that was read, then
/// read back through the real reader.
/// </summary>
/// <remarks>
/// Every case here reads the saved file back rather than inspecting the
/// document in memory. The writer and the reader have to agree, and the only
/// way to know they do is a round trip -- an arc written with its sweep the
/// wrong way round is a correct-looking object in memory and an inside-out
/// arc on screen.
/// <para>
/// The property the whole design exists for gets its own test: what the
/// scene never read, it must never lose.
/// </para>
/// </remarks>
public sealed class SaveTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "FreeDwg.Tests", Environment.ProcessId.ToString(), "save", Guid.NewGuid().ToString("N"));

    public SaveTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private string PathFor(string name) => Path.Combine(_folder, name);

    // ---- fixtures ----------------------------------------------------------

    /// <summary>
    /// A file with things the scene models and things it does not: xdata on
    /// a line, a POINT the reader skips, a hatch with a pattern and an arc in
    /// its boundary, a block with an attribute, and a dimension.
    /// </summary>
    private string WriteFixture(string name = "fixture.dwg")
    {
        var doc = new CadDocument();

        var walls = new AcadLayer("WALLS") { Color = new Color(1) };
        var notes = new AcadLayer("NOTES") { Color = new Color(3) };
        doc.Layers.Add(walls);
        doc.Layers.Add(notes);

        var tagged = new AcadLine(new XYZ(0, 0, 0), new XYZ(100, 0, 0)) { Layer = walls };
        doc.Entities.Add(tagged);
        var app = new AppId("FREEDWG_TEST");
        doc.AppIds.Add(app);
        tagged.ExtendedData.Add(app, new ExtendedDataRecord[] { new ExtendedDataString("keep me") });

        doc.Entities.Add(new AcadLine(new XYZ(0, 50, 0), new XYZ(100, 50, 0)) { Layer = walls });
        doc.Entities.Add(new AcadCircle { Center = new XYZ(200, 0, 0), Radius = 20, Layer = notes });
        doc.Entities.Add(new Point(new XYZ(7, 7, 0)) { Layer = notes });

        // A pattern hatch bounded by a line and a half circle.
        var hatch = new Hatch { Layer = notes, PatternScale = 1 };
        var pattern = new HatchPattern("STRIPES");
        pattern.Lines.Add(new HatchPattern.Line { Angle = Math.PI / 4, BasePoint = new XY(0, 0), Offset = new XY(-2, 2) });
        hatch.Pattern = pattern;
        var path = new Hatch.BoundaryPath();
        path.Edges.Add(new Hatch.BoundaryPath.Line { Start = new XY(300, 0), End = new XY(340, 0) });
        path.Edges.Add(new Hatch.BoundaryPath.Arc
        {
            Center = new XY(320, 0), Radius = 20, StartAngle = 0, EndAngle = Math.PI, CounterClockWise = true,
        });
        hatch.Paths.Add(path);
        doc.Entities.Add(hatch);

        // A block with an attribute, inserted turned and scaled.
        var block = new BlockRecord("TAG");
        block.Entities.Add(new AcadLine(new XYZ(0, 0, 0), new XYZ(10, 0, 0)));
        block.Entities.Add(new AttributeDefinition
        {
            Tag = "ID", Value = "A1", InsertPoint = new XYZ(0, 2, 0), Height = 2,
        });
        doc.BlockRecords.Add(block);
        var insert = new Insert(doc.BlockRecords["TAG"])
        {
            InsertPoint = new XYZ(0, 200, 0), XScale = 2, YScale = 2, Rotation = Math.PI / 6, Layer = walls,
        };
        doc.Entities.Add(insert);

        // A dimension carrying its own picture block, as AutoCAD writes one.
        var dimension = new DimensionAligned(new XYZ(0, 100, 0), new XYZ(60, 100, 0))
        {
            DefinitionPoint = new XYZ(60, 110, 0),
        };
        doc.Entities.Add(dimension);
        dimension.UpdateBlock();

        doc.Header.Version = ACadVersion.AC1018;
        string file = PathFor(name);
        DwgWriter.Write(file, doc);
        return file;
    }

    private static CommandStack Stack(DwgSession session) => new(session.Drawing);

    private static T Single<T>(SceneDrawing drawing, Func<T, bool>? where = null) where T : SceneEntity =>
        Assert.Single(drawing.ModelSpace.Entities.OfType<T>().Where(e => where?.Invoke(e) ?? true));

    private static void AssertNear(Vec2 expected, Vec2 actual, double tolerance = 1e-6)
    {
        Assert.True(Vec2.Distance(expected, actual) <= tolerance, $"expected {expected}, got {actual}");
    }

    private static void AssertNear(double expected, double actual, double tolerance = 1e-6) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"expected {expected}, got {actual}");

    private static void AssertSameBounds(Bounds2 expected, Bounds2 actual, double tolerance = 1e-6)
    {
        AssertNear(expected.Min, actual.Min, tolerance);
        AssertNear(expected.Max, actual.Max, tolerance);
    }

    // ---- a new drawing -----------------------------------------------------

    [Fact]
    public void ANewDrawingStartsLikeTheBlankOneItReplaced()
    {
        var session = DwgSession.CreateNew();
        var drawing = session.Drawing;

        Assert.Null(session.Path);
        Assert.Equal(DrawingUnits.Millimetres, drawing.Units);
        Assert.Same(drawing.ModelSpace, drawing.ActiveLayout);
        Assert.Empty(drawing.ModelSpace.Entities);

        var zero = Assert.Single(drawing.Layers, layer => layer.Name == "0");
        Assert.Equal(Rgb.White, zero.Color);
        Assert.NotEqual(0ul, zero.SourceHandle);
        Assert.Same(zero, drawing.CurrentLayer);
    }

    [Fact]
    public void ANewDrawingSavesEveryKindItCanDraw()
    {
        var session = DwgSession.CreateNew();
        var drawing = session.Drawing;
        var stack = Stack(session);

        var line = drawing.Place(new SLine(new Vec2(0, 0), new Vec2(30, 40)));
        var circle = drawing.Place(new SCircle(new Vec2(100, 0), 12));
        var arc = drawing.Place(new SArc(new Vec2(0, 100), 10, 0.3, 2.0));

        // Clockwise, as a mirror leaves one: DWG cannot say that directly.
        var clockwise = drawing.Place(new SArc(new Vec2(50, 100), 10, 1.0, -2.5));

        var polyline = drawing.Place(new SPolyline(
            [new PolyVertex(new Vec2(0, 200), 0.5), new PolyVertex(new Vec2(40, 200), -0.3), new PolyVertex(new Vec2(40, 240))],
            closed: true));
        var ellipse = drawing.Place(new SEllipse(new Vec2(200, 200), new Vec2(30, 10), 0.4, 0, ArcMath.TwoPi));
        var text = drawing.Place(new SText(["HELLO"], new Vec2(300, 0), 5) { Rotation = 0.5 });

        stack.Do(new AddEntities(drawing.ActiveLayout, line, circle, arc, clockwise, polyline, ellipse, text));

        string file = PathFor("new.dwg");
        var report = session.Save(stack, file);

        Assert.Equal(7, report.Created);
        Assert.Empty(report.Notes);
        Assert.False(stack.IsModified);
        Assert.Equal(file, session.Path);

        var back = DwgLoader.Load(file);

        var l = Single<SLine>(back);
        AssertNear(line.Start, l.Start);
        AssertNear(line.End, l.End);

        AssertNear(12, Single<SCircle>(back).Radius);

        var arcs = back.ModelSpace.Entities.OfType<SArc>().ToList();
        Assert.Equal(2, arcs.Count);

        // Same sweep, same ends: compare the three points that pin an arc
        // down, since the start angle and sign legitimately differ.
        foreach (var expected in new[] { arc, clockwise })
        {
            var actual = arcs.Single(a => Vec2.Distance(a.Center, expected.Center) < 1e-9);
            AssertNear(Math.Abs(expected.Sweep), Math.Abs(actual.Sweep));
            AssertNear(expected.MidPoint, actual.MidPoint);
            AssertSameBounds(expected.Bounds, actual.Bounds);
        }

        var p = Single<SPolyline>(back);
        Assert.True(p.Closed);
        Assert.Equal(polyline.Vertices.Select(v => v.Bulge), p.Vertices.Select(v => v.Bulge));

        var e = Single<SEllipse>(back);
        AssertNear(ellipse.MajorAxis, e.MajorAxis);
        AssertNear(0.4, e.Ratio);

        var t = Single<SText>(back);
        Assert.Equal(["HELLO"], t.Lines);
        AssertNear(text.Position, t.Position);
        AssertNear(0.5, t.Rotation);
    }

    [Fact]
    public void ColoursFollowTheLayerUnlessTheyWereGivenOne()
    {
        var session = DwgSession.CreateNew();
        var drawing = session.Drawing;
        var stack = Stack(session);

        var layer = new SceneLayer("PIPES") { Color = new Rgb(255, 0, 0) };
        var add = new AddLayer(layer);
        stack.Do(add);
        drawing.CurrentLayerIndex = add.Index;

        var follows = drawing.Place(new SLine(new Vec2(0, 0), new Vec2(10, 0)));
        var own = drawing.Place(new SLine(new Vec2(0, 5), new Vec2(10, 5)));
        own.Style = own.Style with { Color = new Rgb(12, 34, 56) };
        stack.Do(new AddEntities(drawing.ActiveLayout, follows, own));

        string file = PathFor("colours.dwg");
        session.Save(stack, file);

        var doc = DwgReader.Read(file);
        var lines = doc.Entities.OfType<AcadLine>().OrderBy(l => l.StartPoint.Y).ToList();

        Assert.Equal("PIPES", lines[0].Layer.Name);
        Assert.True(lines[0].Color.IsByLayer);
        Assert.Equal(1, doc.Layers["PIPES"].Color.Index);

        Assert.True(lines[1].Color.IsTrueColor);
        Assert.Equal(new byte[] { 12, 34, 56 }, lines[1].Color.GetRgb().ToArray());
    }

    [Fact]
    public void ADrawnDimensionIsARealDimensionWithAPictureOfItself()
    {
        var session = DwgSession.CreateNew();
        var drawing = session.Drawing;
        var stack = Stack(session);

        var dimension = drawing.Place(new SDimension(DimensionKind.Aligned, new Vec2(0, 0), new Vec2(80, 0), new Vec2(40, 15)));
        stack.Do(new AddEntities(drawing.ActiveLayout, dimension));

        string file = PathFor("dimension.dwg");
        session.Save(stack, file);

        var doc = DwgReader.Read(file);
        var written = Assert.Single(doc.Entities.OfType<DimensionAligned>());
        Assert.Equal(new XYZ(0, 0, 0), written.FirstPoint);
        Assert.Equal(new XYZ(80, 0, 0), written.SecondPoint);
        Assert.NotNull(written.Block);
        Assert.NotEmpty(written.Block.Entities);

        // Read back, it is the picture: it should cover what was drawn.
        var back = DwgLoader.Load(file);
        var picture = Single<SInsert>(back);
        AssertSameBounds(dimension.Bounds, picture.Bounds, tolerance: 0.5);
    }

    // ---- editing a file ----------------------------------------------------

    [Fact]
    public void WhatTheSceneNeverReadSurvivesAnEdit()
    {
        string file = WriteFixture();
        var session = DwgSession.Open(file);
        var drawing = session.Drawing;
        var stack = Stack(session);

        // Move the line that carries xdata.
        var tagged = Single<SLine>(drawing, l => l.Start.Y == 0);
        stack.Do(new TransformEntities(drawing.ActiveLayout, [tagged], Mat3.Translation(5, 5), "Move"));
        session.Save(stack);

        var doc = DwgReader.Read(file);
        var line = doc.Entities.OfType<AcadLine>().Single(l => l.StartPoint.Y == 5);
        Assert.Equal(new XYZ(105, 5, 0), line.EndPoint);

        Assert.True(line.ExtendedData.TryGet("FREEDWG_TEST", out var xdata));
        Assert.Equal("keep me", ((ExtendedDataString)Assert.Single(xdata.Records)).Value);

        // Never read at all, so never touched.
        var point = Assert.Single(doc.Entities.OfType<Point>());
        Assert.Equal(new XYZ(7, 7, 0), point.Location);

        Assert.True(File.Exists(Path.ChangeExtension(file, ".bak")));
    }

    [Fact]
    public void DeletedObjectsLeaveTheFileAndTheRestStay()
    {
        string file = WriteFixture();
        var session = DwgSession.Open(file);
        var drawing = session.Drawing;
        var stack = Stack(session);
        int before = drawing.ModelSpace.Entities.Count;

        stack.Do(new DeleteEntities(drawing.ActiveLayout, [Single<SCircle>(drawing)]));
        var report = session.Save(stack);
        Assert.Equal(1, report.Deleted);

        var back = DwgLoader.Load(file);
        Assert.Empty(back.ModelSpace.Entities.OfType<SCircle>());
        Assert.Equal(before - 1, back.ModelSpace.Entities.Count);
    }

    [Fact]
    public void UndoAfterASaveIsWrittenByTheNextOne()
    {
        string file = WriteFixture();
        var session = DwgSession.Open(file);
        var drawing = session.Drawing;
        var stack = Stack(session);

        // Erase, save, undo, save: the circle is back.
        stack.Do(new DeleteEntities(drawing.ActiveLayout, [Single<SCircle>(drawing)]));
        session.Save(stack);
        Assert.Empty(DwgLoader.Load(file).ModelSpace.Entities.OfType<SCircle>());

        stack.Undo();
        session.Save(stack);
        Assert.Equal(20, Single<SCircle>(DwgLoader.Load(file)).Radius);

        // Draw, save, undo, save: the line is gone again.
        var line = drawing.Place(new SLine(new Vec2(-50, -50), new Vec2(-40, -40)));
        stack.Do(new AddEntities(drawing.ActiveLayout, line));
        session.Save(stack);
        Assert.Single(DwgLoader.Load(file).ModelSpace.Entities.OfType<SLine>(), l => l.Start.X == -50);

        stack.Undo();
        session.Save(stack);
        Assert.DoesNotContain(DwgLoader.Load(file).ModelSpace.Entities.OfType<SLine>(), l => l.Start.X == -50);

        // Move, save, undo, save: back where it started.
        var circle = Single<SCircle>(drawing);
        stack.Do(new TransformEntities(drawing.ActiveLayout, [circle], Mat3.Translation(0, 30), "Move"));
        session.Save(stack);
        stack.Undo();
        session.Save(stack);
        AssertNear(new Vec2(200, 0), Single<SCircle>(DwgLoader.Load(file)).Center);
    }

    [Fact]
    public void SavingTwiceDoesNotWriteTwice()
    {
        var session = DwgSession.CreateNew();
        var drawing = session.Drawing;
        var stack = Stack(session);
        stack.Do(new AddEntities(drawing.ActiveLayout, drawing.Place(new SLine(new Vec2(0, 0), new Vec2(1, 1)))));

        string file = PathFor("twice.dwg");
        Assert.Equal(1, session.Save(stack, file).Created);

        var again = session.Save(stack);
        Assert.Equal(0, again.Created + again.Modified + again.Deleted);
        Assert.Single(DwgLoader.Load(file).ModelSpace.Entities.OfType<SLine>());
    }

    [Fact]
    public void TrimmingACircleWritesAnArc()
    {
        string file = WriteFixture();
        var session = DwgSession.Open(file);
        var drawing = session.Drawing;
        var stack = Stack(session);

        var circle = Single<SCircle>(drawing);
        var arc = new SArc(circle.Center, circle.Radius, 0, Math.PI) { LayerIndex = circle.LayerIndex, Style = circle.Style };
        stack.Do(new ReplaceEntities(drawing.ActiveLayout, EditPlan.Replace(circle, arc), "Trim"));
        session.Save(stack);

        var back = DwgLoader.Load(file);
        Assert.Empty(back.ModelSpace.Entities.OfType<SCircle>());
        var written = Single<SArc>(back);
        AssertNear(new Vec2(200, 20), written.MidPoint);
        Assert.Equal("NOTES", back.Layers[written.LayerIndex].Name);
    }

    [Fact]
    public void AMirroredPolylineAndArcComeBackTheRightWayOut()
    {
        string file = WriteFixture();
        var session = DwgSession.Open(file);
        var drawing = session.Drawing;
        var stack = Stack(session);

        var arc = drawing.Place(new SArc(new Vec2(0, -100), 10, 0, Math.PI / 2));
        var polyline = drawing.Place(new SPolyline(
            [new PolyVertex(new Vec2(20, -100), 1), new PolyVertex(new Vec2(40, -100))], closed: false));
        stack.Do(new AddEntities(drawing.ActiveLayout, arc, polyline));
        session.Save(stack);

        stack.Do(new TransformEntities(drawing.ActiveLayout, [arc, polyline],
            Mat3.Reflection(new Vec2(0, -100), new Vec2(0, 1)), "Mirror"));
        session.Save(stack);

        var back = DwgLoader.Load(file);
        var a = Single<SArc>(back);
        AssertNear(arc.MidPoint, a.MidPoint);

        var p = Single<SPolyline>(back);
        AssertSameBounds(polyline.Bounds, p.Bounds);
    }

    [Fact]
    public void AMovedHatchTakesItsPatternAlong()
    {
        string file = WriteFixture();
        var session = DwgSession.Open(file);
        var drawing = session.Drawing;
        var stack = Stack(session);

        var hatch = Single<SHatch>(drawing);
        int strokes = hatch.PatternSegments.Count;
        Assert.True(strokes > 0);

        var move = Mat3.RotationAbout(Math.PI / 2, new Vec2(320, 0)) * Mat3.Translation(10, 20);
        stack.Do(new TransformEntities(drawing.ActiveLayout, [hatch], move, "Rotate"));
        session.Save(stack);

        var back = Single<SHatch>(DwgLoader.Load(file));
        AssertSameBounds(hatch.Bounds, back.Bounds, tolerance: 1e-3);
        Assert.InRange(back.PatternSegments.Count, strokes - 2, strokes + 2);

        // And a second save does not move it again.
        session.Save(stack);
        AssertSameBounds(hatch.Bounds, Single<SHatch>(DwgLoader.Load(file)).Bounds, tolerance: 1e-3);
    }

    [Fact]
    public void AMirroredHatchIsStillFilledWhereItWasDrawn()
    {
        string file = WriteFixture();
        var session = DwgSession.Open(file);
        var drawing = session.Drawing;
        var stack = Stack(session);

        var hatch = Single<SHatch>(drawing);
        stack.Do(new TransformEntities(drawing.ActiveLayout, [hatch],
            Mat3.Reflection(new Vec2(0, -10), new Vec2(1, 0)), "Mirror"));
        session.Save(stack);

        var back = Single<SHatch>(DwgLoader.Load(file));
        AssertSameBounds(hatch.Bounds, back.Bounds, tolerance: 1e-3);
    }

    [Fact]
    public void ACopiedHatchIsWrittenFromTheOriginal()
    {
        string file = WriteFixture();
        var session = DwgSession.Open(file);
        var drawing = session.Drawing;
        var stack = Stack(session);

        var hatch = Single<SHatch>(drawing);
        var copy = new CopyEntities(drawing.ActiveLayout, [hatch], Mat3.Translation(0, -100), "Copy");
        stack.Do(copy);
        session.Save(stack);

        var hatches = DwgLoader.Load(file).ModelSpace.Entities.OfType<SHatch>().ToList();
        Assert.Equal(2, hatches.Count);
        Assert.Contains(hatches, h => Math.Abs(h.Bounds.Min.Y - copy.Copies[0].Bounds.Min.Y) < 1e-3);
        Assert.All(hatches, h => Assert.NotEmpty(h.PatternSegments));
    }

    [Fact]
    public void AMovedInsertTakesItsAttributeAlong()
    {
        string file = WriteFixture();
        var session = DwgSession.Open(file);
        var drawing = session.Drawing;
        var stack = Stack(session);

        var was = DwgReader.Read(file).Entities.OfType<Insert>().Single(i => i.Block.Name == "TAG");
        var attributeWas = Assert.Single(was.Attributes).InsertPoint;

        var insert = Single<SInsert>(drawing, i => i.Block.Name == "TAG");
        var turn = Mat3.RotationAbout(Math.PI / 3, Vec2.Zero);
        stack.Do(new TransformEntities(drawing.ActiveLayout, [insert], turn, "Rotate"));
        session.Save(stack);

        var back = Single<SInsert>(DwgLoader.Load(file), i => i.Block.Name == "TAG");
        Assert.True(CadMatrixNear(insert.Placement, back.Placement));

        var doc = DwgReader.Read(file);
        var written = doc.Entities.OfType<Insert>().Single(i => i.Block.Name == "TAG");
        var attribute = Assert.Single(written.Attributes);
        Assert.Equal("A1", attribute.Value);

        // Attributes are separate objects in world space: it has to have
        // been turned with the insert, or it is left behind where it was.
        var expected = turn.Transform(new Vec2(attributeWas.X, attributeWas.Y));
        AssertNear(expected, new Vec2(attribute.InsertPoint.X, attribute.InsertPoint.Y), 1e-6);
    }

    private static bool CadMatrixNear(Mat3 a, Mat3 b) =>
        Math.Abs(a.M11 - b.M11) < 1e-9 && Math.Abs(a.M12 - b.M12) < 1e-9 &&
        Math.Abs(a.M21 - b.M21) < 1e-9 && Math.Abs(a.M22 - b.M22) < 1e-9 &&
        Math.Abs(a.OffsetX - b.OffsetX) < 1e-6 && Math.Abs(a.OffsetY - b.OffsetY) < 1e-6;

    [Fact]
    public void AMovedImportedDimensionTakesItsPictureAlong()
    {
        string file = WriteFixture();
        var session = DwgSession.Open(file);
        var drawing = session.Drawing;
        var stack = Stack(session);

        var dimension = Single<SInsert>(drawing, i => i.Block.IsAnonymous);
        stack.Do(new TransformEntities(drawing.ActiveLayout, [dimension], Mat3.Translation(15, -40), "Move"));
        session.Save(stack);
        session.Save(stack);

        var back = Single<SInsert>(DwgLoader.Load(file), i => i.Block.IsAnonymous);
        AssertSameBounds(dimension.Bounds, back.Bounds);

        var written = DwgReader.Read(file).Entities.OfType<DimensionAligned>().Single();
        Assert.Equal(new XYZ(15, 60, 0), written.FirstPoint);
    }

    [Fact]
    public void MovedTextKeepsItsAlignment()
    {
        var session = DwgSession.CreateNew();
        var drawing = session.Drawing;
        var stack = Stack(session);

        var text = drawing.Place(new SText(["MID"], new Vec2(10, 10), 4) { AnchorX = TextAnchorX.Center, AnchorY = TextAnchorY.Middle });
        stack.Do(new AddEntities(drawing.ActiveLayout, text));
        string file = PathFor("text.dwg");
        session.Save(stack, file);

        stack.Do(new TransformEntities(drawing.ActiveLayout, [text], Mat3.RotationAbout(Math.PI / 2, Vec2.Zero) * Mat3.Scaling(2), "Turn"));
        session.Save(stack);

        var back = Single<SText>(DwgLoader.Load(file));
        AssertNear(new Vec2(-20, 20), back.Position);
        AssertNear(8, back.Height);
        AssertNear(Math.PI / 2, back.Rotation);
        Assert.Equal(TextAnchorX.Center, back.AnchorX);
        Assert.Equal(TextAnchorY.Middle, back.AnchorY);
    }

    // ---- layers ------------------------------------------------------------

    [Fact]
    public void LayerEditsAreWritten()
    {
        string file = WriteFixture();
        var session = DwgSession.Open(file);
        var drawing = session.Drawing;
        var stack = Stack(session);

        int walls = LayerTable.IndexOf(drawing, "WALLS");
        var renamed = LayerProperties.Of(drawing.Layers[walls]) with { Name = "PARTITIONS", Color = new Rgb(0, 0, 255) };
        stack.Do(new ChangeLayer(drawing, walls, renamed));

        var spare = new AddLayer(new SceneLayer("SPARE") { Color = new Rgb(255, 255, 0) });
        stack.Do(spare);

        session.Save(stack);

        var doc = DwgReader.Read(file);
        Assert.False(doc.Layers.Contains("WALLS"));
        Assert.Equal(5, doc.Layers["PARTITIONS"].Color.Index);
        Assert.Equal(2, doc.Layers["SPARE"].Color.Index);

        // The lines were following the layer, and still are.
        Assert.All(doc.Entities.OfType<AcadLine>(), line =>
        {
            Assert.Equal("PARTITIONS", line.Layer.Name);
            Assert.True(line.Color.IsByLayer);
        });

        // Deleting it again, after the save made it.
        stack.Do(new DeleteLayer(drawing, LayerTable.IndexOf(drawing, "SPARE")));
        session.Save(stack);
        Assert.False(DwgReader.Read(file).Layers.Contains("SPARE"));
    }

    [Fact]
    public void SwappingTwoLayerNamesDoesNotCollide()
    {
        string file = WriteFixture();
        var session = DwgSession.Open(file);
        var drawing = session.Drawing;
        var stack = Stack(session);

        int walls = LayerTable.IndexOf(drawing, "WALLS");
        int notes = LayerTable.IndexOf(drawing, "NOTES");
        stack.Do(new ChangeLayer(drawing, walls, LayerProperties.Of(drawing.Layers[walls]) with { Name = "TEMP" }));
        stack.Do(new ChangeLayer(drawing, notes, LayerProperties.Of(drawing.Layers[notes]) with { Name = "WALLS" }));
        stack.Do(new ChangeLayer(drawing, walls, LayerProperties.Of(drawing.Layers[walls]) with { Name = "NOTES" }));
        session.Save(stack);

        var doc = DwgReader.Read(file);
        Assert.Equal(1, doc.Layers["NOTES"].Color.Index);
        Assert.Equal(3, doc.Layers["WALLS"].Color.Index);
        Assert.Equal("NOTES", doc.Entities.OfType<AcadLine>().First().Layer.Name);
    }

    // ---- formats -----------------------------------------------------------

    [Fact]
    public void SaveAsDxfWritesDxf()
    {
        string file = WriteFixture();
        var session = DwgSession.Open(file);
        var stack = Stack(session);
        stack.Do(new DeleteEntities(session.Drawing.ActiveLayout, [Single<SCircle>(session.Drawing)]));

        string dxf = PathFor("copy.dxf");
        session.Save(stack, dxf);

        Assert.StartsWith("  0", File.ReadAllText(dxf)[..3]);
        var back = DwgLoader.Load(dxf);
        Assert.Empty(back.ModelSpace.Entities.OfType<SCircle>());
        Assert.NotEmpty(back.ModelSpace.Entities.OfType<SHatch>());
        Assert.Equal(dxf, session.Path);
    }

    [Fact]
    public void AFailedWriteLeavesTheOldFileWhole()
    {
        string file = WriteFixture();
        byte[] original = File.ReadAllBytes(file);

        var session = DwgSession.Open(file);
        var stack = Stack(session);
        stack.Do(new DeleteEntities(session.Drawing.ActiveLayout, [Single<SCircle>(session.Drawing)]));

        // Hold the temporary file open so the write cannot create it.
        using (File.Create(file + ".saving"))
            Assert.ThrowsAny<IOException>(() => session.Save(stack));

        Assert.Equal(original, File.ReadAllBytes(file));
        Assert.True(stack.IsModified);
    }
}
