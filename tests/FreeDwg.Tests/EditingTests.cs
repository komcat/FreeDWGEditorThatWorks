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
}
