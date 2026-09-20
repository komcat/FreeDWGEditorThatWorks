using FreeDwg.Core.Editing;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;

namespace FreeDwg.Tests;

/// <summary>
/// What each entity offers as handles, and what dragging one does to it.
/// </summary>
/// <remarks>
/// Arithmetic, like the tool tests: a grip is a world point in and a world
/// point out, with no mouse and no canvas anywhere near it. The two cases
/// worth knowing are the arc, whose sweep sign is invisible in its endpoints
/// and its bounds, and the bounds cache, which a stretched entity that
/// forgot to drop it would leave culled where it used to be.
/// </remarks>
public sealed class GripTests
{
    private static List<Grip> GripsOf(SceneEntity entity)
    {
        var grips = new List<Grip>();
        entity.CollectGrips(grips);
        return grips;
    }

    private static Grip Shape(SceneEntity entity, int index) =>
        GripsOf(entity).Single(grip => grip.Role == GripRole.Shape && grip.Index == index);

    // ---- lines -------------------------------------------------------------

    [Fact]
    public void ALineOffersItsTwoEndsToStretchAndItsMiddleToMove()
    {
        var grips = GripsOf(new SLine(new Vec2(0, 0), new Vec2(10, 20)));

        Assert.Equal(3, grips.Count);
        Assert.Equal(new Vec2(0, 0), Shape(new SLine(new Vec2(0, 0), new Vec2(10, 20)), 0).Point);

        var middle = Assert.Single(grips, grip => grip.Role == GripRole.Move);
        Assert.Equal(new Vec2(5, 10), middle.Point);
    }

    [Fact]
    public void DraggingOneEndOfALineLeavesTheOtherWhereItWas()
    {
        var line = new SLine(new Vec2(0, 0), new Vec2(10, 0));

        Assert.True(line.MoveGrip(Shape(line, 1), new Vec2(10, 40)));

        Assert.Equal(new Vec2(0, 0), line.Start);
        Assert.Equal(new Vec2(10, 40), line.End);
    }

    [Fact]
    public void AStretchedEntityDropsItsCachedBounds()
    {
        var line = new SLine(new Vec2(0, 0), new Vec2(10, 0));

        // Read once so there is a cache to go stale. This is the whole reason
        // MoveGrip is a wrapper rather than the override: an entity that moved
        // and kept its old bounds is culled where it used to be and picked
        // where it no longer is.
        Assert.Equal(0, line.Bounds.MaxY);

        line.MoveGrip(Shape(line, 1), new Vec2(10, 40));

        Assert.Equal(40, line.Bounds.MaxY);
    }

    [Fact]
    public void AGripIndexThatNoEntityOfferedIsRefused()
    {
        var line = new SLine(new Vec2(0, 0), new Vec2(10, 0));

        Assert.False(line.MoveGrip(new Grip(Vec2.Zero, GripRole.Shape, 7), new Vec2(5, 5)));
        Assert.Equal(new Vec2(10, 0), line.End);
    }

    // ---- circles -----------------------------------------------------------

    [Fact]
    public void ACircleOffersItsCentreToMoveAndItsQuadrantsToResize()
    {
        var circle = new SCircle(new Vec2(10, 10), 5);
        var grips = GripsOf(circle);

        Assert.Equal(new Vec2(10, 10), Assert.Single(grips, grip => grip.Role == GripRole.Move).Point);
        Assert.Equal(4, grips.Count(grip => grip.Role == GripRole.Shape));
        Assert.Equal(new Vec2(15, 10), Shape(circle, 0).Point);
        Assert.Equal(new Vec2(10, 5), Shape(circle, 3).Point);
    }

    [Fact]
    public void DraggingAQuadrantOfACircleSetsTheRadiusAndLeavesTheCentre()
    {
        var circle = new SCircle(new Vec2(10, 10), 5);

        // Dragged off the axis it was on: a circle has one dimension, so the
        // answer is the distance and not the displacement along X.
        Assert.True(circle.MoveGrip(Shape(circle, 0), new Vec2(16, 18)));

        Assert.Equal(new Vec2(10, 10), circle.Center);
        Assert.Equal(10, circle.Radius, 12);
    }

    // ---- arcs --------------------------------------------------------------

    /// <summary>A half turn over the top, from (10,0) to (-10,0) about the origin.</summary>
    private static SArc Semicircle() => new(Vec2.Zero, 10, 0, Math.PI);

    [Fact]
    public void AnArcOffersItsThreeDefiningPointsAndItsCentre()
    {
        var arc = Semicircle();

        Assert.Equal(new Vec2(10, 0), Shape(arc, 0).Point);
        Assert.Equal(-10, Shape(arc, 1).Point.X, 12);
        Assert.Equal(10, Shape(arc, 2).Point.Y, 12);
        Assert.Equal(Vec2.Zero, Assert.Single(GripsOf(arc), grip => grip.Role == GripRole.Move).Point);
    }

    [Fact]
    public void DraggingAnArcEndKeepsItPassingThroughItsOldMidpoint()
    {
        var arc = Semicircle();

        Assert.True(arc.MoveGrip(Shape(arc, 1), new Vec2(0, -10)));

        // Re-solved through the start, the old middle and the new end: three
        // quarters of the same circle, so the radius is unchanged, the start
        // has not moved, and the arc still goes round over the top rather
        // than taking the short way to the new end.
        Assert.Equal(10, arc.Radius, 9);
        Assert.Equal(10, arc.StartPoint.X, 9);
        Assert.Equal(-10, arc.EndPoint.Y, 9);
        Assert.True(arc.Sweep > 0, "an arc dragged the long way round still sweeps the way it did");
        Assert.True(ArcMath.Contains(Math.PI / 2, arc.StartAngle, arc.Sweep),
            "the point the arc used to pass through is still on it");
    }

    [Fact]
    public void DraggingAnArcsMiddleAcrossItsChordTurnsItOver()
    {
        var arc = Semicircle();
        Assert.True(arc.Sweep > 0);

        // The same endpoints, and the only thing that says which way round
        // the arc goes is where the middle point now is. Endpoints and bounds
        // would both be satisfied by the wrong answer here.
        Assert.True(arc.MoveGrip(Shape(arc, 2), new Vec2(0, -10)));

        Assert.True(arc.Sweep < 0, "an arc dragged under its chord must sweep the other way");
        Assert.Equal(10, arc.Radius, 9);
        Assert.Equal(-10, arc.MidPoint.Y, 9);
    }

    [Fact]
    public void AnArcDraggedIntoAStraightLineIsRefusedRatherThanCollapsed()
    {
        var arc = Semicircle();

        // Start, middle and end in a row describe no arc at all.
        Assert.False(arc.MoveGrip(Shape(arc, 2), new Vec2(0, 0)));
        Assert.Equal(10, arc.Radius);
        Assert.Equal(Math.PI, arc.Sweep);
    }

    // ---- polylines ---------------------------------------------------------

    private static SPolyline Bent() => new(
    [
        new PolyVertex(new Vec2(0, 0)),
        new PolyVertex(new Vec2(10, 0), bulge: 1),   // a half turn up to the next
        new PolyVertex(new Vec2(20, 0)),
    ], closed: false);

    [Fact]
    public void APolylineOffersEveryVertexAndTheMidpointOfEveryArcSegment()
    {
        var grips = GripsOf(Bent());

        Assert.Equal(4, grips.Count);

        // Three vertices, then the one bulged segment's midpoint -- which is
        // on the arc, a whole sagitta off the chord it is drawn from, and
        // *under* it: a positive bulge is a counter-clockwise sweep, and
        // counter-clockwise from (10,0) to (20,0) goes round the bottom.
        Assert.Equal(15, grips[3].Point.X, 12);
        Assert.Equal(-5, grips[3].Point.Y, 12);
        Assert.Equal(GripRole.Shape, grips[3].Role);
    }

    [Fact]
    public void AStraightPolylineSegmentOffersNoMidpointGrip()
    {
        var square = new SPolyline(
        [
            new PolyVertex(new Vec2(0, 0)),
            new PolyVertex(new Vec2(10, 0)),
            new PolyVertex(new Vec2(10, 10)),
            new PolyVertex(new Vec2(0, 10)),
        ], closed: true);

        // Four corners and nothing else: a handle that turned a straight
        // segment into an arc on a nudge would change what the segment is.
        Assert.Equal(4, GripsOf(square).Count);
    }

    [Fact]
    public void DraggingAVertexCarriesItsBulgeWithIt()
    {
        var polyline = Bent();

        Assert.True(polyline.MoveGrip(Shape(polyline, 1), new Vec2(10, 6)));

        Assert.Equal(new Vec2(10, 6), polyline.Vertices[1].Point);
        Assert.Equal(1, polyline.Vertices[1].Bulge);
        Assert.Equal(new Vec2(0, 0), polyline.Vertices[0].Point);
    }

    [Fact]
    public void DraggingAnArcSegmentsMidpointRebulgesIt()
    {
        var polyline = Bent();
        var midpoint = GripsOf(polyline)[3];

        // Pulled in towards the chord until the sweep is a quarter turn: a
        // 90-degree arc on a chord of 10 has a sagitta of 2.0711, and the
        // bulge that counts it is tan(sweep / 4) -- tan(22.5 degrees).
        Assert.True(polyline.MoveGrip(midpoint, new Vec2(15, -2.0710678118654755)));

        Assert.Equal(Math.Tan(Math.PI / 8), polyline.Vertices[1].Bulge, 9);
    }

    [Fact]
    public void DraggingAnArcSegmentAcrossItsChordTurnsItTheOtherWay()
    {
        var polyline = Bent();

        Assert.True(polyline.MoveGrip(GripsOf(polyline)[3], new Vec2(15, 5)));

        Assert.Equal(-1, polyline.Vertices[1].Bulge, 9);
    }

    // ---- ellipses, splines, and the ones with nothing to offer -------------

    [Fact]
    public void DraggingAnEllipsesMajorAxisTurnsAndStretchesIt()
    {
        var ellipse = new SEllipse(Vec2.Zero, new Vec2(10, 0), 0.5, 0, ArcMath.TwoPi);

        Assert.True(ellipse.MoveGrip(Shape(ellipse, 0), new Vec2(0, 20)));

        Assert.Equal(new Vec2(0, 20), ellipse.MajorAxis);
        Assert.Equal(0.5, ellipse.Ratio);
    }

    [Fact]
    public void DraggingAnEllipsesMinorAxisIsOnlyARatio()
    {
        var ellipse = new SEllipse(Vec2.Zero, new Vec2(10, 0), 0.5, 0, ArcMath.TwoPi);

        // Dragged off square on purpose: only the distance is read, because
        // an ellipse whose axes are not perpendicular is not one this model
        // can hold.
        Assert.True(ellipse.MoveGrip(Shape(ellipse, 1), new Vec2(3, 4)));

        Assert.Equal(new Vec2(10, 0), ellipse.MajorAxis);
        Assert.Equal(0.5, ellipse.Ratio, 12);
    }

    [Fact]
    public void DraggingASplinesControlPointLeavesItsKnotsAlone()
    {
        var spline = new SSpline(
            [new Vec2(0, 0), new Vec2(5, 10), new Vec2(10, 0)],
            [0, 0, 0, 1, 1, 1], degree: 2);

        var knots = spline.Knots.ToArray();

        Assert.True(spline.MoveGrip(Shape(spline, 1), new Vec2(5, 30)));

        Assert.Equal(new Vec2(5, 30), spline.ControlPoints[1]);
        Assert.Equal(knots, spline.Knots);
    }

    [Fact]
    public void AHatchOffersNoGrips()
    {
        var hatch = new SHatch([[new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10)]]) { IsSolid = true };

        // For the reason it offers no snap points: the boundary is derived
        // and already flattened, so its vertices are not the ones anyone drew.
        Assert.Empty(GripsOf(hatch));
    }

    [Fact]
    public void ATextOffersItsInsertionPointAndNothingElse()
    {
        var text = new SText(["hello"], new Vec2(3, 4), 2.5);

        var grip = Assert.Single(GripsOf(text));
        Assert.Equal(GripRole.Move, grip.Role);
        Assert.Equal(new Vec2(3, 4), grip.Point);
    }

    // ---- the set -----------------------------------------------------------

    [Fact]
    public void TheNearestGripWinsAndOnlyWithinTolerance()
    {
        var set = new GripSet();
        set.Rebuild([new SLine(new Vec2(0, 0), new Vec2(100, 0))]);

        Assert.Equal(new Vec2(0, 0), set.Nearest(new Vec2(1, 1), 5)!.Value.Point);
        Assert.Equal(new Vec2(50, 0), set.Nearest(new Vec2(52, 0), 5)!.Value.Point);
        Assert.Null(set.Nearest(new Vec2(25, 0), 5));
    }

    [Fact]
    public void TheGripSetCarriesTheEntityEachHandleCameFrom()
    {
        var first = new SLine(new Vec2(0, 0), new Vec2(10, 0));
        var second = new SLine(new Vec2(0, 20), new Vec2(10, 20));

        var set = new GripSet();
        set.Rebuild([first, second]);

        Assert.Equal(6, set.Count);
        Assert.Same(second, set.Nearest(new Vec2(0, 20), 1)!.Value.Entity);
    }

    [Fact]
    public void ASelectionTooBigToAimAtGetsNoGripsAtAll()
    {
        var many = Enumerable.Range(0, GripSet.MaxEntities + 1)
            .Select(i => (SceneEntity)new SLine(new Vec2(i, 0), new Vec2(i, 1)))
            .ToList();

        var set = new GripSet();
        set.Rebuild(many);

        // A crossing window over a whole drawing would otherwise bury the
        // geometry under handles nobody could pick out.
        Assert.True(set.IsEmpty);

        set.Rebuild(many.Take(GripSet.MaxEntities).ToList());
        Assert.Equal(GripSet.MaxEntities * 3, set.Count);
    }
}
