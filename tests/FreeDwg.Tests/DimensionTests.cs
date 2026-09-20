using FreeDwg.Core.Editing;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Tools;

namespace FreeDwg.Tests;

/// <summary>
/// What a dimension measures, and where the marks that say so end up.
/// </summary>
/// <remarks>
/// Arithmetic, with no sink anywhere near it. A dimension is a dozen
/// coincidences that all have to agree -- the extension lines have to reach
/// the dimension line exactly, the arrow tips have to sit on its ends, and
/// the number has to be the distance the arrows actually span -- and every
/// one of those is a statement rather than a picture.
/// <para>
/// The case worth knowing is the difference between the two kinds. A
/// horizontal dimension across two points at different heights measures the
/// horizontal gap and <em>not</em> the distance between them; an aligned one
/// measures the distance. Both are correct and they are different numbers,
/// so a test that only ever used level points would not tell them apart.
/// </para>
/// </remarks>
public sealed class DimensionTests
{
    private static readonly DimensionStyle Millimetres = DimensionStyle.Iso;

    // ---- what it measures --------------------------------------------------

    [Fact]
    public void AHorizontalDimensionMeasuresTheHorizontalGap()
    {
        // Eighty across and thirty up: the two answers are 80 and 85.44.
        var dimension = new SDimension(DimensionKind.Linear,
            new Vec2(10, 10), new Vec2(90, 40), new Vec2(50, 60))
        { DimensionStyle = Millimetres };

        Assert.Equal(80, dimension.Solve().Measurement, 9);
        Assert.Equal("80", dimension.MeasurementText);
    }

    [Fact]
    public void AVerticalDimensionMeasuresTheOtherOne()
    {
        var dimension = new SDimension(DimensionKind.Linear,
            new Vec2(10, 10), new Vec2(90, 40), new Vec2(110, 25), Math.PI / 2)
        { DimensionStyle = Millimetres };

        Assert.Equal(30, dimension.Solve().Measurement, 9);
    }

    [Fact]
    public void AnAlignedDimensionMeasuresTheDistanceItself()
    {
        var dimension = new SDimension(DimensionKind.Aligned,
            new Vec2(0, 0), new Vec2(30, 40), new Vec2(-8, 6))
        { DimensionStyle = Millimetres };

        Assert.Equal(50, dimension.Solve().Measurement, 9);
    }

    [Fact]
    public void AnAlignedDimensionReadsItsDirectionOffItsOwnOrigins()
    {
        var dimension = new SDimension(DimensionKind.Aligned,
            new Vec2(0, 0), new Vec2(10, 0), new Vec2(5, 8), rotation: 0);

        // Dragging an origin turns it, where a linear one would keep the axis
        // it was given and start measuring a shadow of itself.
        dimension.Second = new Vec2(0, 10);

        Assert.Equal(Math.PI / 2, dimension.Direction, 9);
        Assert.Equal(10, dimension.Solve().Measurement, 9);
    }

    // ---- where the marks go ------------------------------------------------

    [Fact]
    public void TheDimensionLineRunsThroughThePickedPointAndSpansTheMeasurement()
    {
        var geometry = DimensionMath.Solve(
            new Vec2(10, 10), new Vec2(90, 40), new Vec2(50, 60), rotation: 0, Millimetres);

        // Both ends on the line the third point picked, and the span between
        // them is the measurement -- these are the same fact twice, which is
        // the point: the number is of the thing that is drawn.
        Assert.Equal(60, geometry.LineStart.Y, 9);
        Assert.Equal(60, geometry.LineEnd.Y, 9);
        Assert.Equal(10, geometry.LineStart.X, 9);
        Assert.Equal(90, geometry.LineEnd.X, 9);
        Assert.Equal(80, geometry.Measurement, 9);
    }

    [Fact]
    public void ExtensionLinesStartClearOfTheGeometryAndFinishPastTheLine()
    {
        var geometry = DimensionMath.Solve(
            new Vec2(10, 10), new Vec2(90, 40), new Vec2(50, 60), rotation: 0, Millimetres);

        // A gap at the object, so the dimension does not touch what it
        // measures, and an overshoot past the dimension line, which is what
        // makes the corner read as deliberate.
        Assert.Equal(10 + Millimetres.ExtensionOffset, geometry.FirstExtensionStart.Y, 9);
        Assert.Equal(60 + Millimetres.ExtensionBeyond, geometry.FirstExtensionEnd.Y, 9);

        Assert.Equal(40 + Millimetres.ExtensionOffset, geometry.SecondExtensionStart.Y, 9);
        Assert.Equal(60 + Millimetres.ExtensionBeyond, geometry.SecondExtensionEnd.Y, 9);

        // Straight up from the point each came from.
        Assert.Equal(10, geometry.FirstExtensionStart.X, 9);
        Assert.Equal(90, geometry.SecondExtensionStart.X, 9);
    }

    [Fact]
    public void AnExtensionLineReachesItsEndOfTheDimensionLineFromEitherSide()
    {
        // The dimension line at y=20 lies between the two origins: one above
        // it and one below. Each extension line has to travel its own way to
        // get there -- the second one downwards -- and a single shared
        // direction leaves it hanging in mid air, short of the line it exists
        // to meet.
        var geometry = DimensionMath.Solve(
            new Vec2(0, 0), new Vec2(50, 40), new Vec2(25, 20), rotation: 0, Millimetres);

        Assert.Equal(20, geometry.LineStart.Y, 9);

        // Each ends the same overshoot past the line, on the far side from
        // the point it came from.
        Assert.Equal(Millimetres.ExtensionBeyond, geometry.FirstExtensionEnd.Y - 20, 9);
        Assert.Equal(Millimetres.ExtensionBeyond, 20 - geometry.SecondExtensionEnd.Y, 9);

        // And each starts its gap clear of its own origin, travelling that way.
        Assert.Equal(Millimetres.ExtensionOffset, geometry.FirstExtensionStart.Y, 9);
        Assert.Equal(40 - Millimetres.ExtensionOffset, geometry.SecondExtensionStart.Y, 9);
    }

    [Fact]
    public void ADimensionPulledOutBelowRunsItsExtensionLinesDownAndKeepsItsNumberUp()
    {
        var geometry = DimensionMath.Solve(
            new Vec2(10, 10), new Vec2(90, 10), new Vec2(50, -20), rotation: 0, Millimetres);

        Assert.Equal(-20, geometry.LineStart.Y, 9);
        Assert.True(geometry.FirstExtensionEnd.Y < geometry.FirstExtensionStart.Y);

        // The number still sits on top of its own line. It is not put on the
        // far side from the geometry: a dimension under a part carries its
        // number above its line like every other one, because that is the
        // side it is read from.
        Assert.Equal(-20 + Millimetres.TextGap, geometry.TextAnchor.Y, 9);
    }

    [Fact]
    public void TheArrowTipsSitExactlyOnTheEndsOfTheDimensionLine()
    {
        var geometry = DimensionMath.Solve(
            new Vec2(10, 10), new Vec2(90, 10), new Vec2(50, 30), rotation: 0, Millimetres);

        var fit = DimensionMath.Fit(geometry, textWidth: 4, Millimetres);
        var arrows = DimensionMath.Arrowheads(geometry, fit, Millimetres);

        Assert.False(fit.ArrowsOutside);
        Assert.Equal(2, arrows.Count);
        Assert.Equal(geometry.LineStart, arrows[0][0]);
        Assert.Equal(geometry.LineEnd, arrows[1][0]);

        // Pointing inwards at each other, which is what says the measurement
        // is the gap between them rather than beyond them.
        Assert.True(arrows[0][1].X > geometry.LineStart.X);
        Assert.True(arrows[1][1].X < geometry.LineEnd.X);
    }

    // ---- fitting the marks into what is being measured ---------------------

    [Fact]
    public void ADimensionTooShortForItsArrowsTurnsThemOut()
    {
        // Five across, with two 2.5 arrows to put in it. Head to head they
        // meet exactly and read as one solid diamond rather than as a
        // measurement, which is what a small chamfer looked like.
        var geometry = DimensionMath.Solve(
            new Vec2(0, 0), new Vec2(5, 0), new Vec2(2.5, 10), rotation: 0, Millimetres);

        var fit = DimensionMath.Fit(geometry, textWidth: 1.6, Millimetres);
        Assert.True(fit.ArrowsOutside);

        var arrows = DimensionMath.Arrowheads(geometry, fit, Millimetres);

        // Still two of them, and their tips have not moved: the tip is the
        // point the measurement is of, whichever way the body faces. They
        // sit on the dimension line, ten out from the points measured.
        Assert.Equal(2, arrows.Count);
        Assert.Equal(new Vec2(0, 10), arrows[0][0]);
        Assert.Equal(new Vec2(5, 10), arrows[1][0]);

        // Bodies now outside, pointing back in at the ends.
        Assert.True(arrows[0][1].X < 0, "the first arrow should sit outside the measurement");
        Assert.True(arrows[1][1].X > 5, "the second one too");
    }

    [Fact]
    public void TurningTheArrowsOutGivesThemALineToSitOn()
    {
        var geometry = DimensionMath.Solve(
            new Vec2(0, 0), new Vec2(5, 0), new Vec2(2.5, 10), rotation: 0, Millimetres);

        var fit = DimensionMath.Fit(geometry, textWidth: 1.6, Millimetres);

        // The drawn line runs past both ends; the measured ends do not move,
        // because they are what the number is of.
        Assert.Equal(-5, fit.StrokeStart.X, 9);
        Assert.Equal(10, fit.StrokeEnd.X, 9);
        Assert.Equal(5, geometry.Measurement, 9);
    }

    [Fact]
    public void ANumberThatStillFitsStaysBetweenTheExtensionLines()
    {
        var geometry = DimensionMath.Solve(
            new Vec2(0, 0), new Vec2(5, 0), new Vec2(2.5, 10), rotation: 0, Millimetres);

        // The arrows are the ones that ran out of room; a short number in a
        // five-wide gap did not, and moving it as well would be answering a
        // question nobody asked.
        var fit = DimensionMath.Fit(geometry, textWidth: 1.6, Millimetres);

        Assert.True(fit.ArrowsOutside);
        Assert.False(fit.TextOutside);
        Assert.Equal(geometry.TextAnchor, fit.TextAnchor);
    }

    [Fact]
    public void ANumberTooWideForTheGapGoesOutsideIt()
    {
        var geometry = DimensionMath.Solve(
            new Vec2(0, 0), new Vec2(4, 0), new Vec2(2, 10), rotation: 0, Millimetres);

        var fit = DimensionMath.Fit(geometry, textWidth: 9, Millimetres);

        Assert.True(fit.TextOutside);

        // Past the end it reads towards, clear of the arrow out there, and
        // still on top of its own line.
        Assert.True(fit.TextAnchor.X > 4, "the number belongs past the end, not over the middle");
        Assert.Equal(10 + Millimetres.TextGap, fit.TextAnchor.Y, 9);
    }

    [Fact]
    public void AGenerousDimensionMovesNothing()
    {
        var geometry = DimensionMath.Solve(
            new Vec2(0, 0), new Vec2(80, 0), new Vec2(40, 20), rotation: 0, Millimetres);

        var fit = DimensionMath.Fit(geometry, textWidth: 5, Millimetres);

        Assert.False(fit.ArrowsOutside);
        Assert.False(fit.TextOutside);
        Assert.Equal(geometry.LineStart, fit.StrokeStart);
        Assert.Equal(geometry.LineEnd, fit.StrokeEnd);
    }

    [Fact]
    public void AVerticalDimensionTurnsItsArrowsOutAlongItsOwnAxis()
    {
        // The same decision, turned a quarter: the arrows have to go out
        // along the dimension line and not along X.
        var geometry = DimensionMath.Solve(
            new Vec2(0, 0), new Vec2(0, 5), new Vec2(10, 2.5), Math.PI / 2, Millimetres);

        var fit = DimensionMath.Fit(geometry, textWidth: 1.6, Millimetres);
        Assert.True(fit.ArrowsOutside);

        Assert.Equal(-5, fit.StrokeStart.Y, 9);
        Assert.Equal(10, fit.StrokeEnd.Y, 9);
        Assert.Equal(10, fit.StrokeStart.X, 9);
    }

    [Fact]
    public void ASmallDimensionOnAnEntityTurnsItsArrowsOutToo()
    {
        // End to end through the entity, since that is where the text width
        // actually comes from -- a number nobody passed in.
        var dimension = new SDimension(DimensionKind.Linear,
            new Vec2(0, 0), new Vec2(5, 0), new Vec2(2.5, 10))
        { DimensionStyle = Millimetres };

        var (_, fit) = dimension.Layout();

        Assert.True(fit.ArrowsOutside);
        Assert.False(fit.TextOutside);
    }

    [Theory]
    [InlineData(0, 10, 90, 10, "picked left to right")]
    [InlineData(90, 10, 0, 10, "picked right to left")]
    public void TheNumberReadsLeftToRightWhicheverWayItWasPicked(
        double x1, double y1, double x2, double y2, string what)
    {
        var geometry = DimensionMath.Solve(
            new Vec2(x1, y1), new Vec2(x2, y2), new Vec2(45, 30), rotation: 0, Millimetres);

        Assert.True(Math.Cos(geometry.TextRotation) > 0, $"{what}: the text is upside down");

        // And still above the line, which is the half that a bare flip of the
        // rotation would get wrong.
        Assert.True(geometry.TextAnchor.Y > geometry.LineStart.Y, $"{what}: the number fell below the line");
    }

    [Fact]
    public void AVerticalDimensionReadsUpThePageWithItsNumberToTheLeft()
    {
        var geometry = DimensionMath.Solve(
            new Vec2(10, 0), new Vec2(10, 50), new Vec2(40, 25), Math.PI / 2, Millimetres);

        // Turned a quarter, reading up, and "above the line" is then to the
        // left of it -- which is where a vertical dimension's number goes on
        // every drawing there has ever been.
        Assert.Equal(Math.PI / 2, geometry.TextRotation, 9);
        Assert.Equal(40 - Millimetres.TextGap, geometry.TextAnchor.X, 9);
        Assert.Equal(25, geometry.TextAnchor.Y, 9);
    }

    // ---- the number --------------------------------------------------------

    [Fact]
    public void TheNumberIsWrittenInTheDrawingsOwnUnitsAndPrecision()
    {
        var drawing = Drawing.CreateEmpty();
        drawing.Units = DrawingUnits.Metres;
        drawing.LinearPrecision = 3;

        var style = DimensionStyle.For(drawing);

        // ISO's 2.5 mm of text is 0.0025 of a metre, so the marks come out
        // the same size on paper rather than a thousand times too big.
        Assert.Equal(0.0025, style.TextHeight, 12);
        Assert.Equal(0.0025, style.ArrowSize, 12);

        var dimension = new SDimension(DimensionKind.Aligned,
            new Vec2(0, 0), new Vec2(1.23456, 0), new Vec2(0.5, 0.2))
        { DimensionStyle = style };

        Assert.Equal("1.235", dimension.MeasurementText);
    }

    [Fact]
    public void AnOverrideIsWrittenInsteadOfTheMeasurement()
    {
        var dimension = new SDimension(DimensionKind.Linear,
            new Vec2(0, 0), new Vec2(80, 0), new Vec2(40, 20))
        { TextOverride = "80 REF" };

        Assert.Equal("80 REF", dimension.MeasurementText);
    }

    // ---- moving it ---------------------------------------------------------

    [Fact]
    public void ScalingADimensionScalesItsMarksAndItsMeasurement()
    {
        var dimension = new SDimension(DimensionKind.Linear,
            new Vec2(0, 0), new Vec2(80, 0), new Vec2(40, 20))
        { DimensionStyle = Millimetres };

        dimension.Transform(Mat3.Scaling(2));

        Assert.Equal(160, dimension.Solve().Measurement, 9);

        // Marks sized for the drawing it came from would come out half size
        // in the one it landed in.
        Assert.Equal(5, dimension.DimensionStyle.TextHeight, 9);
        Assert.Equal(5, dimension.DimensionStyle.ArrowSize, 9);
    }

    [Fact]
    public void RotatingALinearDimensionCarriesItsAxisWithIt()
    {
        var dimension = new SDimension(DimensionKind.Linear,
            new Vec2(0, 0), new Vec2(80, 0), new Vec2(40, 20))
        { DimensionStyle = Millimetres };

        dimension.Transform(Mat3.RotationAbout(Math.PI / 2, Vec2.Zero));

        // Still measuring the same eighty: an axis left behind would have it
        // measuring the shadow of the thing it is attached to.
        Assert.Equal(80, dimension.Solve().Measurement, 9);
        Assert.Equal(Math.PI / 2, ArcMath.Normalize(dimension.Rotation), 9);
    }

    [Fact]
    public void ADimensionIsPickedByItsLineAndByItsNumber()
    {
        var dimension = new SDimension(DimensionKind.Linear,
            new Vec2(0, 0), new Vec2(80, 0), new Vec2(40, 20))
        { DimensionStyle = Millimetres };

        Assert.Equal(0, dimension.DistanceTo(new Vec2(40, 20), 0.5), 6);
        Assert.Equal(0, dimension.DistanceTo(new Vec2(40, 21.5), 0.5), 6);

        // Well clear of every part of it.
        Assert.True(dimension.DistanceTo(new Vec2(40, 40), 0.5) > 10);
    }

    // ---- grips -------------------------------------------------------------

    [Fact]
    public void ADimensionOffersItsThreePointsAsGrips()
    {
        var dimension = new SDimension(DimensionKind.Linear,
            new Vec2(0, 0), new Vec2(80, 0), new Vec2(40, 20))
        { DimensionStyle = Millimetres };

        var grips = new List<Grip>();
        dimension.CollectGrips(grips);

        Assert.Equal(3, grips.Count);
        Assert.Equal(new Vec2(0, 0), grips[0].Point);
        Assert.Equal(new Vec2(80, 0), grips[1].Point);
        Assert.Equal(new Vec2(40, 20), grips[2].Point);
    }

    [Fact]
    public void DraggingAnOriginGripReMeasuresIt()
    {
        var dimension = new SDimension(DimensionKind.Linear,
            new Vec2(0, 0), new Vec2(80, 0), new Vec2(40, 20))
        { DimensionStyle = Millimetres };

        var grips = new List<Grip>();
        dimension.CollectGrips(grips);

        Assert.True(dimension.MoveGrip(grips[1], new Vec2(120, 0)));

        Assert.Equal(120, dimension.Solve().Measurement, 9);
        Assert.Equal("120", dimension.MeasurementText);

        // And the bounds followed it, which is what MoveGrip's wrapper is for.
        Assert.Equal(120, dimension.Bounds.MaxX, 6);
    }

    [Fact]
    public void DraggingTheLineGripSlidesTheDimensionWithoutChangingTheNumber()
    {
        var dimension = new SDimension(DimensionKind.Linear,
            new Vec2(0, 0), new Vec2(80, 0), new Vec2(40, 20))
        { DimensionStyle = Millimetres };

        var grips = new List<Grip>();
        dimension.CollectGrips(grips);

        Assert.True(dimension.MoveGrip(grips[2], new Vec2(40, 55)));

        Assert.Equal(55, dimension.Solve().LineStart.Y, 9);
        Assert.Equal(80, dimension.Solve().Measurement, 9);
    }

    // ---- the tools ---------------------------------------------------------

    [Theory]
    [InlineData(0, 40, 0.0, "pulled up, so it measures across")]
    [InlineData(0, -40, 0.0, "pulled down, still across")]
    [InlineData(60, 0, Math.PI / 2, "pulled to the right, so it measures up")]
    [InlineData(-60, 5, Math.PI / 2, "pulled to the left, still up")]
    public void WhichAxisALinearDimensionTakesIsReadOffWhereTheLineWasPulled(
        double offsetX, double offsetY, double expected, string what)
    {
        var first = new Vec2(10, 10);
        var second = new Vec2(90, 40);
        var middle = Vec2.Lerp(first, second, 0.5);

        double axis = LinearDimensionTool.AxisFor(first, second,
            middle + new Vec2(offsetX, offsetY));

        Assert.True(Math.Abs(axis - expected) < 1e-9, what);
    }

    [Fact]
    public void ADimensionToolBuildsOnItsThirdPointWithTheSizesItWasGiven()
    {
        var tool = new LinearDimensionTool { Sizes = DimensionStyle.ForUnits(DrawingUnits.Metres, 3) };

        Assert.Null(tool.Click(new Vec2(0, 0)));
        Assert.Null(tool.Click(new Vec2(2, 0)));

        var dimension = Assert.IsType<SDimension>(tool.Click(new Vec2(1, 0.5)));

        Assert.Equal(2, dimension.Solve().Measurement, 9);
        Assert.Equal(0.0025, dimension.DimensionStyle.TextHeight, 12);

        // And it starts over, like every other tool here.
        Assert.False(tool.InProgress);
    }


    // ---- radius and diameter -----------------------------------------------

    /// <summary>A circle of radius 25 at the origin, measured to a point outside it.</summary>
    private static SDimension Radial(DimensionKind kind, Vec2 at) =>
        new(kind, Vec2.Zero, new Vec2(25, 0), at) { DimensionStyle = Millimetres };

    [Fact]
    public void ARadiusMeasuresTheCircleAndADiameterMeasuresTwiceIt()
    {
        var radius = Radial(DimensionKind.Radius, new Vec2(40, 40));
        var diameter = Radial(DimensionKind.Diameter, new Vec2(40, 40));

        Assert.Equal(25, radius.Solve().Measurement, 9);
        Assert.Equal(50, diameter.Solve().Measurement, 9);
    }

    [Fact]
    public void ARadialNumberCarriesTheMarkThatSaysWhichItIs()
    {
        // A bare 25 against a circle could be either of two things, and a
        // drawing that leaves that open is one that gets made twice.
        Assert.Equal("R25", Radial(DimensionKind.Radius, new Vec2(40, 0)).MeasurementText);
        Assert.Equal("\u00d850", Radial(DimensionKind.Diameter, new Vec2(40, 0)).MeasurementText);
    }

    [Fact]
    public void TheArrowSlidesRoundTheRimToFaceWhereTheNumberWasDropped()
    {
        // Straight up, so the arrow belongs at the top of the circle rather
        // than wherever the rim point happened to be recorded.
        var geometry = Radial(DimensionKind.Radius, new Vec2(0, 60)).Solve();

        Assert.Equal(0, geometry.LineEnd.X, 9);
        Assert.Equal(25, geometry.LineEnd.Y, 9);

        // And a radius runs out from the centre, not across.
        Assert.Equal(Vec2.Zero, geometry.LineStart);
    }

    [Fact]
    public void ADiameterRunsRightAcrossTheCircle()
    {
        var geometry = Radial(DimensionKind.Diameter, new Vec2(0, 60)).Solve();

        Assert.Equal(25, geometry.LineEnd.Y, 9);
        Assert.Equal(-25, geometry.LineStart.Y, 9);
        Assert.Equal(50, geometry.Measurement, 9);
    }

    [Fact]
    public void ARadiusGetsOneArrowheadAndADiameterGetsTwo()
    {
        var radius = Radial(DimensionKind.Radius, new Vec2(40, 0));
        var diameter = Radial(DimensionKind.Diameter, new Vec2(40, 0));

        var one = DimensionMath.LeaderArrowheads(radius.Solve(), across: false, Millimetres);
        var two = DimensionMath.LeaderArrowheads(diameter.Solve(), across: true, Millimetres);

        Assert.Single(one);
        Assert.Equal(2, two.Count);

        // The tip sits on the rim, which is the point being measured to.
        Assert.Equal(new Vec2(25, 0), one[0][0]);
    }

    [Fact]
    public void ARadialDimensionOffersItsCentreToMoveAndItsTextToSwing()
    {
        var dimension = Radial(DimensionKind.Radius, new Vec2(40, 40));

        var grips = new List<Grip>();
        dimension.CollectGrips(grips);

        Assert.Equal(2, grips.Count);
        Assert.Equal(GripRole.Move, grips[0].Role);
        Assert.Equal(new Vec2(40, 40), grips[1].Point);

        // Dragging the text swings the leader round without changing the
        // number: a dimension is not where you resize what it measures.
        Assert.True(dimension.MoveGrip(grips[1], new Vec2(-60, 0)));
        Assert.Equal(25, dimension.Solve().Measurement, 9);
        Assert.Equal(-25, dimension.Solve().LineEnd.X, 9);
    }

    [Fact]
    public void ARadiusDimensionIsPickedByItsLeader()
    {
        var dimension = Radial(DimensionKind.Radius, new Vec2(50, 0));

        // On the leader, out past the rim.
        Assert.Equal(0, dimension.DistanceTo(new Vec2(40, 0), 0.5), 6);

        // And well off it.
        Assert.True(dimension.DistanceTo(new Vec2(40, 30), 0.5) > 5);
    }

    [Fact]
    public void ACircleDimensionToolTakesTheCircleAndThenOnePoint()
    {
        var tool = new RadiusDimensionTool { Sizes = Millimetres };

        Assert.True(tool.WantsEntity);
        Assert.False(tool.Take(new SLine(Vec2.Zero, new Vec2(10, 0))));
        Assert.True(tool.WantsEntity);

        Assert.True(tool.Take(new SCircle(new Vec2(10, 10), 4)));
        Assert.False(tool.WantsEntity);

        var dimension = Assert.IsType<SDimension>(tool.Click(new Vec2(20, 20)));

        Assert.Equal(DimensionKind.Radius, dimension.Kind);
        Assert.Equal(4, dimension.Solve().Measurement, 9);

        // And it wants another circle rather than quietly measuring the last
        // one again.
        Assert.True(tool.WantsEntity);
    }

    [Fact]
    public void ACircleDimensionToolTakesAnArcToo()
    {
        var tool = new DiameterDimensionTool();

        Assert.True(tool.Take(new SArc(new Vec2(5, 5), 12, 0, Math.PI)));

        var dimension = Assert.IsType<SDimension>(tool.Click(new Vec2(30, 30)));
        Assert.Equal(24, dimension.Solve().Measurement, 9);
    }

    [Fact]
    public void TwoPointsOnTopOfEachOtherAreNotADimension()
    {
        var tool = new AlignedDimensionTool();

        tool.Click(new Vec2(5, 5));
        tool.Click(new Vec2(5, 5));

        Assert.Null(tool.Click(new Vec2(5, 20)));
    }
}
