using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Tools;

namespace FreeDwg.Tests;

/// <summary>
/// What each draw tool builds out of the points it is given.
/// </summary>
/// <remarks>
/// The tools take world points and give back scene entities, and know
/// nothing about mice or WPF, so the whole set tests as arithmetic. The
/// interesting cases are the ones where a click has to mean something
/// geometric: which way an arc goes, how far off an axis a third point is.
/// </remarks>
public sealed class ToolTests
{
    [Fact]
    public void ALineTakesTwoPointsAndNotOne()
    {
        var tool = new LineTool();

        Assert.Null(tool.Click(new Vec2(0, 0)));
        Assert.True(tool.InProgress);

        var line = Assert.IsType<SLine>(tool.Click(new Vec2(10, 5)));
        Assert.Equal(new Vec2(0, 0), line.Start);
        Assert.Equal(new Vec2(10, 5), line.End);

        // Finished tools let go, so the next click starts a new line.
        Assert.False(tool.InProgress);
    }

    [Fact]
    public void APolylineIsOnlyFinishedDeliberately()
    {
        var tool = new PolylineTool();

        Assert.Null(tool.Click(new Vec2(0, 0)));
        Assert.Null(tool.Click(new Vec2(10, 0)));
        Assert.Null(tool.Click(new Vec2(10, 10)));

        var polyline = Assert.IsType<SPolyline>(tool.Finish());

        Assert.Equal(3, polyline.Vertices.Length);
        Assert.False(polyline.Closed);
        Assert.Equal(new Vec2(10, 10), polyline.Vertices[2].Point);
        Assert.False(tool.InProgress);
    }

    [Fact]
    public void APolylineOfOnePointIsNothing()
    {
        var tool = new PolylineTool();
        tool.Click(new Vec2(4, 4));

        Assert.Null(tool.Finish());
        Assert.False(tool.InProgress);
    }

    [Fact]
    public void ARectangleIsAClosedPolylineWhicheverCornersAreGiven()
    {
        // Dragged up and to the left, which is the case that catches a
        // rectangle built by assuming the first corner is the minimum.
        var tool = new RectangleTool();
        tool.Click(new Vec2(30, 20));

        var rectangle = Assert.IsType<SPolyline>(tool.Click(new Vec2(10, 5)));

        Assert.True(rectangle.Closed);
        Assert.Equal(4, rectangle.Vertices.Length);

        var bounds = rectangle.Bounds;
        Assert.Equal(10, bounds.MinX, 9);
        Assert.Equal(5, bounds.MinY, 9);
        Assert.Equal(30, bounds.MaxX, 9);
        Assert.Equal(20, bounds.MaxY, 9);
    }

    [Fact]
    public void ARectangleWithNoAreaIsNotDrawn()
    {
        var tool = new RectangleTool();
        tool.Click(new Vec2(10, 10));

        // A line the user did not mean to draw is worse than nothing.
        Assert.Null(tool.Click(new Vec2(30, 10)));
    }

    [Theory]
    [InlineData(25.0, 7.0, 100.0, null, 110.0, 7.0)]   // width typed, height aimed
    [InlineData(25.0, 17.0, null, 40.0, 25.0, 50.0)]   // height typed, width aimed
    [InlineData(25.0, 17.0, 100.0, 40.0, 110.0, 50.0)] // both typed
    [InlineData(-3.0, -8.0, 100.0, 40.0, -90.0, -30.0)] // the cursor says which way
    [InlineData(10.0, 10.0, 100.0, 40.0, 110.0, 50.0)] // level with the corner: positive
    [InlineData(25.0, 7.0, null, null, 25.0, 7.0)]     // nothing typed: the cursor
    public void ATypedRectangleSizeHoldsItsSideAndTheCursorPicksTheDirection(
        double cursorX, double cursorY, double? width, double? height, double x, double y)
    {
        var corner = RectangleTool.Constrain(new Vec2(10, 10), new Vec2(cursorX, cursorY), width, height);
        Assert.Equal(new Vec2(x, y), corner);
    }

    [Fact]
    public void ACircleTakesItsRadiusFromTheSecondPoint()
    {
        var tool = new CircleTool();
        tool.Click(new Vec2(10, 10));

        var circle = Assert.IsType<SCircle>(tool.Click(new Vec2(13, 14)));

        Assert.Equal(new Vec2(10, 10), circle.Center);
        Assert.Equal(5, circle.Radius, 9);
    }

    [Fact]
    public void AZeroRadiusCircleIsNotDrawn()
    {
        var tool = new CircleTool();
        tool.Click(new Vec2(10, 10));

        Assert.Null(tool.Click(new Vec2(10, 10)));
    }

    // ---- arcs, where the middle point decides the direction --------------

    [Theory]
    // Left to right over the top is clockwise, not counter-clockwise: from
    // (0,0) the start angle is pi, and reaching (10,10) at pi/2 means the
    // angle decreasing. The sign of the sweep is the bug that turns every
    // bulge in a drawing inside out, so it is stated here both ways round.
    [InlineData(10.0, 10.0, false)]
    [InlineData(10.0, -10.0, true)]
    public void AnArcGoesTheWayItsMiddlePointLies(double throughX, double throughY, bool counterClockwise)
    {
        var tool = new ArcTool();
        tool.Click(new Vec2(0, 0));
        tool.Click(new Vec2(throughX, throughY));

        var arc = Assert.IsType<SArc>(tool.Click(new Vec2(20, 0)));

        Assert.Equal(new Vec2(10, 0), arc.Center);
        Assert.Equal(10, arc.Radius, 9);
        Assert.Equal(Math.PI, Math.Abs(arc.Sweep), 9);
        Assert.Equal(counterClockwise, arc.Sweep > 0);

        // Whichever way round, it has to start and end where it was asked to.
        Assert.Equal(0, arc.StartPoint.X, 6);
        Assert.Equal(0, arc.StartPoint.Y, 6);
        Assert.Equal(20, arc.EndPoint.X, 6);
        Assert.Equal(0, arc.EndPoint.Y, 6);
    }

    [Fact]
    public void AnArcPassesThroughItsMiddlePoint()
    {
        var tool = new ArcTool();
        tool.Click(new Vec2(0, 0));
        tool.Click(new Vec2(4, 6));
        var arc = Assert.IsType<SArc>(tool.Click(new Vec2(14, 2)));

        // The whole point of a three-point arc: the middle point is on it.
        Assert.Equal(0.0, arc.DistanceTo(new Vec2(4, 6), tolerance: 0.01), 6);
    }

    [Fact]
    public void ThreeCollinearPointsMakeNoArc()
    {
        var tool = new ArcTool();
        tool.Click(new Vec2(0, 0));
        tool.Click(new Vec2(5, 0));

        Assert.Null(tool.Click(new Vec2(10, 0)));
    }

    // ---- ellipses --------------------------------------------------------

    [Fact]
    public void AnEllipseTakesItsMinorAxisFromTheDistanceOffTheMajor()
    {
        var tool = new EllipseTool();
        tool.Click(new Vec2(0, 0));
        tool.Click(new Vec2(20, 0));

        // 8 off the axis, and the 30 along it must not matter.
        var ellipse = Assert.IsType<SEllipse>(tool.Click(new Vec2(30, 8)));

        Assert.Equal(new Vec2(20, 0), ellipse.MajorAxis);
        Assert.Equal(0.4, ellipse.Ratio, 9);
        Assert.True(ellipse.IsClosed);
    }

    [Fact]
    public void AnEllipseOfZeroWidthIsNotDrawn()
    {
        var tool = new EllipseTool();
        tool.Click(new Vec2(0, 0));
        tool.Click(new Vec2(20, 0));

        // Dead on the major axis: no minor axis at all.
        Assert.Null(tool.Click(new Vec2(12, 0)));
    }

    // ---- shared behaviour ------------------------------------------------

    [Fact]
    public void CancellingThrowsAwayThePointsPickedSoFar()
    {
        var tool = new PolylineTool();
        tool.Click(new Vec2(0, 0));
        tool.Click(new Vec2(10, 0));

        tool.Cancel();

        Assert.False(tool.InProgress);
        Assert.Null(tool.Finish());
    }

    [Fact]
    public void EveryToolSaysWhatItWantsNext()
    {
        DrawTool[] tools = [new LineTool(), new PolylineTool(), new RectangleTool(),
                            new CircleTool(), new ArcTool(), new EllipseTool()];

        foreach (var tool in tools)
        {
            string first = tool.Prompt;
            Assert.False(string.IsNullOrWhiteSpace(first), $"{tool.Name} has no opening prompt");

            tool.Click(new Vec2(0, 0));

            // The prompt is the whole interface for a tool mid-pick; one that
            // does not change has stopped telling the user anything.
            Assert.NotEqual(first, tool.Prompt);
        }
    }
}
