using FreeDwg.Core.Editing;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;

namespace FreeDwg.Tests;

/// <summary>
/// Trim, extend, fillet, chamfer and tangent mate.
/// </summary>
/// <remarks>
/// Every one of these exists to remove a gap, so the assertions are on exact
/// coordinates. "Near enough" is the failure mode being guarded against, not
/// an acceptable result.
/// </remarks>
public sealed class EditingTests
{
    private static List<CurvePiece> Curves(params SceneEntity[] entities)
    {
        var curves = new List<CurvePiece>();
        foreach (var entity in entities) entity.CollectCurves(curves, 0.001);
        return curves;
    }

    // ---- trim -------------------------------------------------------------

    [Fact]
    public void TrimmingTheEndOfALineShortensIt()
    {
        var line = new SLine(new Vec2(0, 0), new Vec2(100, 0));
        var wall = new SLine(new Vec2(60, -10), new Vec2(60, 10));

        // Clicked past the wall: that stretch goes.
        var plan = Trimming.Trim(line, Curves(wall), new Vec2(80, 0));

        var kept = Assert.IsType<SLine>(Assert.Single(plan.Added));
        Assert.Equal(new Vec2(0, 0), kept.Start);
        Assert.Equal(new Vec2(60, 0), kept.End);
        Assert.Same(line, Assert.Single(plan.Removed));
    }

    [Fact]
    public void TrimmingTheMiddleOfALineLeavesTwoPieces()
    {
        var line = new SLine(new Vec2(0, 0), new Vec2(100, 0));
        var left = new SLine(new Vec2(30, -10), new Vec2(30, 10));
        var right = new SLine(new Vec2(70, -10), new Vec2(70, 10));

        var plan = Trimming.Trim(line, Curves(left, right), new Vec2(50, 0));

        Assert.Equal(2, plan.Added.Count);

        var first = (SLine)plan.Added[0];
        var second = (SLine)plan.Added[1];

        Assert.Equal(new Vec2(0, 0), first.Start);
        Assert.Equal(new Vec2(30, 0), first.End);
        Assert.Equal(new Vec2(70, 0), second.Start);
        Assert.Equal(new Vec2(100, 0), second.End);
    }

    [Fact]
    public void ATrimmedPieceKeepsTheHandleOfWhatItCameFrom()
    {
        var line = new SLine(new Vec2(0, 0), new Vec2(100, 0)) { SourceHandle = 0x2A, LayerIndex = 2 };
        var wall = new SLine(new Vec2(60, -10), new Vec2(60, 10));

        var plan = Trimming.Trim(line, Curves(wall), new Vec2(80, 0));
        var kept = plan.Added[0];

        // Still the line the file knows about, so a save rewrites it rather
        // than deleting it and inventing another in its place.
        Assert.Equal(0x2AUL, kept.SourceHandle);
        Assert.Equal(2, kept.LayerIndex);
    }

    [Fact]
    public void TrimmingWithNothingInTheWayDoesNothing()
    {
        var line = new SLine(new Vec2(0, 0), new Vec2(100, 0));
        var elsewhere = new SLine(new Vec2(0, 50), new Vec2(100, 50));

        Assert.True(Trimming.Trim(line, Curves(elsewhere), new Vec2(50, 0)).IsEmpty);
    }

    [Fact]
    public void TrimmingACircleLeavesAnArc()
    {
        var circle = new SCircle(Vec2.Zero, 10);
        var cut = new SLine(new Vec2(-20, 0), new Vec2(20, 0));

        // Clicked on the top half, so the top half goes.
        var plan = Trimming.Trim(circle, Curves(cut), new Vec2(0, 10));
        var arc = Assert.IsType<SArc>(Assert.Single(plan.Added));

        Assert.Equal(Vec2.Zero, arc.Center);
        Assert.Equal(10, arc.Radius, 9);
        Assert.Equal(Math.PI, Math.Abs(arc.Sweep), 6);

        // What is left is the bottom: its midpoint is below the axis.
        var midpoint = ArcMath.PointAt(arc.Center, arc.Radius, arc.StartAngle + arc.Sweep / 2);
        Assert.True(midpoint.Y < 0, $"the untouched half should be the lower one, got {midpoint}");
    }

    [Fact]
    public void TrimmingAnArcShortensItsSweep()
    {
        // The upper half of a circle, cut by a vertical line at x = 0.
        var arc = new SArc(Vec2.Zero, 10, 0, Math.PI);
        var cut = new SLine(new Vec2(0, -20), new Vec2(0, 20));

        // Clicked on the left quarter, which goes.
        var plan = Trimming.Trim(arc, Curves(cut), ArcMath.PointAt(Vec2.Zero, 10, Math.PI * 0.75));
        var kept = Assert.IsType<SArc>(Assert.Single(plan.Added));

        Assert.Equal(Math.PI / 2, Math.Abs(kept.Sweep), 6);

        var midpoint = ArcMath.PointAt(kept.Center, kept.Radius, kept.StartAngle + kept.Sweep / 2);
        Assert.True(midpoint.X > 0, $"the right quarter should survive, got {midpoint}");
    }

    // ---- extend -----------------------------------------------------------

    [Fact]
    public void ExtendingALineReachesTheBoundary()
    {
        var line = new SLine(new Vec2(0, 0), new Vec2(40, 0));
        var wall = new SLine(new Vec2(90, -10), new Vec2(90, 10));

        // Clicked near the far end, so that is the end that moves.
        var plan = Trimming.Extend(line, Curves(wall), new Vec2(38, 0));
        var longer = Assert.IsType<SLine>(Assert.Single(plan.Added));

        Assert.Equal(new Vec2(0, 0), longer.Start);
        Assert.Equal(new Vec2(90, 0), longer.End);
    }

    [Fact]
    public void ExtendingBackwardsMovesTheOtherEnd()
    {
        var line = new SLine(new Vec2(40, 0), new Vec2(80, 0));
        var wall = new SLine(new Vec2(10, -10), new Vec2(10, 10));

        var plan = Trimming.Extend(line, Curves(wall), new Vec2(42, 0));
        var longer = Assert.IsType<SLine>(Assert.Single(plan.Added));

        Assert.Equal(new Vec2(10, 0), longer.Start);
        Assert.Equal(new Vec2(80, 0), longer.End);
    }

    [Fact]
    public void ExtendingToACircleStopsAtTheNearerSide()
    {
        var line = new SLine(new Vec2(0, 0), new Vec2(10, 0));
        var circle = new SCircle(new Vec2(60, 0), 20);

        var plan = Trimming.Extend(line, Curves(circle), new Vec2(9, 0));
        var longer = Assert.IsType<SLine>(Assert.Single(plan.Added));

        // The near rim at x = 40, not the far one at 80.
        Assert.Equal(40, longer.End.X, 6);
    }

    [Fact]
    public void ExtendingTowardsNothingDoesNothing()
    {
        var line = new SLine(new Vec2(0, 0), new Vec2(40, 0));
        var parallel = new SLine(new Vec2(0, 10), new Vec2(100, 10));

        Assert.True(Trimming.Extend(line, Curves(parallel), new Vec2(38, 0)).IsEmpty);
    }

    // ---- fillet -----------------------------------------------------------

    [Fact]
    public void AZeroRadiusFilletBringsTwoLinesToASharpCorner()
    {
        // Two lines that stop short of where they would meet, at (50, 50).
        var horizontal = new SLine(new Vec2(0, 50), new Vec2(30, 50));
        var vertical = new SLine(new Vec2(50, 0), new Vec2(50, 30));

        var plan = Corners.Fillet(horizontal, new Vec2(5, 50), vertical, new Vec2(50, 5), radius: 0);

        Assert.Equal(2, plan.Added.Count);

        var a = (SLine)plan.Added[0];
        var b = (SLine)plan.Added[1];

        // Each keeps the end the user clicked near and reaches the corner.
        Assert.Equal(new Vec2(0, 50), a.Start);
        Assert.Equal(new Vec2(50, 50), a.End);
        Assert.Equal(new Vec2(50, 0), b.Start);
        Assert.Equal(new Vec2(50, 50), b.End);
    }

    [Fact]
    public void AFilletRoundsTheCornerWithATangentArc()
    {
        var horizontal = new SLine(new Vec2(0, 0), new Vec2(100, 0));
        var vertical = new SLine(new Vec2(0, 0), new Vec2(0, 100));

        var plan = Corners.Fillet(horizontal, new Vec2(90, 0), vertical, new Vec2(0, 90), radius: 10);

        Assert.Equal(3, plan.Added.Count);

        var arc = Assert.IsType<SArc>(plan.Added[2]);
        Assert.Equal(10, arc.Radius, 9);

        // A right angle filleted at radius 10 has its centre at (10,10) and
        // touches the axes at (10,0) and (0,10).
        Assert.Equal(10, arc.Center.X, 9);
        Assert.Equal(10, arc.Center.Y, 9);
        Assert.Equal(Math.PI / 2, Math.Abs(arc.Sweep), 9);

        var trimmedHorizontal = (SLine)plan.Added[0];
        var trimmedVertical = (SLine)plan.Added[1];

        // Component-wise: the setback comes out of a tangent, so the answer
        // is 10 to within a rounding step rather than the literal double 10.
        Assert.Equal(new Vec2(100, 0), trimmedHorizontal.Start);
        Assert.Equal(10, trimmedHorizontal.End.X, 9);
        Assert.Equal(0, trimmedHorizontal.End.Y, 9);

        Assert.Equal(new Vec2(0, 100), trimmedVertical.Start);
        Assert.Equal(0, trimmedVertical.End.X, 9);
        Assert.Equal(10, trimmedVertical.End.Y, 9);
    }

    [Fact]
    public void TheArcOfAFilletTouchesBothLines()
    {
        var first = new SLine(new Vec2(0, 0), new Vec2(100, 0));
        var second = new SLine(new Vec2(0, 0), new Vec2(80, 60));

        var plan = Corners.Fillet(first, new Vec2(90, 0), second, new Vec2(70, 52.5), radius: 12);
        var arc = Assert.IsType<SArc>(plan.Added[2]);

        // Tangent means the centre is exactly the radius from each line.
        Assert.Equal(12, Distance.PointToSegment(arc.Center, new Vec2(0, 0), new Vec2(100, 0)), 6);
        Assert.Equal(12, Distance.PointToSegment(arc.Center, new Vec2(0, 0), new Vec2(80, 60)), 6);

        // And its ends sit on the lines rather than near them.
        Assert.Equal(0.0, Distance.PointToSegment(arc.StartPoint, new Vec2(0, 0), new Vec2(100, 0)), 6);
        Assert.Equal(0.0, Distance.PointToSegment(arc.EndPoint, new Vec2(0, 0), new Vec2(80, 60)), 6);
    }

    [Fact]
    public void AFilletTooBigForTheLinesIsRefused()
    {
        var horizontal = new SLine(new Vec2(0, 0), new Vec2(10, 0));
        var vertical = new SLine(new Vec2(0, 0), new Vec2(0, 10));

        // The setback would run past the far end of both lines.
        Assert.True(Corners.Fillet(horizontal, new Vec2(9, 0), vertical, new Vec2(0, 9), radius: 50).IsEmpty);
    }

    [Fact]
    public void ParallelLinesHaveNoCornerToFillet()
    {
        var first = new SLine(new Vec2(0, 0), new Vec2(100, 0));
        var second = new SLine(new Vec2(0, 20), new Vec2(100, 20));

        Assert.True(Corners.Fillet(first, new Vec2(50, 0), second, new Vec2(50, 20), radius: 5).IsEmpty);
    }

    // ---- chamfer ----------------------------------------------------------

    [Fact]
    public void AChamferCutsTheCornerOffStraight()
    {
        var horizontal = new SLine(new Vec2(0, 0), new Vec2(100, 0));
        var vertical = new SLine(new Vec2(0, 0), new Vec2(0, 100));

        var plan = Corners.Chamfer(horizontal, new Vec2(90, 0), vertical, new Vec2(0, 90), distance: 20);

        Assert.Equal(3, plan.Added.Count);

        var bridge = Assert.IsType<SLine>(plan.Added[2]);
        Assert.Equal(new Vec2(20, 0), bridge.Start);
        Assert.Equal(new Vec2(0, 20), bridge.End);

        Assert.Equal(new Vec2(20, 0), ((SLine)plan.Added[0]).End);
        Assert.Equal(new Vec2(0, 20), ((SLine)plan.Added[1]).End);
    }

    // ---- tangent mate -----------------------------------------------------

    [Fact]
    public void MatingACircleToALineBringsItJustIntoContact()
    {
        var circle = new SCircle(new Vec2(50, 40), 10);
        var line = new SLine(new Vec2(0, 0), new Vec2(100, 0));

        var offset = Assert.NotNull(Tangency.ToLine(circle, line));
        circle.Transform(Mat3.Translation(offset));

        // Touching: the centre sits exactly a radius off the line.
        Assert.Equal(10, Distance.PointToSegment(circle.Center, new Vec2(0, 0), new Vec2(100, 0)), 9);
        Assert.Equal(50, circle.Center.X, 9);
        Assert.Equal(10, circle.Center.Y, 9);
    }

    [Fact]
    public void MatingKeepsTheCircleOnTheSideItStartedOn()
    {
        var below = new SCircle(new Vec2(50, -40), 10);
        var line = new SLine(new Vec2(0, 0), new Vec2(100, 0));

        below.Transform(Mat3.Translation(Assert.NotNull(Tangency.ToLine(below, line))));

        // A circle that jumped across the line it was being mated to would be
        // a surprise, and surprises in a constraint are bugs.
        Assert.Equal(-10, below.Center.Y, 9);
    }

    [Fact]
    public void MatingPushesOutwardsWhenTheLineCutsTheCircle()
    {
        // The line passes through the circle, so the move is outward.
        var circle = new SCircle(new Vec2(50, 3), 10);
        var line = new SLine(new Vec2(0, 0), new Vec2(100, 0));

        circle.Transform(Mat3.Translation(Assert.NotNull(Tangency.ToLine(circle, line))));

        Assert.Equal(10, circle.Center.Y, 9);
    }

    [Fact]
    public void ACircleCentredOnTheLineHasNoSideToKeep()
    {
        var circle = new SCircle(new Vec2(50, 0), 10);
        var line = new SLine(new Vec2(0, 0), new Vec2(100, 0));

        Assert.Null(Tangency.ToLine(circle, line));
    }

    // ---- fillet and chamfer on a polyline corner ---------------------------

    /// <summary>A 100 by 60 rectangle, as the rectangle tool makes one.</summary>
    private static SPolyline Rectangle() => new(
    [
        new PolyVertex(new Vec2(0, 0)),
        new PolyVertex(new Vec2(100, 0)),
        new PolyVertex(new Vec2(100, 60)),
        new PolyVertex(new Vec2(0, 60)),
    ], closed: true);

    [Fact]
    public void FilletingTwoSegmentsOfAPolylineLeavesItOnePolyline()
    {
        var rectangle = Rectangle();

        // The bottom edge and the right edge, which share the corner at
        // (100,0). Clicked where a user would: out along each one.
        var plan = Corners.Fillet(rectangle, new Vec2(50, 0), new Vec2(100, 30), radius: 10);

        var result = Assert.IsType<SPolyline>(Assert.Single(plan.Added));
        Assert.Same(rectangle, Assert.Single(plan.Removed));

        // One corner became two vertices; nothing was exploded into pieces.
        Assert.Equal(5, result.Vertices.Length);
        Assert.True(result.Closed);
    }

    [Fact]
    public void TheRoundedCornerStartsAndEndsWhereTheArcIsTangent()
    {
        var plan = Corners.Fillet(Rectangle(), new Vec2(50, 0), new Vec2(100, 30), radius: 10);
        var result = (SPolyline)plan.Added[0];

        // A right angle sets back by exactly the radius on each side.
        Assert.Equal(new Vec2(90, 0), result.Vertices[1].Point);
        Assert.Equal(new Vec2(100, 10), result.Vertices[2].Point);

        // And the bulge between them is a quarter turn: tan(90 / 4).
        Assert.Equal(Math.Tan(Math.PI / 8), result.Vertices[1].Bulge, 9);
        Assert.Equal(0, result.Vertices[2].Bulge);
    }

    [Fact]
    public void TheArcOfAPolylineFilletHasTheRadiusThatWasAskedFor()
    {
        var plan = Corners.Fillet(Rectangle(), new Vec2(50, 0), new Vec2(100, 30), radius: 10);
        var result = (SPolyline)plan.Added[0];

        // Read back out of the bulge, which is the only place it is stored:
        // a sign or a factor wrong here draws a corner of the wrong size
        // with both its ends still exactly where they belong.
        var (center, radius, _, _) = ArcMath.FromBulge(
            result.Vertices[1].Point, result.Vertices[2].Point, result.Vertices[1].Bulge);

        Assert.Equal(10, radius, 9);
        Assert.Equal(new Vec2(90, 10), center);
    }

    [Fact]
    public void ARoundedCornerBulgesIntoTheShapeAndNotOutOfIt()
    {
        // The same corner from a rectangle wound the other way round. The
        // turn reverses, so the bulge has to as well -- and the endpoints are
        // identical either way, which is what makes this invisible until it
        // is drawn.
        var clockwise = new SPolyline(
        [
            new PolyVertex(new Vec2(0, 0)),
            new PolyVertex(new Vec2(0, 60)),
            new PolyVertex(new Vec2(100, 60)),
            new PolyVertex(new Vec2(100, 0)),
        ], closed: true);

        var counter = (SPolyline)Corners.Fillet(Rectangle(), new Vec2(50, 0), new Vec2(100, 30), 10).Added[0];
        var over = (SPolyline)Corners.Fillet(clockwise, new Vec2(100, 30), new Vec2(50, 0), 10).Added[0];

        Assert.True(counter.Vertices[1].Bulge > 0, "counter-clockwise corner should sweep positive");

        var rounded = over.Vertices.Single(v => Math.Abs(v.Bulge) > 1e-12);
        Assert.True(rounded.Bulge < 0, "the same corner wound the other way sweeps negative");
    }

    [Fact]
    public void TheCornerWhereAClosedPolylineJoinsUpCanBeRoundedToo()
    {
        // The last segment and the first share vertex zero. Without the wrap
        // it is the one corner of a rectangle that cannot be rounded, which
        // is the sort of gap nobody reports and everybody notices.
        var plan = Corners.Fillet(Rectangle(), new Vec2(0, 30), new Vec2(50, 0), radius: 10);
        var result = (SPolyline)plan.Added[0];

        Assert.Equal(5, result.Vertices.Length);
        Assert.Equal(new Vec2(0, 10), result.Vertices[0].Point);
        Assert.Equal(new Vec2(10, 0), result.Vertices[1].Point);
    }

    [Fact]
    public void ChamferingAPolylineCornerCutsItStraight()
    {
        var plan = Corners.Chamfer(Rectangle(), new Vec2(50, 0), new Vec2(100, 30), distance: 15);
        var result = (SPolyline)plan.Added[0];

        Assert.Equal(new Vec2(85, 0), result.Vertices[1].Point);
        Assert.Equal(new Vec2(100, 15), result.Vertices[2].Point);

        // No bulge: the whole difference between the two operations.
        Assert.Equal(0, result.Vertices[1].Bulge);
    }

    [Fact]
    public void AChamferedPolylineKeepsTheHandleTheFileKnowsItBy()
    {
        var rectangle = Rectangle();
        rectangle.SourceHandle = 0x3B;

        var plan = Corners.Chamfer(rectangle, new Vec2(50, 0), new Vec2(100, 30), distance: 15);

        Assert.Equal(0x3BUL, plan.Added[0].SourceHandle);
    }

    [Fact]
    public void TwoSegmentsThatDoNotTouchHaveNoCorner()
    {
        // The bottom edge and the top edge of a rectangle: opposite, not
        // adjacent. Rounding "the corner between them" would have to invent
        // one.
        Assert.True(Corners.Fillet(Rectangle(), new Vec2(50, 0), new Vec2(50, 60), radius: 10).IsEmpty);

        // And the same segment twice.
        Assert.True(Corners.Fillet(Rectangle(), new Vec2(30, 0), new Vec2(70, 0), radius: 10).IsEmpty);
    }

    [Fact]
    public void AFilletTooBigForTheEdgesItSitsBetweenIsRefused()
    {
        var narrow = new SPolyline(
        [
            new PolyVertex(new Vec2(0, 0)),
            new PolyVertex(new Vec2(12, 0)),
            new PolyVertex(new Vec2(12, 40)),
        ], closed: false);

        // Setting back 30 along a 12 edge would move a corner that was not
        // the one picked.
        Assert.True(Corners.Fillet(narrow, new Vec2(6, 0), new Vec2(12, 20), radius: 30).IsEmpty);
    }

    [Fact]
    public void TheEndOfAnOpenPolylineIsNotACorner()
    {
        var run = new SPolyline(
        [
            new PolyVertex(new Vec2(0, 0)),
            new PolyVertex(new Vec2(50, 0)),
        ], closed: false);

        Assert.True(Corners.Fillet(run, new Vec2(10, 0), new Vec2(40, 0), radius: 5).IsEmpty);
    }

    [Fact]
    public void ACornerThatIsAlreadyAnArcIsLeftAlone()
    {
        var bent = new SPolyline(
        [
            new PolyVertex(new Vec2(0, 0)),
            new PolyVertex(new Vec2(50, 0), bulge: 0.5),
            new PolyVertex(new Vec2(50, 50)),
            new PolyVertex(new Vec2(0, 50)),
        ], closed: false);

        // Rounding into a curve is a tangent circle to a curve, which is a
        // different problem and not one to approximate quietly.
        Assert.True(Corners.Fillet(bent, new Vec2(25, 0), new Vec2(50, 25), radius: 5).IsEmpty);
    }
}
