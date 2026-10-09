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
/// A polyline that is a rectangle, given a width and a height to edit.
/// </summary>
/// <remarks>
/// The cases worth having are the ones where "width" could be read wrongly:
/// a rectangle drawn right to left, one whose vertices start up its side,
/// and one that is turned. Width is the side that looks like a width.
/// </remarks>
public sealed class RectangleShapeTests
{
    private static SPolyline Polyline(params Vec2[] points) =>
        new(points.Select(p => new PolyVertex(p)).ToArray(), closed: true);

    [Fact]
    public void ADrawnRectangleReadsAsItsWidthAndHeight()
    {
        var tool = new RectangleTool();
        tool.Click(new Vec2(10, 10));
        var rectangle = (SPolyline)tool.Click(new Vec2(60, 110))!;

        Assert.True(RectangleShape.TryRead(rectangle, out var shape));
        Assert.Equal(50, shape.Width, 9);
        Assert.Equal(100, shape.Height, 9);
    }

    [Fact]
    public void WidthIsTheHorizontalSideWhicheverWayTheVerticesRun()
    {
        // Up the left side first: the first edge is the height.
        var upFirst = Polyline(new(0, 0), new(0, 100), new(50, 100), new(50, 0));

        Assert.True(RectangleShape.TryRead(upFirst, out var shape));
        Assert.Equal(50, shape.Width, 9);
        Assert.Equal(100, shape.Height, 9);
    }

    [Fact]
    public void ResizingKeepsTheFirstCornerAndTheDirections()
    {
        // Drawn right to left and downwards from (60, 110).
        var rectangle = Polyline(new(60, 110), new(10, 110), new(10, 10), new(60, 10));

        Assert.True(RectangleShape.Resize(rectangle, width: 80, height: null));

        Assert.Equal(new Vec2(60, 110), rectangle.Vertices[0].Point);
        Assert.Equal(new Vec2(-20, 110), rectangle.Vertices[1].Point);
        Assert.Equal(new Vec2(-20, 10), rectangle.Vertices[2].Point);
        Assert.Equal(new Vec2(60, 10), rectangle.Vertices[3].Point);
        Assert.True(rectangle.Closed);
    }

    [Fact]
    public void ATurnedRectangleKeepsItsAngle()
    {
        double c = Math.Cos(Math.PI / 6), s = Math.Sin(Math.PI / 6);
        var along = new Vec2(c, s) * 40;
        var across = new Vec2(-s, c) * 20;
        var rectangle = Polyline(Vec2.Zero, along, along + across, across);

        Assert.True(RectangleShape.Resize(rectangle, width: null, height: 30));
        Assert.True(RectangleShape.TryRead(rectangle, out var shape));

        Assert.Equal(40, shape.Width, 9);
        Assert.Equal(30, shape.Height, 9);
        Assert.Equal(Math.PI / 6, shape.Along.Angle(), 9);
    }

    [Fact]
    public void OnlyARectangleIsOne()
    {
        // A parallelogram, an open run, a rounded corner, a triangle.
        Assert.False(RectangleShape.TryRead(Polyline(new(0, 0), new(50, 0), new(60, 20), new(10, 20)), out _));
        Assert.False(RectangleShape.TryRead(
            new SPolyline([new(new(0, 0)), new(new(50, 0)), new(new(50, 20)), new(new(0, 20))], closed: false), out _));
        Assert.False(RectangleShape.TryRead(
            new SPolyline([new(new(0, 0), 0.4), new(new(50, 0)), new(new(50, 20)), new(new(0, 20))], closed: true), out _));
        Assert.False(RectangleShape.TryRead(Polyline(new(0, 0), new(50, 0), new(0, 20)), out _));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void ASizeThatIsNotASizeIsRefused(double width)
    {
        var rectangle = Polyline(new(0, 0), new(50, 0), new(50, 20), new(0, 20));
        Assert.False(RectangleShape.Resize(rectangle, width, null));
    }

    [Fact]
    public void ThePanelOffersWidthAndHeightAndEditsThroughTheStack()
    {
        var (width, height, after, undone) = StaRenderer.OnSta(() =>
        {
            var canvas = new CadCanvas { Width = 800, Height = 600 };
            canvas.Measure(new Size(800, 600));
            canvas.Arrange(new Rect(0, 0, 800, 600));

            var drawing = SceneDrawing.CreateEmpty();
            canvas.Drawing = drawing;
            canvas.Snapping.Modes = SnapModes.None;

            canvas.UseTool(new RectangleTool());
            canvas.PlaceToolPoint(new Vec2(0, 0));
            canvas.PlaceToolPoint(new Vec2(50, 100));
            canvas.UseSelect();
            canvas.Selection.Set([drawing.Entities[0]]);

            var rows = PropertySource.Build(canvas);
            string width = rows.Single(row => row.Name == "Width").Value;
            string height = rows.Single(row => row.Name == "Height").Value;

            rows.Single(row => row.Name == "Width").Value = "75";
            var after = drawing.Entities[0].Bounds;

            canvas.Undo();
            return (width, height, after, drawing.Entities[0].Bounds);
        });

        Assert.Equal("50", width);
        Assert.Equal("100", height);
        Assert.Equal(75, after.Width, 9);
        Assert.Equal(100, after.Height, 9);
        Assert.Equal(50, undone.Width, 9);
    }
}
