using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene.Entities;

namespace FreeDwg.Tests;

/// <summary>
/// Where curves cross, and the pieces each entity offers to be crossed.
/// </summary>
/// <remarks>
/// Trim, extend, fillet and chamfer all stand on this, and all four are the
/// kind of operation whose whole purpose is to leave no gap. So every
/// crossing here is asserted to full precision: a point that is a thousandth
/// out is a gap, and a gap defeats the operation that created it.
/// </remarks>
public sealed class IntersectionTests
{
    private static List<Vec2> Cross(in CurvePiece a, in CurvePiece b)
    {
        var points = new List<Vec2>();
        Intersection.Between(a, b, points);
        return points;
    }

    // ---- segments ---------------------------------------------------------

    [Fact]
    public void TwoSegmentsCrossOnce()
    {
        var points = Cross(
            CurvePiece.Segment(new Vec2(0, 0), new Vec2(10, 10)),
            CurvePiece.Segment(new Vec2(0, 10), new Vec2(10, 0)));

        var at = Assert.Single(points);
        Assert.Equal(5, at.X, 9);
        Assert.Equal(5, at.Y, 9);
    }

    [Fact]
    public void SegmentsThatOnlyWouldCrossDoNot()
    {
        // They cross where the lines would meet, well past both ends.
        Assert.Empty(Cross(
            CurvePiece.Segment(new Vec2(0, 0), new Vec2(1, 1)),
            CurvePiece.Segment(new Vec2(0, 10), new Vec2(1, 9))));
    }

    [Fact]
    public void SegmentsTouchingAtAnEndpointStillCount()
    {
        // A trim at a corner depends on this: the boundary meets the target
        // exactly at its end, and an exclusive test would miss it.
        var at = Assert.Single(Cross(
            CurvePiece.Segment(new Vec2(0, 0), new Vec2(10, 0)),
            CurvePiece.Segment(new Vec2(10, 0), new Vec2(10, 10))));

        Assert.Equal(new Vec2(10, 0), at);
    }

    [Fact]
    public void ParallelSegmentsNeverCross()
    {
        Assert.Empty(Cross(
            CurvePiece.Segment(new Vec2(0, 0), new Vec2(10, 0)),
            CurvePiece.Segment(new Vec2(0, 5), new Vec2(10, 5))));

        // Collinear and overlapping has no single crossing point either, and
        // trimming against one is not something anyone means to do.
        Assert.Empty(Cross(
            CurvePiece.Segment(new Vec2(0, 0), new Vec2(10, 0)),
            CurvePiece.Segment(new Vec2(5, 0), new Vec2(15, 0))));
    }

    // ---- segments and circles ---------------------------------------------

    [Fact]
    public void ASegmentThroughACircleCrossesItTwice()
    {
        var points = Cross(
            CurvePiece.Segment(new Vec2(-20, 0), new Vec2(20, 0)),
            CurvePiece.Circle(Vec2.Zero, 10));

        Assert.Equal(2, points.Count);
        Assert.Contains(points, p => Math.Abs(p.X + 10) < 1e-9);
        Assert.Contains(points, p => Math.Abs(p.X - 10) < 1e-9);
    }

    [Fact]
    public void ATangentSegmentTouchesOnceAndOnlyOnce()
    {
        var points = Cross(
            CurvePiece.Segment(new Vec2(-20, 10), new Vec2(20, 10)),
            CurvePiece.Circle(Vec2.Zero, 10));

        // Returning the same point twice would have trim believe there was a
        // zero-length piece between two crossings.
        var at = Assert.Single(points);
        Assert.Equal(0, at.X, 6);
        Assert.Equal(10, at.Y, 9);
    }

    [Fact]
    public void ACrossingOutsideAnArcsSweepIsNotACrossing()
    {
        // The upper half only; a line across the bottom misses it entirely.
        var upper = CurvePiece.Arc(Vec2.Zero, 10, 0, Math.PI);

        Assert.Empty(Cross(CurvePiece.Segment(new Vec2(-20, -5), new Vec2(20, -5)), upper));
        Assert.Equal(2, Cross(CurvePiece.Segment(new Vec2(-20, 5), new Vec2(20, 5)), upper).Count);
    }

    // ---- circles ----------------------------------------------------------

    [Fact]
    public void TwoOverlappingCirclesCrossTwice()
    {
        // Centres 16 apart, both radius 10: crossings at x = 8, y = +-6.
        var points = Cross(
            CurvePiece.Circle(Vec2.Zero, 10),
            CurvePiece.Circle(new Vec2(16, 0), 10));

        Assert.Equal(2, points.Count);
        Assert.All(points, p => Assert.Equal(8, p.X, 9));
        Assert.Contains(points, p => Math.Abs(p.Y - 6) < 1e-9);
        Assert.Contains(points, p => Math.Abs(p.Y + 6) < 1e-9);
    }

    [Theory]
    [InlineData(40.0, "too far apart to touch")]
    [InlineData(2.0, "one swallowed by the other")]
    [InlineData(0.0, "concentric, so either nothing or everything")]
    public void CirclesThatDoNotMeetGiveNothing(double apart, string what)
    {
        Assert.Empty(Cross(
            CurvePiece.Circle(Vec2.Zero, 10),
            CurvePiece.Circle(new Vec2(apart, 0), 5)));
    }

    [Fact]
    public void TouchingCirclesMeetAtOnePoint()
    {
        var at = Assert.Single(Cross(
            CurvePiece.Circle(Vec2.Zero, 10),
            CurvePiece.Circle(new Vec2(15, 0), 5)));

        Assert.Equal(10, at.X, 6);
        Assert.Equal(0, at.Y, 6);
    }

    // ---- reaching past the ends, which is what extend needs ----------------

    [Fact]
    public void AnUnboundedSegmentReachesACrossingBeyondItsEnd()
    {
        var stub = CurvePiece.Segment(new Vec2(0, 0), new Vec2(1, 0));
        var wall = CurvePiece.Segment(new Vec2(10, -5), new Vec2(10, 5));

        // Nothing while the segment is taken as it is...
        Assert.Empty(Cross(stub, wall));

        // ...but extend has to see where it would reach.
        var points = new List<Vec2>();
        Intersection.Unbounded(stub, wall, points);

        Assert.Equal(new Vec2(10, 0), Assert.Single(points));
    }

    // ---- what the entities offer -------------------------------------------

    [Fact]
    public void APolylineOffersItsBulgesAsArcs()
    {
        var polyline = new SPolyline(
            [new PolyVertex(new Vec2(0, 0), 1), new PolyVertex(new Vec2(10, 0))], closed: false);

        var curves = new List<CurvePiece>();
        polyline.CollectCurves(curves, 0.01);

        // As an arc, not a chord: trimming to the chord would cut in the
        // wrong place by a whole sagitta.
        var piece = Assert.Single(curves);
        Assert.True(piece.IsArc);
        Assert.Equal(5, piece.Radius, 9);
    }

    [Fact]
    public void ACircleOffersOneFullTurn()
    {
        var curves = new List<CurvePiece>();
        new SCircle(new Vec2(4, 5), 3).CollectCurves(curves, 0.01);

        var piece = Assert.Single(curves);
        Assert.True(piece.IsArc);
        Assert.Equal(ArcMath.TwoPi, Math.Abs(piece.Sweep), 9);
    }

    [Fact]
    public void ABlockInstanceOffersItsContentsWhereTheyAreDrawn()
    {
        var block = new FreeDwg.Core.Scene.BlockDefinition("BOX");
        block.Entities.Add(new SLine(new Vec2(0, 0), new Vec2(10, 0)));

        var insert = new SInsert(block,
            SInsert.BuildTransform(Vec2.Zero, new Vec2(100, 50), 2, 2, rotation: 0));

        var curves = new List<CurvePiece>();
        insert.CollectCurves(curves, 0.01);

        // Trimming *to* block geometry has to work; trimming the block itself
        // would cut the definition and every other instance with it.
        var piece = Assert.Single(curves);
        Assert.Equal(new Vec2(100, 50), piece.A);
        Assert.Equal(new Vec2(120, 50), piece.B);
    }

    [Fact]
    public void TextOffersNothingToTrimTo()
    {
        var curves = new List<CurvePiece>();
        new SText(["HELLO"], Vec2.Zero, 10).CollectCurves(curves, 0.01);

        Assert.Empty(curves);
    }
}
