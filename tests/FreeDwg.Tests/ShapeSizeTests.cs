using System.Windows;
using FreeDwg.Core.Editing;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Snapping;
using FreeDwg.Core.Tools;
using FreeDwg.Tests.Rendering;
using FreeDWGEditorThatWorks.Controls;
using FreeDWGEditorThatWorks.ViewModels;
using SceneDrawing = FreeDwg.Core.Scene.Drawing;

namespace FreeDwg.Tests;

/// <summary>
/// Sizes typed for an ellipse, a regular polygon and a circle -- the
/// companions to <see cref="RectangleShapeTests"/>.
/// </summary>
/// <remarks>
/// The ellipse case worth knowing: typing the short axis longer than the
/// long one makes the other axis the major, and an elliptical arc's span is
/// measured from the major axis. Get the quarter turn wrong and the arc jumps
/// to a different stretch of the same curve with its size exactly right.
/// </remarks>
public sealed class ShapeSizeTests
{
    // ---- ellipse -----------------------------------------------------------

    [Fact]
    public void AnEllipseReadsAsItsFullWidthAndHeight()
    {
        var ellipse = new SEllipse(Vec2.Zero, new Vec2(30, 0), 0.5, 0, ArcMath.TwoPi);

        Assert.Equal(60, EllipseShape.Width(ellipse), 9);
        Assert.Equal(30, EllipseShape.Height(ellipse), 9);
    }

    [Fact]
    public void WidthIsTheHorizontalAxisEvenWhenItIsTheMinorOne()
    {
        // Tall: the major axis points up.
        var ellipse = new SEllipse(Vec2.Zero, new Vec2(0, 40), 0.25, 0, ArcMath.TwoPi);

        Assert.Equal(20, EllipseShape.Width(ellipse), 9);
        Assert.Equal(80, EllipseShape.Height(ellipse), 9);
    }

    [Fact]
    public void ResizingAnEllipseKeepsItsCentreAndItsRatioAtMostOne()
    {
        var ellipse = new SEllipse(new Vec2(5, 5), new Vec2(30, 0), 0.5, 0, ArcMath.TwoPi);

        // Taller than it is wide: the vertical axis becomes the major.
        Assert.True(EllipseShape.Resize(ellipse, width: null, height: 100));

        Assert.Equal(new Vec2(5, 5), ellipse.Center);
        Assert.True(ellipse.Ratio <= 1);
        Assert.Equal(60, EllipseShape.Width(ellipse), 9);
        Assert.Equal(100, EllipseShape.Height(ellipse), 9);
        Assert.Equal(60, ellipse.Bounds.Width, 6);
        Assert.Equal(100, ellipse.Bounds.Height, 6);
    }

    [Fact]
    public void AnEllipticalArcStaysOnTheSameStretchWhenItsAxesSwap()
    {
        var arc = new SEllipse(Vec2.Zero, new Vec2(30, 0), 0.5, 0.3, 1.2);
        Vec2 major = arc.MajorAxis, minor = EllipseMath.MinorAxis(arc.MajorAxis, arc.Ratio);

        Assert.True(EllipseShape.Resize(arc, width: 60, height: 100));

        // The same parameter on the stretched axes is where each end must be.
        var stretchedMinor = minor.Normalized() * 50;
        foreach (double t in new[] { 0.3, 1.5 })
        {
            var expected = major * Math.Cos(t) + stretchedMinor * Math.Sin(t);
            double t2 = t == 0.3 ? arc.StartParameter : arc.StartParameter + arc.Sweep;
            var actual = EllipseMath.PointAt(arc.Center, arc.MajorAxis, arc.Ratio, t2);
            Assert.True(Vec2.Distance(expected, actual) < 1e-9, $"{expected} vs {actual}");
        }
    }

    // ---- regular polygon ---------------------------------------------------

    private static SPolyline Hexagon(double radius, double turn = 0) =>
        new(Enumerable.Range(0, 6)
            .Select(i => new PolyVertex(new Vec2(10, 20) + new Vec2(Math.Cos(turn + i * Math.PI / 3), Math.Sin(turn + i * Math.PI / 3)) * radius))
            .ToArray(), closed: true);

    [Fact]
    public void AHexagonReadsAsItsSizes()
    {
        Assert.True(RegularPolygon.TryRead(Hexagon(10), out var polygon));

        Assert.Equal(6, polygon.Sides);
        Assert.Equal(new Vec2(10, 20).X, polygon.Center.X, 9);
        Assert.Equal(10, polygon.OuterRadius, 9);
        Assert.Equal(10, polygon.SideLength, 9);
        Assert.Equal(10 * Math.Sqrt(3) / 2, polygon.InnerRadius, 9);
    }

    [Fact]
    public void ResizingAPolygonScalesItAboutItsCentreAndKeepsItsTurn()
    {
        var hexagon = Hexagon(10, turn: 0.2);
        Assert.True(RegularPolygon.TryRead(hexagon, out var before));

        Assert.True(RegularPolygon.Resize(hexagon, before.RadiusForSide(25)));
        Assert.True(RegularPolygon.TryRead(hexagon, out var after));

        Assert.Equal(25, after.SideLength, 9);
        Assert.Equal(before.Center.X, after.Center.X, 9);
        Assert.Equal(before.Center.Y, after.Center.Y, 9);
        Assert.Equal(0.2, (hexagon.Vertices[0].Point - after.Center).Angle(), 9);
    }

    [Fact]
    public void OnlyARegularPolygonIsOne()
    {
        // Equal radii, unequal sides.
        var uneven = new SPolyline(
            [new(new(10, 0)), new(new(0, 10)), new(new(-10, 0)), new(new(0, -10)), new(new(7.0710678, -7.0710678))],
            closed: true);
        Assert.False(RegularPolygon.TryRead(uneven, out _));

        // A pentagram: equal sides on one circle, but it goes round twice.
        var star = new SPolyline(Enumerable.Range(0, 5)
            .Select(i => new PolyVertex(new Vec2(Math.Cos(i * 4 * Math.PI / 5), Math.Sin(i * 4 * Math.PI / 5)) * 10))
            .ToArray(), closed: true);
        Assert.False(RegularPolygon.TryRead(star, out _));
    }

    // ---- the panel ---------------------------------------------------------

    private static List<PropertyRow> RowsFor(SceneDrawing drawing, CadCanvas canvas, FreeDwg.Core.Scene.SceneEntity entity)
    {
        canvas.Commands!.Do(new FreeDwg.Core.Commands.AddEntities(drawing.ActiveLayout, entity));
        canvas.Selection.Set([entity]);
        return PropertySource.Build(canvas);
    }

    private static T OnCanvas<T>(Func<CadCanvas, SceneDrawing, T> body) => StaRenderer.OnSta(() =>
    {
        var canvas = new CadCanvas { Width = 800, Height = 600 };
        canvas.Measure(new Size(800, 600));
        canvas.Arrange(new Rect(0, 0, 800, 600));

        var drawing = SceneDrawing.CreateEmpty();
        canvas.Drawing = drawing;
        canvas.Snapping.Modes = SnapModes.None;
        return body(canvas, drawing);
    });

    [Fact]
    public void ACirclesDiameterCanBeTyped()
    {
        double radius = OnCanvas((canvas, drawing) =>
        {
            var rows = RowsFor(drawing, canvas, new SCircle(Vec2.Zero, 10));
            rows.Single(row => row.Name == "Diameter").Value = "50";
            return ((SCircle)drawing.Entities[0]).Radius;
        });

        Assert.Equal(25, radius, 9);
    }

    [Fact]
    public void AnEllipseAndAPolygonOfferTheirSizes()
    {
        var (ellipseRows, polygonRows, side) = OnCanvas((canvas, drawing) =>
        {
            var ellipse = RowsFor(drawing, canvas, new SEllipse(Vec2.Zero, new Vec2(30, 0), 0.5, 0, ArcMath.TwoPi))
                .Select(row => row.Name).ToList();

            var rows = RowsFor(drawing, canvas, Hexagon(10));
            var polygon = rows.Select(row => row.Name).ToList();
            rows.Single(row => row.Name == "Side length").Value = "4";

            RegularPolygon.TryRead((SPolyline)drawing.Entities[^1], out var resized);
            return (ellipse, polygon, resized.SideLength);
        });

        Assert.Contains("Width", ellipseRows);
        Assert.Contains("Height", ellipseRows);
        Assert.Contains("Sides", polygonRows);
        Assert.Contains("Radius to corners", polygonRows);
        Assert.DoesNotContain("Width", polygonRows);
        Assert.Equal(4, side, 9);
    }
}
