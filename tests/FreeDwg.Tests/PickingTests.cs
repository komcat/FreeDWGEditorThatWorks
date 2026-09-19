using FreeDwg.Core.Geometry;
using FreeDwg.Core.Picking;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;

namespace FreeDwg.Tests;

/// <summary>
/// What each entity considers a hit. No WPF, no files: picking is arithmetic
/// on the same geometry the renderer draws, so it can be pinned down exactly
/// rather than inferred from pixels.
/// </summary>
/// <remarks>
/// The distances asserted here are stated against the curve itself, never
/// against a flattening of it. Where a curve has to be flattened -- ellipses
/// and splines -- the assertion allows the documented error and no more.
/// </remarks>
public sealed class PickingTests
{
    private const double Tolerance = 0.5;

    private static PickContext Context(double tolerance = Tolerance) => new(tolerance);

    // ---- lines ----------------------------------------------------------

    [Theory]
    [InlineData(5, 3, 3.0, "beside the middle of the run")]
    [InlineData(0, 0, 0.0, "on the start point")]
    [InlineData(-4, 0, 4.0, "off the end, so measured to the endpoint")]
    [InlineData(-3, 4, 5.0, "off the end and to one side")]
    public void ALineIsMeasuredToTheSegmentNotTheInfiniteLine(double x, double y, double expected, string what)
    {
        var line = new SLine(new Vec2(0, 0), new Vec2(10, 0));
        double actual = line.DistanceTo(new Vec2(x, y), Context());

        Assert.True(Math.Abs(actual - expected) < 1e-9,
            $"{what}: expected {expected}, got {actual}");
    }

    // ---- circles and arcs -----------------------------------------------

    [Fact]
    public void ACircleIsPickedOnItsRimAndNotInItsMiddle()
    {
        var circle = new SCircle(new Vec2(0, 0), 10);

        Assert.Equal(0.0, circle.DistanceTo(new Vec2(10, 0), Context()), 9);
        Assert.Equal(2.0, circle.DistanceTo(new Vec2(12, 0), Context()), 9);

        // The disc is not the circle: clicking the middle must fall through to
        // whatever is actually drawn there.
        Assert.Equal(10.0, circle.DistanceTo(new Vec2(0, 0), Context()), 9);
    }

    [Fact]
    public void AnArcOutsideItsSweepIsMeasuredToTheNearerEnd()
    {
        // Quarter arc from +X round to +Y.
        var arc = new SArc(Vec2.Zero, 10, 0, Math.PI / 2);

        // Inside the sweep: radial.
        Assert.Equal(2.0, arc.DistanceTo(ArcMath.PointAt(Vec2.Zero, 12, Math.PI / 4), Context()), 9);

        // Outside it: the arc stops at (10,0), so a point below is measured
        // to that end rather than to the circle it would lie on.
        Assert.Equal(5.0, arc.DistanceTo(new Vec2(10, -5), Context()), 9);

        // Behind the arc entirely: the nearer end is (0,10), not (10,0).
        Assert.Equal(Math.Sqrt(200), arc.DistanceTo(new Vec2(-10, 0), Context()), 9);
    }

    // ---- polylines ------------------------------------------------------

    [Fact]
    public void ABulgedSegmentIsMeasuredToTheArcNotTheChord()
    {
        // Bulge 1 is a semicircle, and a positive bulge sweeps counter
        // clockwise, so a left-to-right chord sags: the apex is at (5,-5).
        var polyline = new SPolyline(
            [new PolyVertex(new Vec2(0, 0), 1), new PolyVertex(new Vec2(10, 0))], closed: false);

        // A polyline that flattened its bulges would put the apex 5 away.
        Assert.Equal(0.0, polyline.DistanceTo(new Vec2(5, -5), Context()), 9);
        Assert.Equal(1.0, polyline.DistanceTo(new Vec2(5, -6), Context()), 9);

        // Nothing is drawn on the side the arc does not go, so a point up
        // there is measured to the ends of the sweep, not across the circle.
        Assert.Equal(Math.Sqrt(50), polyline.DistanceTo(new Vec2(5, 5), Context()), 9);
    }

    [Fact]
    public void AClosedPolylineIncludesTheClosingSegment()
    {
        PolyVertex[] square =
        [
            new(new Vec2(0, 0)), new(new Vec2(10, 0)), new(new Vec2(10, 10)), new(new Vec2(0, 10)),
        ];

        var open = new SPolyline(square, closed: false);
        var closed = new SPolyline(square, closed: true);

        // Midway down the left edge, which only the closing run reaches.
        Assert.Equal(5.0, open.DistanceTo(new Vec2(0, 5), Context()), 9);
        Assert.Equal(0.0, closed.DistanceTo(new Vec2(0, 5), Context()), 9);
    }

    // ---- curves that have to be flattened -------------------------------

    [Fact]
    public void AnEllipseIsPickedWithinTheFlatteningError()
    {
        // Major axis 20 along +X, ratio 0.5, so the minor semi-axis is 10.
        var ellipse = new SEllipse(Vec2.Zero, new Vec2(20, 0), 0.5, 0, ArcMath.TwoPi);

        foreach (double t in new[] { 0.0, 0.7, 1.9, 3.3, 5.1 })
        {
            var onCurve = EllipseMath.PointAt(Vec2.Zero, new Vec2(20, 0), 0.5, t);

            // Flattening pulls the chord inside the curve by up to a quarter
            // of the tolerance; more than that would be a real error.
            Assert.InRange(ellipse.DistanceTo(onCurve, Context()), 0.0, Tolerance / 4);
        }

        Assert.Equal(5.0, ellipse.DistanceTo(new Vec2(25, 0), Context()), 1);
    }

    [Fact]
    public void ASplineIsPickedOnTheCurveAndNotOnItsControlPolygon()
    {
        Vec2[] control = [new(0, 0), new(10, 30), new(30, 30), new(40, 0)];
        double[] knots = [0, 0, 0, 0, 1, 1, 1, 1];
        var spline = new SSpline(control, knots, degree: 3);

        var onCurve = BSpline.Evaluate(control, null, knots, 3, 0.5);
        Assert.InRange(spline.DistanceTo(onCurve, Context()), 0.0, Tolerance / 4);

        // The middle control points sit well above the curve they pull.
        Assert.True(spline.DistanceTo(new Vec2(10, 30), Context()) > 5.0,
            "a control point is not on the curve and must not read as a hit");
    }

    // ---- regions --------------------------------------------------------

    [Fact]
    public void AHatchIsPickedAnywhereInsideItButNotInAnIsland()
    {
        IReadOnlyList<Vec2> outer = [new(0, 0), new(20, 0), new(20, 20), new(0, 20)];
        IReadOnlyList<Vec2> island = [new(8, 8), new(12, 8), new(12, 12), new(8, 12)];

        var hatch = new SHatch([outer, island]) { IsSolid = true };

        Assert.Equal(0.0, hatch.DistanceTo(new Vec2(4, 4), Context()), 9);
        Assert.Equal(0.0, hatch.DistanceTo(new Vec2(0, 10), Context()), 9);

        // The island is a hole: nothing is painted there, so nothing is picked.
        Assert.Equal(2.0, hatch.DistanceTo(new Vec2(10, 10), Context()), 9);
        Assert.Equal(3.0, hatch.DistanceTo(new Vec2(-3, 10), Context()), 9);
    }

    [Fact]
    public void TextIsPickedOnItsRotatedBoxNotItsBoundingBox()
    {
        var text = new SText(["HELLO"], new Vec2(0, 0), height: 10) { Rotation = Math.PI / 4 };

        // Up the 45 degree baseline is inside the box however wide the glyphs
        // measure; the same distance along +X is not.
        var alongBaseline = new Vec2(4 * Math.Cos(Math.PI / 4), 4 * Math.Sin(Math.PI / 4));
        Assert.Equal(0.0, text.DistanceTo(alongBaseline, Context()), 9);

        // Well inside the axis-aligned bounds, well outside the text itself.
        Assert.True(text.DistanceTo(new Vec2(20, -6), Context()) > 1.0,
            "a point beside rotated text must not be picked off its bounding box");
    }

    // ---- blocks ---------------------------------------------------------

    [Fact]
    public void ABlockInstanceIsHitTestedThroughItsTransform()
    {
        var block = new BlockDefinition("RING");
        block.Entities.Add(new SCircle(Vec2.Zero, 5));

        // Placed at (100, 50) and scaled by three, so the rim is at r = 15.
        var insert = new SInsert(block,
            SInsert.BuildTransform(Vec2.Zero, new Vec2(100, 50), 3, 3, rotation: 0));

        Assert.Equal(0.0, insert.DistanceTo(new Vec2(115, 50), Context()), 9);
        Assert.Equal(0.0, insert.DistanceTo(new Vec2(100, 65), Context()), 9);

        // Distances come back in the caller's units, not the block's.
        Assert.Equal(3.0, insert.DistanceTo(new Vec2(118, 50), Context()), 9);
    }

    [Fact]
    public void GeometryOnAnInvisibleLayerInsideABlockIsNotPickable()
    {
        var layers = new List<Layer> { new("0"), new("HIDDEN") { IsOn = false } };

        var block = new BlockDefinition("RING");
        block.Entities.Add(new SCircle(Vec2.Zero, 5) { LayerIndex = 1 });

        var insert = new SInsert(block, Mat3.Identity);

        Assert.Equal(double.PositiveInfinity,
            insert.DistanceTo(new Vec2(5, 0), new PickContext(Tolerance, layers)));
    }

    [Fact]
    public void AViewportIsPickedOnItsFrame()
    {
        var model = new Layout("Model", isPaperSpace: false);
        var viewport = new SViewport(model, Bounds2.FromCorners(new Vec2(0, 0), new Vec2(100, 60)),
            Vec2.Zero, viewHeight: 60);

        Assert.Equal(0.0, viewport.DistanceTo(new Vec2(0, 30), Context()), 9);
        Assert.Equal(2.0, viewport.DistanceTo(new Vec2(-2, 30), Context()), 9);

        // The middle of a viewport belongs to what it looks at, not to it.
        Assert.Equal(30.0, viewport.DistanceTo(new Vec2(50, 30), Context()), 9);
    }

    // ---- crossing rectangles --------------------------------------------

    [Theory]
    [InlineData(4, 4, 6, 6, true, "rectangle sitting on the line")]
    [InlineData(-5, -5, 15, 15, true, "rectangle swallowing the line")]
    [InlineData(2, 6, 4, 9, false, "rectangle above the line")]
    public void ALineCrossesARectangleOnlyWhenItReachesIt(
        double x0, double y0, double x1, double y1, bool expected, string what)
    {
        var line = new SLine(new Vec2(0, 0), new Vec2(10, 10));
        var rect = Bounds2.FromCorners(new Vec2(x0, y0), new Vec2(x1, y1));

        Assert.True(expected == line.IntersectsRect(rect, Context()),
            $"{what}: expected crossing to be {expected}");
    }

    [Fact]
    public void ARectangleInsideACircleDoesNotCrossIt()
    {
        var circle = new SCircle(Vec2.Zero, 10);

        // Wholly inside: it touches no part of what is drawn.
        Assert.False(circle.IntersectsRect(Bounds2.FromCorners(new Vec2(-2, -2), new Vec2(2, 2)), Context()));

        // Reaching across the rim.
        Assert.True(circle.IntersectsRect(Bounds2.FromCorners(new Vec2(8, -2), new Vec2(12, 2)), Context()));

        // Outside altogether.
        Assert.False(circle.IntersectsRect(Bounds2.FromCorners(new Vec2(20, 20), new Vec2(30, 30)), Context()));
    }

    [Fact]
    public void ARectangleInsideASolidHatchCrossesIt()
    {
        IReadOnlyList<Vec2> outer = [new(0, 0), new(20, 0), new(20, 20), new(0, 20)];
        var hatch = new SHatch([outer]) { IsSolid = true };

        // Unlike a circle, a filled region is drawn where the rectangle is.
        Assert.True(hatch.IntersectsRect(Bounds2.FromCorners(new Vec2(8, 8), new Vec2(12, 12)), Context()));
        Assert.False(hatch.IntersectsRect(Bounds2.FromCorners(new Vec2(30, 30), new Vec2(40, 40)), Context()));
    }

    [Fact]
    public void ABulgedSegmentCrossesWhereTheArcGoesNotWhereTheChordDoes()
    {
        var polyline = new SPolyline(
            [new PolyVertex(new Vec2(0, 0), 1), new PolyVertex(new Vec2(10, 0))], closed: false);

        // A box around the apex at (5,-5), which the chord never enters.
        Assert.True(polyline.IntersectsRect(Bounds2.FromCorners(new Vec2(4, -6), new Vec2(6, -4)), Context()));

        // A box between the chord and the arc, which neither touches.
        Assert.False(polyline.IntersectsRect(Bounds2.FromCorners(new Vec2(4, -2), new Vec2(6, -1)), Context()));
    }
}
