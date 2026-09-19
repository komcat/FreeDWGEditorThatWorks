using FreeDwg.Core.Geometry;
using FreeDwg.Core.Picking;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;
using SceneDrawing = FreeDwg.Core.Scene.Drawing;

namespace FreeDwg.Tests;

/// <summary>
/// The spatial index, and what a click or a dragged rectangle takes.
/// </summary>
/// <remarks>
/// The index is checked against the linear scan it replaces rather than
/// against hand-written expectations: the property that matters is that
/// nothing changed except the cost.
/// </remarks>
public sealed class SelectionTests
{
    private const double Tolerance = 0.5;

    // ---- the index ------------------------------------------------------

    /// <summary>A spread of lines, circles and one entity with no bounds at all.</summary>
    private static List<SceneEntity> ScatteredScene(int count, int seed = 20260919)
    {
        var random = new Random(seed);
        var entities = new List<SceneEntity>(count);

        for (int i = 0; i < count; i++)
        {
            double x = random.NextDouble() * 1000;
            double y = random.NextDouble() * 1000;
            double size = random.NextDouble() * 40 + 0.5;

            entities.Add(random.Next(2) == 0
                ? new SLine(new Vec2(x, y), new Vec2(x + size, y + size / 3))
                : new SCircle(new Vec2(x, y), size));
        }

        // A degenerate entity: no vertices, so no bounds to index it by.
        entities.Add(new SPolyline([], closed: false));
        return entities;
    }

    [Theory]
    [InlineData(0, 0, 100, 100)]
    [InlineData(400, 400, 600, 600)]
    [InlineData(-500, -500, 1500, 1500)]
    [InlineData(999, 999, 1001, 1001)]
    public void TheIndexAgreesWithTheScanItReplaces(double x0, double y0, double x1, double y1)
    {
        var entities = ScatteredScene(400);
        var index = SpatialIndex.Build(entities);
        var area = Bounds2.FromCorners(new Vec2(x0, y0), new Vec2(x1, y1));

        var expected = new List<int>();
        for (int i = 0; i < entities.Count; i++)
        {
            var bounds = entities[i].Bounds;
            if (bounds.IsEmpty || bounds.Intersects(area)) expected.Add(i);
        }

        var actual = index.Query(area);
        actual.Sort();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void QueryingTheIndexKeepsPaintingOrderOnceSorted()
    {
        var entities = ScatteredScene(200);
        var index = SpatialIndex.Build(entities);

        var positions = index.Query(Bounds2.FromCorners(new Vec2(0, 0), new Vec2(1000, 1000)));
        positions.Sort();

        // Painting order is entity order, and a hatch drawn over a line is a
        // different picture from a line drawn over a hatch.
        for (int i = 1; i < positions.Count; i++)
            Assert.True(positions[i] > positions[i - 1], "positions must be strictly increasing");

        Assert.All(positions, position => Assert.Same(entities[position], index[position]));
    }

    [Fact]
    public void AnEntityWithNoBoundsIsAlwaysReturned()
    {
        var entities = new List<SceneEntity> { new SPolyline([], closed: false) };
        var index = SpatialIndex.Build(entities);

        // Nowhere near it, because it is nowhere.
        Assert.Single(index.Query(Bounds2.FromCorners(new Vec2(-9e9, -9e9), new Vec2(-8e9, -8e9))));
    }

    [Fact]
    public void AddingAnEntityRebuildsTheLayoutIndex()
    {
        var layout = new Layout("Model", isPaperSpace: false);
        layout.Add(new SLine(new Vec2(0, 0), new Vec2(10, 0)));

        Assert.Equal(1, layout.Index.Count);

        layout.Add(new SLine(new Vec2(0, 5), new Vec2(10, 5)));
        Assert.Equal(2, layout.Index.Count);
    }

    // ---- clicking -------------------------------------------------------

    private static SceneDrawing SmallScene(out SLine low, out SLine high, out SCircle circle)
    {
        var drawing = new SceneDrawing();
        drawing.AddLayer(new Layer("0"));

        low = new SLine(new Vec2(0, 0), new Vec2(100, 0));
        high = new SLine(new Vec2(0, 10), new Vec2(100, 10));
        circle = new SCircle(new Vec2(50, 50), 20);

        drawing.Add(low);
        drawing.Add(high);
        drawing.Add(circle);
        return drawing;
    }

    [Fact]
    public void AClickTakesTheNearestEntityWithinTolerance()
    {
        var drawing = SmallScene(out var low, out var high, out _);

        Assert.Same(low, Picker.At(drawing, new Vec2(50, 1), tolerance: 3));
        Assert.Same(high, Picker.At(drawing, new Vec2(50, 9), tolerance: 3));
        Assert.Null(Picker.At(drawing, new Vec2(50, 5), tolerance: 3));
    }

    [Fact]
    public void AClickWhereTwoEntitiesMeetTakesTheOneOnTop()
    {
        var drawing = new SceneDrawing();
        drawing.AddLayer(new Layer("0"));

        var under = new SLine(new Vec2(0, 0), new Vec2(100, 0));
        var over = new SLine(new Vec2(0, 0), new Vec2(100, 0));
        drawing.Add(under);
        drawing.Add(over);

        // Equidistant by construction: the tie has to go to what is visible.
        Assert.Same(over, Picker.At(drawing, new Vec2(50, 0), tolerance: 1));
    }

    [Fact]
    public void ALockedOrHiddenLayerCannotBePicked()
    {
        var drawing = new SceneDrawing();
        drawing.AddLayer(new Layer("0"));
        drawing.AddLayer(new Layer("LOCKED") { IsLocked = true });
        drawing.AddLayer(new Layer("OFF") { IsOn = false });

        drawing.Add(new SLine(new Vec2(0, 0), new Vec2(100, 0)) { LayerIndex = 1 });
        drawing.Add(new SLine(new Vec2(0, 10), new Vec2(100, 10)) { LayerIndex = 2 });

        Assert.Null(Picker.At(drawing, new Vec2(50, 0), tolerance: 3));
        Assert.Null(Picker.At(drawing, new Vec2(50, 10), tolerance: 3));
    }

    // ---- dragging -------------------------------------------------------

    [Fact]
    public void WindowSelectionTakesOnlyWhatIsWhollyEnclosed()
    {
        var drawing = SmallScene(out _, out _, out var circle);
        var rect = Bounds2.FromCorners(new Vec2(20, 20), new Vec2(80, 80));

        var taken = Picker.InRect(drawing, rect, SelectionMode.Window, Tolerance);

        // The circle fits; the two lines run out of both sides.
        Assert.Single(taken);
        Assert.Same(circle, taken[0]);
    }

    [Fact]
    public void CrossingSelectionTakesWhateverItTouches()
    {
        var drawing = SmallScene(out var low, out var high, out var circle);

        // Reaches the low line and the bottom of the circle, not the high one.
        var rect = Bounds2.FromCorners(new Vec2(40, -5), new Vec2(60, 2));
        var taken = Picker.InRect(drawing, rect, SelectionMode.Crossing, Tolerance);

        Assert.Same(low, Assert.Single(taken));

        rect = Bounds2.FromCorners(new Vec2(40, 25), new Vec2(60, 35));
        taken = Picker.InRect(drawing, rect, SelectionMode.Crossing, Tolerance);

        Assert.Same(circle, Assert.Single(taken));
        Assert.DoesNotContain(high, taken);
    }

    [Fact]
    public void SelectionResultsComeBackInPaintingOrder()
    {
        var drawing = SmallScene(out var low, out var high, out var circle);
        var everything = Bounds2.FromCorners(new Vec2(-50, -50), new Vec2(150, 150));

        var taken = Picker.InRect(drawing, everything, SelectionMode.Crossing, Tolerance);

        Assert.Equal<object>([low, high, circle], taken);
    }

    // ---- the selection set ----------------------------------------------

    [Fact]
    public void TheSelectionSetKeepsOrderAndRejectsDuplicates()
    {
        var a = new SLine(Vec2.Zero, new Vec2(1, 0));
        var b = new SLine(Vec2.Zero, new Vec2(0, 1));

        var selection = new Selection();
        int changes = 0;
        selection.Changed += (_, _) => changes++;

        Assert.True(selection.Add(a));
        Assert.False(selection.Add(a));
        Assert.True(selection.Add(b));

        Assert.Equal<object>([a, b], selection.Ordered);
        Assert.Equal(2, changes);

        Assert.False(selection.Toggle(a));
        Assert.Equal<object>([b], selection.Ordered);

        selection.Clear();
        Assert.True(selection.IsEmpty);
    }

    [Fact]
    public void ReplacingTheSelectionRaisesChangedOnce()
    {
        var entities = ScatteredScene(10).Take(5).ToList();

        var selection = new Selection();
        int changes = 0;
        selection.Changed += (_, _) => changes++;

        selection.Set(entities);

        Assert.Equal(5, selection.Count);
        Assert.Equal(1, changes);
    }
}
