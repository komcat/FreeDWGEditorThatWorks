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

    private static SnapResult Snap(SceneDrawing drawing, SnapEngine engine, Vec2 cursor,
        Vec2? from = null, Vec2? before = null) =>
        engine.Resolve(drawing.ActiveLayout, drawing.Layers, cursor, Tolerance, from, 1, before);

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

    // ---- perpendicular ------------------------------------------------------

    [Fact]
    public void APerpendicularLandsSquareOnTheLine()
    {
        var drawing = WithEntities(new SLine(new Vec2(0, 0), new Vec2(100, 0)));
        var engine = new SnapEngine { Modes = SnapModes.Perpendicular };

        // Drawing down from (30, 50): the right angle is at (30, 0).
        var result = Snap(drawing, engine, new Vec2(31, 1), from: new Vec2(30, 50));

        Assert.Equal(SnapKind.Perpendicular, result.Kind);
        Assert.Equal(new Vec2(30, 0), result.Point);
    }

    [Fact]
    public void APerpendicularNeedsSomewhereToBeDrawnFrom()
    {
        var drawing = WithEntities(new SLine(new Vec2(0, 0), new Vec2(100, 0)));
        var engine = new SnapEngine { Modes = SnapModes.Perpendicular };

        // No origin, so there is no right angle to be square to.
        Assert.False(Snap(drawing, engine, new Vec2(30, 1)).Found);
    }

    [Fact]
    public void AFootBeyondTheEndOfTheLineIsNotOffered()
    {
        var drawing = WithEntities(new SLine(new Vec2(0, 0), new Vec2(10, 0)));
        var engine = new SnapEngine { Modes = SnapModes.Perpendicular };

        // The foot would be at x = 50, well off the end. Clamping it to the
        // endpoint would offer a point endpoint snap already has, under a
        // name that promises a right angle.
        Assert.False(Snap(drawing, engine, new Vec2(50, 1), from: new Vec2(50, 50)).Found);
    }

    [Fact]
    public void APerpendicularOntoACircleRunsThroughItsCentre()
    {
        var drawing = WithEntities(new SCircle(Vec2.Zero, 10));
        var engine = new SnapEngine { Modes = SnapModes.Perpendicular };

        // From straight above: the near side of the rim, at (0, 10).
        var result = Snap(drawing, engine, new Vec2(0.5, 10.5), from: new Vec2(0, 40));

        Assert.Equal(SnapKind.Perpendicular, result.Kind);
        Assert.Equal(0, result.Point.X, 9);
        Assert.Equal(10, result.Point.Y, 9);
    }

    // ---- tangent --------------------------------------------------------------

    [Fact]
    public void ATangentTouchesTheCircleWithoutCrossingIt()
    {
        var drawing = WithEntities(new SCircle(Vec2.Zero, 10));
        var engine = new SnapEngine { Modes = SnapModes.Tangent };

        var from = new Vec2(0, 20);
        var result = Snap(drawing, engine, new Vec2(8.2, 5.5), from: from);

        Assert.Equal(SnapKind.Tangent, result.Kind);

        // The touch point is on the rim, and the radius there is square to
        // the line coming in: that is what tangency is.
        Assert.Equal(10, Vec2.Distance(result.Point, Vec2.Zero), 6);
        Assert.Equal(0.0, Vec2.Dot((result.Point - Vec2.Zero).Normalized(),
                                   (from - result.Point).Normalized()), 6);
    }

    [Fact]
    public void ATangentIsFoundByPointingAtTheCircleNotAtTheTouchPoint()
    {
        var drawing = WithEntities(new SCircle(Vec2.Zero, 10));
        var engine = new SnapEngine { Modes = SnapModes.Tangent };

        // Drawing in from the left, and hovering the far right of the rim --
        // nowhere near either touch point, which are up and down from there.
        var from = new Vec2(-40, 0);
        var result = Snap(drawing, engine, new Vec2(10.5, 0), from: from);

        Assert.Equal(SnapKind.Tangent, result.Kind);

        // Aiming at the touch point would mean knowing where it is first,
        // which is the whole reason the snap exists.
        Assert.True(Vec2.Distance(result.Point, new Vec2(10.5, 0)) > Tolerance,
            "the touch point is nowhere near where the cursor was pointing");

        Assert.Equal(10, Vec2.Distance(result.Point, Vec2.Zero), 6);
        Assert.Equal(0.0, Vec2.Dot((result.Point - Vec2.Zero).Normalized(),
                                   (from - result.Point).Normalized()), 6);
    }

    [Fact]
    public void APerpendicularIsFoundByPointingAtTheLine()
    {
        var drawing = WithEntities(new SLine(new Vec2(0, 0), new Vec2(100, 0)));
        var engine = new SnapEngine { Modes = SnapModes.Perpendicular };

        // Hovering the line well away from the foot, which is at x = 30.
        var result = Snap(drawing, engine, new Vec2(90, 0.4), from: new Vec2(30, 50));

        Assert.Equal(SnapKind.Perpendicular, result.Kind);
        Assert.Equal(new Vec2(30, 0), result.Point);
    }

    [Fact]
    public void PointingAtNothingStillFindsNoTangent()
    {
        var drawing = WithEntities(new SCircle(Vec2.Zero, 10));
        var engine = new SnapEngine { Modes = SnapModes.Tangent };

        // Well off the rim: the circle is not what is being pointed at.
        Assert.False(Snap(drawing, engine, new Vec2(40, 40), from: new Vec2(-40, 0)).Found);
    }

    [Fact]
    public void AnEndpointOnTheRimBeatsATangentThroughIt()
    {
        var drawing = WithEntities(
            new SCircle(Vec2.Zero, 10),
            new SLine(new Vec2(10, 0), new Vec2(40, 0)));

        var engine = new SnapEngine { Modes = SnapModes.Endpoint | SnapModes.Tangent };

        // Both are equally well aimed at; the one whose answer is under the
        // cursor is the one that was meant.
        var result = Snap(drawing, engine, new Vec2(10.2, 0.2), from: new Vec2(-40, 0));

        Assert.Equal(SnapKind.Endpoint, result.Kind);
        Assert.Equal(new Vec2(10, 0), result.Point);
    }

    [Fact]
    public void ThereIsNoTangentFromInsideTheCircle()
    {
        var drawing = WithEntities(new SCircle(Vec2.Zero, 10));
        var engine = new SnapEngine { Modes = SnapModes.Tangent };

        Assert.False(Snap(drawing, engine, new Vec2(9.5, 1), from: new Vec2(2, 0)).Found);
    }

    [Fact]
    public void AStraightLineOffersNoTangentPoint()
    {
        var drawing = WithEntities(new SLine(new Vec2(0, 0), new Vec2(100, 0)));
        var engine = new SnapEngine { Modes = SnapModes.Tangent };

        // A line is its own tangent everywhere, so there is no one point.
        Assert.False(Snap(drawing, engine, new Vec2(30, 1), from: new Vec2(30, 50)).Found);
    }

    // ---- intersections --------------------------------------------------------

    [Fact]
    public void TwoCrossingLinesSnapWhereTheyCross()
    {
        var drawing = WithEntities(
            new SLine(new Vec2(0, 0), new Vec2(100, 0)),
            new SLine(new Vec2(40, -50), new Vec2(40, 50)));

        var engine = new SnapEngine { Modes = SnapModes.Intersection };
        var result = Snap(drawing, engine, new Vec2(40.7, 0.6));

        Assert.Equal(SnapKind.Intersection, result.Kind);
        Assert.Equal(new Vec2(40, 0), result.Point);
    }

    [Fact]
    public void ALineCrossingACircleOffersTheNearerSide()
    {
        var drawing = WithEntities(
            new SCircle(Vec2.Zero, 10),
            new SLine(new Vec2(-50, 0), new Vec2(50, 0)));

        var engine = new SnapEngine { Modes = SnapModes.Intersection };

        // Two crossings, at -10 and +10; the cursor is by the right one.
        var result = Snap(drawing, engine, new Vec2(10.5, 0.4));

        Assert.Equal(SnapKind.Intersection, result.Kind);
        Assert.Equal(10, result.Point.X, 6);
        Assert.Equal(0, result.Point.Y, 6);
    }

    [Fact]
    public void ObjectsThatOnlyWouldCrossOfferNothing()
    {
        // They would meet at (40, 0) if the second ran far enough.
        var drawing = WithEntities(
            new SLine(new Vec2(0, 0), new Vec2(100, 0)),
            new SLine(new Vec2(40, 20), new Vec2(40, 50)));

        var engine = new SnapEngine { Modes = SnapModes.Intersection };

        Assert.False(Snap(drawing, engine, new Vec2(40, 0.5)).Found);
    }

    [Fact]
    public void APolylineVertexIsNotOfferedAsACrossing()
    {
        // Two segments of one polyline meet at every vertex. Reporting those
        // would offer each vertex twice, once under a marker that means
        // something else entirely.
        var drawing = WithEntities(new SPolyline(
            [new PolyVertex(new Vec2(0, 0)), new PolyVertex(new Vec2(40, 0)), new PolyVertex(new Vec2(40, 40))],
            closed: false));

        var engine = new SnapEngine { Modes = SnapModes.Intersection };

        Assert.False(Snap(drawing, engine, new Vec2(40.3, 0.3)).Found);
    }

    [Fact]
    public void ACrossingOnALockedLayerIsNotOffered()
    {
        var drawing = SceneDrawing.CreateEmpty();
        drawing.AddLayer(new Layer("LOCKED") { IsLocked = true });

        drawing.Add(new SLine(new Vec2(0, 0), new Vec2(100, 0)));
        drawing.Add(new SLine(new Vec2(40, -50), new Vec2(40, 50)) { LayerIndex = 1 });

        var engine = new SnapEngine { Modes = SnapModes.Intersection };

        // It takes two to cross, and one of them is not available.
        Assert.False(Snap(drawing, engine, new Vec2(40.4, 0.4)).Found);
    }

    [Fact]
    public void AnEndpointStillWinsWhenItIsNearer()
    {
        var drawing = WithEntities(
            new SLine(new Vec2(0, 0), new Vec2(100, 0)),
            new SLine(new Vec2(40, -50), new Vec2(40, 50)));

        var engine = new SnapEngine { Modes = SnapModes.Endpoint | SnapModes.Intersection };

        // Nearer the end of the first line than the crossing at (40, 0).
        var result = Snap(drawing, engine, new Vec2(99.8, 0.2));

        Assert.Equal(SnapKind.Endpoint, result.Kind);
        Assert.Equal(new Vec2(100, 0), result.Point);
    }

    [Fact]
    public void ACrossingWinsWhenItIsTheNearerOfTheTwo()
    {
        var drawing = WithEntities(
            new SLine(new Vec2(0, 0), new Vec2(100, 0)),
            new SLine(new Vec2(40, -50), new Vec2(40, 50)));

        var engine = new SnapEngine { Modes = SnapModes.Endpoint | SnapModes.Intersection };
        var result = Snap(drawing, engine, new Vec2(40.2, 0.2));

        Assert.Equal(SnapKind.Intersection, result.Kind);
    }

    [Fact]
    public void ACrossingIsNotOfferedWhileTheSnapIsSwitchedOff()
    {
        var drawing = WithEntities(
            new SLine(new Vec2(0, 0), new Vec2(100, 0)),
            new SLine(new Vec2(40, -50), new Vec2(40, 50)));

        var engine = new SnapEngine { Modes = SnapModes.Endpoint };

        Assert.False(Snap(drawing, engine, new Vec2(40.3, 0.3)).Found);
    }

    [Fact]
    public void ACrossingBeatsATangentAimedJustAsWell()
    {
        var drawing = WithEntities(
            new SCircle(Vec2.Zero, 10),
            new SLine(new Vec2(-20, 0), new Vec2(20, 0)));

        var engine = new SnapEngine { Modes = SnapModes.Intersection | SnapModes.Tangent };

        // The line cuts the rim at (10, 0), and the cursor is by it. The
        // tangent aims at the same circle just as closely, but its answer is
        // somewhere else entirely.
        var result = Snap(drawing, engine, new Vec2(10.3, 0.3), from: new Vec2(-40, 30));

        Assert.Equal(SnapKind.Intersection, result.Kind);
        Assert.Equal(10, result.Point.X, 6);
    }

    // ---- tracking -------------------------------------------------------------

    [Fact]
    public void ATrackedPointOffersALineLevelWithIt()
    {
        var drawing = WithEntities();
        var engine = new SnapEngine { Modes = SnapModes.Tracking };

        engine.Acquire(new Vec2(10, 40));

        // Away to the right, and nearly level.
        var result = Snap(drawing, engine, new Vec2(90, 40.8));

        Assert.Equal(SnapKind.Tracking, result.Kind);
        Assert.Equal(new Vec2(90, 40), result.Point);
        Assert.Equal(new Vec2(10, 40), Assert.Single(result.Guides));
    }

    [Fact]
    public void ATrackedPointAlsoOffersALineAboveIt()
    {
        var drawing = WithEntities();
        var engine = new SnapEngine { Modes = SnapModes.Tracking };

        engine.Acquire(new Vec2(10, 40));

        var result = Snap(drawing, engine, new Vec2(10.8, 90));

        Assert.Equal(new Vec2(10, 90), result.Point);
    }

    [Fact]
    public void TwoTrackedPointsGiveTheirCrossing()
    {
        var drawing = WithEntities();
        var engine = new SnapEngine { Modes = SnapModes.Tracking };

        engine.Acquire(new Vec2(10, 40));    // supplies the height
        engine.Acquire(new Vec2(70, 5));     // supplies the distance across

        var result = Snap(drawing, engine, new Vec2(70.6, 40.6));

        // Both coordinates pinned, which is what someone reaching for the
        // corner of two existing features is after.
        Assert.Equal(new Vec2(70, 40), result.Point);
        Assert.Equal(2, result.Guides.Count);
    }

    [Fact]
    public void OnlyTheLastTwoPointsStayAcquired()
    {
        var engine = new SnapEngine();

        engine.Acquire(new Vec2(1, 1));
        engine.Acquire(new Vec2(2, 2));
        engine.Acquire(new Vec2(3, 3));

        // A longer memory turns the screen into a cat's cradle.
        Assert.Equal(SnapEngine.MaxTracked, engine.Tracked.Count);
        Assert.DoesNotContain(new Vec2(1, 1), engine.Tracked);
        Assert.Contains(new Vec2(3, 3), engine.Tracked);
    }

    [Fact]
    public void AcquiringTheSamePointAgainDoesNotPushOutTheOther()
    {
        var engine = new SnapEngine();

        engine.Acquire(new Vec2(1, 1));
        engine.Acquire(new Vec2(2, 2));
        engine.Acquire(new Vec2(2, 2));

        Assert.Equal(2, engine.Tracked.Count);
        Assert.Contains(new Vec2(1, 1), engine.Tracked);
    }

    [Fact]
    public void NothingIsTrackedOnceTheAcquiredPointsAreCleared()
    {
        var drawing = WithEntities();
        var engine = new SnapEngine { Modes = SnapModes.Tracking };

        engine.Acquire(new Vec2(10, 40));
        engine.ClearTracking();

        Assert.False(Snap(drawing, engine, new Vec2(90, 40.2)).Found);
    }

    [Fact]
    public void AnObjectSnapStillBeatsTracking()
    {
        var drawing = WithEntities(new SLine(new Vec2(90, 41), new Vec2(120, 41)));
        var engine = new SnapEngine { Modes = SnapModes.Endpoint | SnapModes.Tracking };

        engine.Acquire(new Vec2(10, 40));

        // Level with the tracked point, and also right by a real endpoint.
        // Pointing at something that exists wins over lining up with one.
        var result = Snap(drawing, engine, new Vec2(90.3, 40.6));

        Assert.Equal(SnapKind.Endpoint, result.Kind);
        Assert.Equal(new Vec2(90, 41), result.Point);
    }

    [Fact]
    public void TrackingBeatsOrtho()
    {
        var drawing = WithEntities();
        var engine = new SnapEngine { Modes = SnapModes.Tracking, Ortho = true };

        engine.Acquire(new Vec2(10, 40));

        // Ortho would flatten this onto the origin's row; tracking puts it
        // level with a point that actually exists, which is more specific.
        var result = Snap(drawing, engine, new Vec2(90, 40.5), from: new Vec2(0, 0));

        Assert.Equal(SnapKind.Tracking, result.Kind);
        Assert.Equal(new Vec2(90, 40), result.Point);
    }

    // ---- polar ----------------------------------------------------------------

    [Fact]
    public void APointNearAPolarRayIsPulledOntoIt()
    {
        var drawing = WithEntities();
        var engine = new SnapEngine { Modes = SnapModes.Polar, PolarAngle = 45 };

        // Heading out at roughly forty-five degrees from the origin.
        var result = Snap(drawing, engine, new Vec2(50, 51), from: Vec2.Zero);

        Assert.Equal(SnapKind.Polar, result.Kind);
        Assert.Equal(result.Point.X, result.Point.Y, 9);
        Assert.Equal(Vec2.Zero, Assert.Single(result.Guides));
    }

    [Fact]
    public void APointBetweenTheRaysIsLeftAlone()
    {
        var drawing = WithEntities();
        var engine = new SnapEngine { Modes = SnapModes.Polar, PolarAngle = 45 };

        // Twenty-odd degrees: not near a ray at this distance, and polar
        // attracts rather than forcing, so the cursor stays put.
        var cursor = new Vec2(100, 40);

        Assert.False(Snap(drawing, engine, cursor, from: Vec2.Zero).Found);
    }

    [Theory]
    // Forty-five degrees is a ray when the increment divides it and not
    // otherwise: at thirty the nearest rays are fifteen degrees away, which
    // at this distance is far outside the tolerance.
    [InlineData(45.0, true)]
    [InlineData(90.0, false)]
    [InlineData(30.0, false)]
    public void TheAngleSettingDecidesWhichRaysExist(double increment, bool hasDiagonal)
    {
        var drawing = WithEntities();
        var engine = new SnapEngine { Modes = SnapModes.Polar, PolarAngle = increment };

        var result = Snap(drawing, engine, new Vec2(50, 50), from: Vec2.Zero);

        Assert.Equal(hasDiagonal, result.Found);
    }

    [Fact]
    public void ThirtyDegreesIsAvailableWhenTheIncrementSaysSo()
    {
        var drawing = WithEntities();
        var engine = new SnapEngine { Modes = SnapModes.Polar, PolarAngle = 30 };

        // tan(30) = 0.5774, so 100 across is 57.74 up.
        var result = Snap(drawing, engine, new Vec2(100, 57), from: Vec2.Zero);

        Assert.Equal(SnapKind.Polar, result.Kind);
        Assert.Equal(30.0, result.Point.Angle() * 180 / Math.PI, 6);
    }

    [Fact]
    public void AVerticalPolarRayIsExactlyVertical()
    {
        var drawing = WithEntities();
        var engine = new SnapEngine { Modes = SnapModes.Polar, PolarAngle = 45 };

        // Just off the vertical ray that runs up out of (20, 0).
        var result = Snap(drawing, engine, new Vec2(20.4, 500), from: new Vec2(20, 0));

        // Straight through a cosine this drifts sideways by a rounding step
        // for every unit of length, and snapping exists to make points land
        // exactly.
        Assert.Equal(20.0, result.Point.X);
    }

    [Fact]
    public void PolarNeedsAPointToRunOutOf()
    {
        var drawing = WithEntities();
        var engine = new SnapEngine { Modes = SnapModes.Polar };

        // Nothing picked yet, so there is no direction to be at an angle to.
        Assert.False(Snap(drawing, engine, new Vec2(50, 50)).Found);
    }

    [Fact]
    public void OrthoTakesPrecedenceOverPolarOutOfTheSamePoint()
    {
        var drawing = WithEntities();
        var engine = new SnapEngine { Modes = SnapModes.Polar, PolarAngle = 45, Ortho = true };

        // On the forty-five degree ray, but ortho forces rather than
        // attracts, so it has the last word.
        var result = Snap(drawing, engine, new Vec2(50, 50), from: Vec2.Zero);

        Assert.False(result.Found);
        Assert.Equal(new Vec2(50, 0), result.Point);
    }

    [Fact]
    public void ATrackedPointAndTheDrawingPointCrossAtAKnownCorner()
    {
        var drawing = WithEntities();
        var engine = new SnapEngine
        {
            Modes = SnapModes.Tracking | SnapModes.Polar,
            PolarAngle = 45,
        };

        // A height acquired from an existing feature, met by a line running
        // up at forty-five degrees: the corner of the two is at (40, 40).
        engine.Acquire(new Vec2(90, 40));

        var result = Snap(drawing, engine, new Vec2(40.5, 40.4), from: Vec2.Zero);

        Assert.Equal(new Vec2(40, 40), result.Point);
        Assert.Equal(2, result.Guides.Count);
    }

    [Fact]
    public void TrackingRaysFollowThePolarAngleToo()
    {
        var drawing = WithEntities();
        var engine = new SnapEngine { Modes = SnapModes.Tracking, PolarAngle = 45 };

        engine.Acquire(new Vec2(10, 10));

        // Up and to the right of the acquired point, on its diagonal.
        var result = Snap(drawing, engine, new Vec2(60, 59), from: null);

        Assert.Equal(SnapKind.Tracking, result.Kind);
        Assert.Equal(result.Point.X - 10, result.Point.Y - 10, 9);
    }

    [Fact]
    public void AnAbsurdlySmallAngleIsClampedRatherThanFillingTheScreenWithRays()
    {
        // Below the floor every ray is within reach at once, so none of them
        // would mean anything.
        Assert.Equal(Polar.Count(Polar.MinIncrement), Polar.Count(0.0001));
    }

    // ---- polar measured from the last segment ---------------------------------

    [Fact]
    public void RelativeAnglesTurnFromTheSegmentJustDrawn()
    {
        var drawing = WithEntities();
        var engine = new SnapEngine { Modes = SnapModes.Polar, PolarAngle = 45, PolarRelative = true };

        // The last run went up at ninety degrees, so a forty-five degree
        // turn off it points back along 135, not along 45.
        var from = new Vec2(0, 100);
        var before = new Vec2(0, 0);

        double wanted = 135 * Math.PI / 180;
        var onRay = from + new Vec2(Math.Cos(wanted), Math.Sin(wanted)) * 60;

        var result = Snap(drawing, engine, onRay + new Vec2(0.5, 0.5), from: from, before: before);

        Assert.Equal(SnapKind.Polar, result.Kind);
        Assert.Equal(135.0, (result.Point - from).Angle() * 180 / Math.PI, 6);
    }

    [Fact]
    public void WithRelativeAnglesTheOldAbsoluteRayIsGone()
    {
        var drawing = WithEntities();
        var engine = new SnapEngine { Modes = SnapModes.Polar, PolarAngle = 90, PolarRelative = true };

        // The last run went up at thirty degrees. At ninety-degree steps the
        // rays are now 30, 120, 210 and 300 -- due east is no longer one.
        var from = new Vec2(0, 0);
        var before = new Vec2(-Math.Cos(Math.PI / 6), -Math.Sin(Math.PI / 6));

        Assert.False(Snap(drawing, engine, new Vec2(100, 0), from: from, before: before).Found);
    }

    [Fact]
    public void AbsoluteAnglesIgnoreTheLastSegment()
    {
        var drawing = WithEntities();
        var engine = new SnapEngine { Modes = SnapModes.Polar, PolarAngle = 90, PolarRelative = false };

        var from = new Vec2(0, 0);
        var before = new Vec2(-Math.Cos(Math.PI / 6), -Math.Sin(Math.PI / 6));

        // Switched off, east is a ray again whatever came before.
        var result = Snap(drawing, engine, new Vec2(100, 0.3), from: from, before: before);

        Assert.Equal(SnapKind.Polar, result.Kind);
        Assert.Equal(new Vec2(100, 0), result.Point);
    }

    [Fact]
    public void TheFirstSegmentHasNothingToBeRelativeTo()
    {
        var drawing = WithEntities();
        var engine = new SnapEngine { Modes = SnapModes.Polar, PolarAngle = 45, PolarRelative = true };

        // One point picked, so there is no previous run: it falls back to
        // east on its own rather than needing to be told.
        var result = Snap(drawing, engine, new Vec2(100, 0.4), from: Vec2.Zero);

        Assert.Equal(SnapKind.Polar, result.Kind);
        Assert.Equal(new Vec2(100, 0), result.Point);
    }

    [Fact]
    public void ADoubledBackPointLeavesTheAnglesWhereTheyWere()
    {
        var drawing = WithEntities();
        var engine = new SnapEngine { Modes = SnapModes.Polar, PolarAngle = 90, PolarRelative = true };

        // The previous point is the same as this one, so the segment has no
        // direction to measure from.
        var from = new Vec2(30, 30);
        var result = Snap(drawing, engine, new Vec2(130, 30.3), from: from, before: from);

        Assert.Equal(new Vec2(130, 30), result.Point);
    }

    [Fact]
    public void TrackingRaysStayAbsoluteWhilePolarTurns()
    {
        var drawing = WithEntities();
        var engine = new SnapEngine
        {
            Modes = SnapModes.Tracking,
            PolarAngle = 90,
            PolarRelative = true,
        };

        engine.Acquire(new Vec2(10, 40));

        // Lining up level with a corner is the whole point of tracking, and
        // rotating those rays with the last segment would take it away.
        var from = new Vec2(0, 0);
        var before = new Vec2(-Math.Cos(Math.PI / 6), -Math.Sin(Math.PI / 6));

        var result = Snap(drawing, engine, new Vec2(90, 40.3), from: from, before: before);

        Assert.Equal(SnapKind.Tracking, result.Kind);
        Assert.Equal(new Vec2(90, 40), result.Point);
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
