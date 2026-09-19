using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Snapping;
using SceneDrawing = FreeDwg.Core.Scene.Drawing;

namespace FreeDwg.Tests;

/// <summary>
/// Object snap, ortho and the grid.
/// </summary>
/// <remarks>
/// Snapping is what separates drafting from drawing: without it two lines
/// that look joined are joined to within a pixel, which is to say not joined
/// at all, and everything downstream -- trim, fillet, area, export -- is
/// then working on a drawing full of near misses. So the assertions here are
/// exact equality on the snapped point, not nearness.
/// </remarks>
public sealed class SnapTests
{
    private const double Tolerance = 2.0;

    private static SceneDrawing WithEntities(params SceneEntity[] entities)
    {
        var drawing = SceneDrawing.CreateEmpty();
        foreach (var entity in entities) drawing.Add(entity);
        return drawing;
    }

    private static SnapResult Snap(SceneDrawing drawing, SnapEngine engine, Vec2 cursor, Vec2? from = null) =>
        engine.Resolve(drawing.ActiveLayout, drawing.Layers, cursor, Tolerance, from);

    // ---- what each entity offers ----------------------------------------

    [Fact]
    public void ALineOffersItsEndsAndItsMiddle()
    {
        var points = new List<SnapCandidate>();
        new SLine(new Vec2(0, 0), new Vec2(10, 20)).CollectSnapPoints(SnapModes.All, points);

        Assert.Contains(points, p => p.Kind == SnapKind.Endpoint && p.Point == new Vec2(0, 0));
        Assert.Contains(points, p => p.Kind == SnapKind.Endpoint && p.Point == new Vec2(10, 20));
        Assert.Contains(points, p => p.Kind == SnapKind.Midpoint && p.Point == new Vec2(5, 10));
    }

    [Fact]
    public void ACircleOffersItsCentreAndFourQuadrants()
    {
        var points = new List<SnapCandidate>();
        new SCircle(new Vec2(10, 10), 5).CollectSnapPoints(SnapModes.All, points);

        Assert.Contains(points, p => p.Kind == SnapKind.Center && p.Point == new Vec2(10, 10));

        var quadrants = points.Where(p => p.Kind == SnapKind.Quadrant).Select(p => p.Point).ToList();
        Assert.Equal(4, quadrants.Count);
        Assert.Contains(new Vec2(15, 10), quadrants);
        Assert.Contains(new Vec2(10, 15), quadrants);
        Assert.Contains(new Vec2(5, 10), quadrants);
        Assert.Contains(new Vec2(10, 5), quadrants);
    }

    [Fact]
    public void AnArcOnlyOffersTheQuadrantsItActuallyReaches()
    {
        // A quarter arc from +X to +Y passes 90 degrees and nothing else.
        var points = new List<SnapCandidate>();
        new SArc(Vec2.Zero, 10, 0, Math.PI / 2).CollectSnapPoints(SnapModes.Quadrant, points);

        var quadrants = points.Select(p => p.Point).ToList();
        Assert.Equal(2, quadrants.Count);
        Assert.Contains(quadrants, p => Math.Abs(p.X - 10) < 1e-9 && Math.Abs(p.Y) < 1e-9);
        Assert.Contains(quadrants, p => Math.Abs(p.X) < 1e-9 && Math.Abs(p.Y - 10) < 1e-9);
    }

    [Fact]
    public void ABulgedPolylineSegmentOffersTheMidpointOfItsArcNotItsChord()
    {
        // Bulge 1 from (0,0) to (10,0): a semicircle dipping to (5,-5).
        var polyline = new SPolyline(
            [new PolyVertex(new Vec2(0, 0), 1), new PolyVertex(new Vec2(10, 0))], closed: false);

        var points = new List<SnapCandidate>();
        polyline.CollectSnapPoints(SnapModes.Midpoint, points);

        var midpoint = Assert.Single(points);
        Assert.Equal(5, midpoint.Point.X, 9);
        Assert.Equal(-5, midpoint.Point.Y, 9);
    }

    [Fact]
    public void ABlockInstanceOffersItsContentsWhereTheyAreDrawn()
    {
        var block = new BlockDefinition("RING");
        block.Entities.Add(new SCircle(Vec2.Zero, 5));

        var insert = new SInsert(block,
            SInsert.BuildTransform(Vec2.Zero, new Vec2(100, 50), 2, 2, rotation: 0));

        var points = new List<SnapCandidate>();
        insert.CollectSnapPoints(SnapModes.All, points);

        // Symbols are the thing most worth snapping to in a real drawing.
        Assert.Contains(points, p => p.Kind == SnapKind.Center && p.Point == new Vec2(100, 50));
        Assert.Contains(points, p => p.Kind == SnapKind.Quadrant && p.Point == new Vec2(110, 50));
    }

    // ---- the engine -------------------------------------------------------

    [Fact]
    public void TheCursorJumpsToTheNearestPointItIsAllowedTo()
    {
        var drawing = WithEntities(new SLine(new Vec2(0, 0), new Vec2(100, 0)));
        var engine = new SnapEngine { Modes = SnapModes.Endpoint };

        var result = Snap(drawing, engine, new Vec2(1.2, 0.7));

        Assert.Equal(SnapKind.Endpoint, result.Kind);
        Assert.Equal(new Vec2(0, 0), result.Point);
    }

    [Fact]
    public void NothingWithinReachLeavesTheCursorAlone()
    {
        var drawing = WithEntities(new SLine(new Vec2(0, 0), new Vec2(100, 0)));
        var engine = new SnapEngine { Modes = SnapModes.Endpoint };

        var cursor = new Vec2(50, 40);
        var result = Snap(drawing, engine, cursor);

        Assert.False(result.Found);
        Assert.Equal(cursor, result.Point);
    }

    [Fact]
    public void ASwitchedOffSnapIsNotOffered()
    {
        var drawing = WithEntities(new SLine(new Vec2(0, 0), new Vec2(100, 0)));

        // Sitting right on the midpoint, with midpoint snap off.
        var engine = new SnapEngine { Modes = SnapModes.Endpoint };
        Assert.False(Snap(drawing, engine, new Vec2(50, 0)).Found);

        engine.Modes = SnapModes.Midpoint;
        Assert.Equal(SnapKind.Midpoint, Snap(drawing, engine, new Vec2(50, 0)).Kind);
    }

    [Fact]
    public void GeometryOnALockedOrHiddenLayerIsNotSnappedTo()
    {
        var drawing = SceneDrawing.CreateEmpty();
        drawing.AddLayer(new Layer("LOCKED") { IsLocked = true });
        drawing.Add(new SLine(new Vec2(0, 0), new Vec2(100, 0)) { LayerIndex = 1 });

        var engine = new SnapEngine { Modes = SnapModes.Endpoint };

        Assert.False(Snap(drawing, engine, new Vec2(0.5, 0.5)).Found);
    }

    [Fact]
    public void AnObjectSnapBeatsOrthoOutright()
    {
        var drawing = WithEntities(new SLine(new Vec2(50, 40), new Vec2(60, 40)));
        var engine = new SnapEngine { Modes = SnapModes.Endpoint, Ortho = true };

        // Ortho would square this up with the previous point and drag it off
        // the endpoint the user is plainly aiming at. Aiming wins.
        var result = Snap(drawing, engine, new Vec2(50.4, 39.6), from: new Vec2(0, 0));

        Assert.Equal(SnapKind.Endpoint, result.Kind);
        Assert.Equal(new Vec2(50, 40), result.Point);
    }

    // ---- ortho ------------------------------------------------------------

    [Theory]
    [InlineData(100.0, 4.0, 100.0, 0.0, "mostly horizontal, so flattened")]
    [InlineData(4.0, 100.0, 0.0, 100.0, "mostly vertical, so straightened")]
    [InlineData(50.0, 50.0, 50.0, 0.0, "a perfect diagonal goes horizontal")]
    public void OrthoDropsAPointOntoTheAxisItIsClosestTo(
        double x, double y, double expectedX, double expectedY, string what)
    {
        var result = Ortho.Constrain(Vec2.Zero, new Vec2(x, y));

        Assert.Equal(expectedX, result.X, 9);
        Assert.Equal(expectedY, result.Y, 9);
    }

    [Fact]
    public void OrthoIsMeasuredFromThePreviousPointNotTheOrigin()
    {
        var result = Ortho.Constrain(new Vec2(100, 100), new Vec2(103, 160));

        Assert.Equal(new Vec2(100, 160), result);
    }

    // ---- the grid ---------------------------------------------------------

    [Theory]
    // The floor is 8 pixels between lines, so the spacing is whichever decade
    // first clears it: at 0.1 px/unit a 10-unit grid would be 1 pixel apart.
    [InlineData(1.0, 10.0)]
    [InlineData(0.1, 100.0)]
    [InlineData(0.01, 1000.0)]
    // And the other way: zoomed right in, the grid subdivides again.
    [InlineData(100.0, 1.0)]
    public void TheGridThinsOutByDecadesAsYouZoomAway(double pixelsPerUnit, double expected)
    {
        var grid = new Grid { Spacing = 10 };

        // Drawing every line at every zoom is a million lines and a grey
        // screen; decades rather than doubling keeps the numbers round.
        Assert.Equal(expected, grid.EffectiveSpacing(pixelsPerUnit), 9);
    }

    [Fact]
    public void TheGridSnapsToTheSpacingYouCanActuallySee()
    {
        var drawing = SceneDrawing.CreateEmpty();
        var engine = new SnapEngine { Modes = SnapModes.Grid };
        engine.Grid.Spacing = 10;

        var result = engine.Resolve(drawing.ActiveLayout, drawing.Layers,
            new Vec2(23, 57), tolerance: 5, from: null, pixelsPerUnit: 1);

        Assert.Equal(SnapKind.Grid, result.Kind);
        Assert.Equal(new Vec2(20, 60), result.Point);
    }

    [Fact]
    public void GridLinesAreNotEnumeratedForeverWhenTheSpacingIsTooFine()
    {
        // A range that would need far too many lines gives none rather than
        // hanging the render thread counting them out.
        Assert.Empty(Grid.LinesAcross(0, 1e9, 1));

        Assert.Equal([0.0, 10.0, 20.0], Grid.LinesAcross(-5, 25, 10).ToArray());
    }
}
