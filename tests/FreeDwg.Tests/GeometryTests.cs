using FreeDwg.Core.Geometry;
using FreeDwg.Core.Rendering;

namespace FreeDwg.Tests;

/// <summary>
/// Numeric checks on the geometry Core computes for itself. The render tests
/// can only show that a curve looks right at a handful of points; these pin
/// down the arithmetic behind it.
/// </summary>
public sealed class GeometryTests
{
    private const double Tolerance = 1e-9;

    // ---- B-splines ------------------------------------------------------

    private static readonly Vec2[] CubicControlPoints =
    [
        new(10, 60), new(20, 90), new(40, 90), new(50, 60),
    ];

    private static readonly double[] ClampedKnots = [0, 0, 0, 0, 1, 1, 1, 1];

    /// <summary>A clamped cubic with four control points is a plain Bezier.</summary>
    private static Vec2 Bezier(double t)
    {
        double u = 1 - t;
        return CubicControlPoints[0] * (u * u * u)
             + CubicControlPoints[1] * (3 * u * u * t)
             + CubicControlPoints[2] * (3 * u * t * t)
             + CubicControlPoints[3] * (t * t * t);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void DeBoorAgreesWithTheBezierItReducesTo(double t)
    {
        var actual = BSpline.Evaluate(CubicControlPoints, null, ClampedKnots, degree: 3, t);
        var expected = Bezier(t);

        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
    }

    [Fact]
    public void AClampedSplineStartsAndEndsOnItsOuterControlPoints()
    {
        var start = BSpline.Evaluate(CubicControlPoints, null, ClampedKnots, 3, 0);
        var end = BSpline.Evaluate(CubicControlPoints, null, ClampedKnots, 3, 1);

        Assert.Equal(CubicControlPoints[0], start);
        Assert.Equal(CubicControlPoints[^1], end);
    }

    [Fact]
    public void AKnotVectorOfTheWrongLengthIsRejected()
    {
        Assert.True(BSpline.IsValid(controlPointCount: 4, knotCount: 8, degree: 3));
        Assert.False(BSpline.IsValid(controlPointCount: 4, knotCount: 7, degree: 3));
        Assert.False(BSpline.IsValid(controlPointCount: 3, knotCount: 7, degree: 3));
    }

    [Fact]
    public void AnUnevaluableSplineFallsBackToItsControlPolygon()
    {
        double[] wrong = [0, 0, 1, 1];
        var points = BSpline.Tessellate(CubicControlPoints, null, wrong, 3, pixelsPerUnit: 10);

        Assert.Equal(CubicControlPoints, points);
    }

    [Fact]
    public void TessellationGetsFinerAsTheViewZoomsIn()
    {
        int coarse = BSpline.Tessellate(CubicControlPoints, null, ClampedKnots, 3, 1).Length;
        int fine = BSpline.Tessellate(CubicControlPoints, null, ClampedKnots, 3, 100).Length;

        Assert.True(fine > coarse, $"expected more samples when zoomed in: {fine} vs {coarse}");
    }

    // ---- bulges ----------------------------------------------------------

    [Fact]
    public void ABulgeOfOneIsASemicircleCentredOnItsChord()
    {
        var (center, radius, _, sweep) = ArcMath.FromBulge(new Vec2(0, 0), new Vec2(10, 0), 1.0);

        Assert.Equal(5, center.X, 9);
        Assert.Equal(0, center.Y, 9);
        Assert.Equal(5, radius, 9);
        Assert.Equal(Math.PI, sweep, 9);
    }

    [Fact]
    public void ANegativeBulgeSweepsTheOtherWay()
    {
        var (_, _, _, positive) = ArcMath.FromBulge(new Vec2(0, 0), new Vec2(10, 0), 0.5);
        var (_, _, _, negative) = ArcMath.FromBulge(new Vec2(0, 0), new Vec2(10, 0), -0.5);

        Assert.True(positive > 0);
        Assert.True(negative < 0);
        Assert.Equal(positive, -negative, 9);
    }

    [Fact]
    public void TheSagittaOfABulgeIsHalfTheChordTimesTheBulge()
    {
        // The wave in the primitives fixture depends on this exactly.
        var (center, radius, _, _) = ArcMath.FromBulge(new Vec2(20, 100), new Vec2(70, 100), 0.6);
        double lowest = center.Y - radius;

        Assert.Equal(85, lowest, 9);
    }

    [Fact]
    public void AnArcCoversTheAxisExtremesItSweepsThrough()
    {
        // 315 to 45 degrees about the origin: crosses 0, so it reaches +r in x.
        var bounds = ArcMath.Bounds(Vec2.Zero, 10, Math.PI * 7 / 4, Math.PI / 2);

        Assert.Equal(10, bounds.MaxX, 9);
        Assert.Equal(-7.0710678, bounds.MinY, 6);
        Assert.Equal(7.0710678, bounds.MaxY, 6);

        // The same sweep never reaches -r, which a naive box would assume.
        Assert.True(bounds.MinX > 7.07, $"minX should stay near the chord, got {bounds.MinX}");
    }

    // ---- ellipses ---------------------------------------------------------

    [Fact]
    public void AnEllipseStandingOnEndIsTallerThanItIsWide()
    {
        // Major axis along +Y, ratio 0.5: rx 7.5, ry 15 about (90,30).
        var bounds = EllipseMath.Bounds(new Vec2(90, 30), new Vec2(0, 15), 0.5, 0, ArcMath.TwoPi);

        Assert.Equal(82.5, bounds.MinX, 6);
        Assert.Equal(97.5, bounds.MaxX, 6);
        Assert.Equal(15, bounds.MinY, 6);
        Assert.Equal(45, bounds.MaxY, 6);
    }

    [Fact]
    public void ARotatedEllipseReachesBeyondItsAxisEndpoints()
    {
        // At 45 degrees the extremes fall between the axis ends, so sampling
        // only the quadrant angles would under-report the box.
        var major = new Vec2(10, 10);
        var bounds = EllipseMath.Bounds(Vec2.Zero, major, 0.5, 0, ArcMath.TwoPi);

        Assert.True(bounds.MaxX > 10, $"expected more than the major axis endpoint, got {bounds.MaxX}");
        Assert.Equal(bounds.MaxX, -bounds.MinX, 9);
        Assert.Equal(bounds.MaxY, -bounds.MinY, 9);
    }

    // ---- transforms --------------------------------------------------------

    [Fact]
    public void MatricesComposeInApplicationOrder()
    {
        var move = Mat3.Translation(10, 0);
        var turn = Mat3.Rotation(Math.PI / 2);

        // Move then turn is not the same as turn then move.
        var movedThenTurned = (move * turn).Transform(Vec2.Zero);
        var turnedThenMoved = (turn * move).Transform(Vec2.Zero);

        Assert.Equal(0, movedThenTurned.X, 9);
        Assert.Equal(10, movedThenTurned.Y, 9);
        Assert.Equal(10, turnedThenMoved.X, 9);
        Assert.Equal(0, turnedThenMoved.Y, 9);
    }

    [Fact]
    public void InvertingATransformRoundTrips()
    {
        var transform = Mat3.Translation(-3, 7) * Mat3.Rotation(0.4) * Mat3.Scaling(2, 5);

        Assert.True(transform.TryInvert(out var inverse));

        var point = new Vec2(13, -4);
        var roundTripped = inverse.Transform(transform.Transform(point));

        Assert.Equal(point.X, roundTripped.X, 9);
        Assert.Equal(point.Y, roundTripped.Y, 9);
    }

    [Fact]
    public void ADegenerateTransformCannotBeInverted() =>
        Assert.False(Mat3.Scaling(1, 0).TryInvert(out _));

    [Fact]
    public void AMirrorHasANegativeDeterminant()
    {
        Assert.True(Mat3.Scaling(-1, 1).Determinant < 0);
        Assert.True(Mat3.Scaling(2, 2).Determinant > 0);

        // Two mirrors cancel, which is why arc winding has to follow the sign
        // of the whole composed transform rather than any one step.
        Assert.True((Mat3.Scaling(-1, 1) * Mat3.Scaling(1, -1)).Determinant > 0);
    }

    [Fact]
    public void RotationIsASimilarityButANonUniformScaleIsNot()
    {
        Assert.True(Mat3.Rotation(0.3).IsSimilarity);
        Assert.True((Mat3.Rotation(0.3) * Mat3.Scaling(4)).IsSimilarity);
        Assert.True(Mat3.Scaling(-1, 1).IsSimilarity);
        Assert.False(Mat3.Scaling(3, 1).IsSimilarity);
    }

    [Fact]
    public void TransformedBoundsFollowTheCornersNotTheExtremes()
    {
        var unit = Bounds2.FromCorners(Vec2.Zero, new Vec2(1, 1));
        var rotated = Mat3.Rotation(Math.PI / 4).TransformBounds(unit);

        // A unit square turned 45 degrees spans the diagonal, not the side.
        Assert.Equal(Math.Sqrt(2), rotated.Height, 9);
    }

    // ---- bounds -------------------------------------------------------------

    [Fact]
    public void TheEmptyBoxIsTheIdentityForUnion()
    {
        var box = Bounds2.FromCorners(new Vec2(1, 2), new Vec2(3, 4));

        Assert.Equal(box.Min, Bounds2.Empty.Union(box).Min);
        Assert.Equal(box.Max, box.Union(Bounds2.Empty).Max);
        Assert.True(Bounds2.Empty.Union(Bounds2.Empty).IsEmpty);
    }

    [Fact]
    public void AnEmptyBoxIntersectsNothing()
    {
        var box = Bounds2.FromCorners(Vec2.Zero, new Vec2(10, 10));

        Assert.False(Bounds2.Empty.Intersects(box));
        Assert.False(box.Intersects(Bounds2.Empty));
        Assert.True(box.Intersects(box));
    }

    [Fact]
    public void TouchingBoxesCount()
    {
        var left = Bounds2.FromCorners(Vec2.Zero, new Vec2(10, 10));
        var right = Bounds2.FromCorners(new Vec2(10, 0), new Vec2(20, 10));

        // Culling must not drop geometry that only grazes the viewport edge.
        Assert.True(left.Intersects(right));
    }

    // ---- camera ---------------------------------------------------------------

    [Fact]
    public void ScreenAndWorldRoundTrip()
    {
        var camera = new Camera { ViewportWidth = 800, ViewportHeight = 600, Center = new Vec2(5, -5), Scale = 3 };

        var world = new Vec2(12.5, 4.25);
        var roundTripped = camera.ScreenToWorld(camera.WorldToScreen(world));

        Assert.Equal(world.X, roundTripped.X, 9);
        Assert.Equal(world.Y, roundTripped.Y, 9);
    }

    [Fact]
    public void TheWorldIsYUpAndTheScreenIsYDown()
    {
        var camera = new Camera { ViewportWidth = 800, ViewportHeight = 600, Scale = 1 };

        Assert.True(camera.WorldToScreen(new Vec2(0, 10)).Y < camera.WorldToScreen(new Vec2(0, 0)).Y);
    }

    [Fact]
    public void ZoomingAtAPointKeepsThatPointStill()
    {
        var camera = new Camera { ViewportWidth = 800, ViewportHeight = 600, Scale = 1 };
        var cursor = new Vec2(200, 150);

        var before = camera.ScreenToWorld(cursor);
        camera.ZoomAtScreenPoint(cursor, 2.5);
        var after = camera.ScreenToWorld(cursor);

        Assert.Equal(before.X, after.X, 9);
        Assert.Equal(before.Y, after.Y, 9);
    }

    [Fact]
    public void ZoomToFitFramesTheBoundsWithoutCroppingThem()
    {
        var camera = new Camera { ViewportWidth = 800, ViewportHeight = 600 };
        var content = Bounds2.FromCorners(new Vec2(-50, 20), new Vec2(150, 80));

        camera.ZoomToFit(content);
        var visible = camera.VisibleWorldBounds;

        Assert.Equal(content.Center.X, camera.Center.X, 9);
        Assert.Equal(content.Center.Y, camera.Center.Y, 9);
        Assert.True(visible.MinX <= content.MinX && visible.MaxX >= content.MaxX);
        Assert.True(visible.MinY <= content.MinY && visible.MaxY >= content.MaxY);
    }

    [Fact]
    public void ZoomToFitSurvivesADegenerateDrawing()
    {
        var camera = new Camera { ViewportWidth = 800, ViewportHeight = 600 };

        // A single point, and a drawing that is one horizontal line.
        camera.ZoomToFit(Bounds2.FromPoint(new Vec2(3, 4)));
        Assert.True(double.IsFinite(camera.Scale) && camera.Scale > 0);

        camera.ZoomToFit(Bounds2.FromCorners(new Vec2(0, 0), new Vec2(100, 0)));
        Assert.True(double.IsFinite(camera.Scale) && camera.Scale > 0);

        camera.ZoomToFit(Bounds2.Empty);
        Assert.True(double.IsFinite(camera.Scale) && camera.Scale > 0);
    }
}
