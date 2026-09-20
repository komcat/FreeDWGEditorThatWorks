using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Tests.Rendering;
using FreeDWGEditorThatWorks.Controls;
using SceneDrawing = FreeDwg.Core.Scene.Drawing;

namespace FreeDwg.Tests;

/// <summary>
/// That grips are actually painted, where the geometry says they should be.
/// </summary>
/// <remarks>
/// A grip is device-space decoration -- a square of a fixed pixel size,
/// drawn straight onto the drawing context rather than through the scene --
/// so pixels are the only place it exists at all. Nothing in the object
/// model would notice a square drawn at the wrong end of the world-to-device
/// transform, or one left behind at a position the entity has already left.
/// <para>
/// Every probe here is at the centre of the circle, which is the one point
/// the fixture guarantees nothing else reaches: the rim is drawn, the middle
/// of a circle is not.
/// </para>
/// </remarks>
public sealed class GripRenderTests
{
    private static readonly Vec2 Centre = new(140, 50);
    private const double Radius = 25;

    private static SceneDrawing Fixture(out SCircle circle)
    {
        var drawing = SceneDrawing.CreateEmpty();

        // A line well away from the circle, so the view has something to
        // frame besides the one entity under test.
        drawing.Add(drawing.Place(new SLine(new Vec2(10, 10), new Vec2(90, 10))));

        circle = drawing.Place(new SCircle(Centre, Radius));
        drawing.Add(circle);

        return drawing;
    }

    private static Probe Render(string saveAs, Action<CadCanvas, SCircle>? prepare = null)
    {
        var drawing = Fixture(out var circle);

        return new Probe(StaRenderer.Render(drawing, saveAs: saveAs,
            prepare: canvas => prepare?.Invoke(canvas, circle)));
    }

    [Fact]
    public void WithNothingSelectedTheMiddleOfACircleStaysEmpty()
    {
        // The baseline every other probe here is read against.
        Render("grips_none").AssertBlank(Centre.X, Centre.Y, "the centre of an unselected circle");
    }

    [Fact]
    public void SelectingACircleDrawsAHandleAtItsCentre()
    {
        var probe = Render("grips_shown", (canvas, circle) => canvas.Selection.Add(circle));

        var ink = probe.SampleNear(Centre);

        Assert.NotNull(ink);
        Assert.True(IsGripBlue(ink!.Value),
            $"the centre handle should paint in the grip colour, got {Describe(ink.Value)}");
    }

    [Fact]
    public void TheHeldHandleRidesWithTheCursorAndLeavesNothingBehind()
    {
        var dragged = new Vec2(140, 80);

        var probe = Render("grips_dragging", (canvas, circle) =>
        {
            canvas.Selection.Add(circle);
            Assert.True(canvas.BeginGripDrag(Centre));
            canvas.DragGripTo(dragged);
        });

        var held = probe.SampleNear(dragged);
        Assert.NotNull(held);
        Assert.True(IsHotGrip(held!.Value),
            $"the held handle should go warm, got {Describe(held.Value)}");

        // And no square left sitting at the position the drag has left,
        // which would say the object is in two places at once.
        probe.AssertBlank(Centre.X, Centre.Y, "the centre the grip was dragged off");
    }

    [Fact]
    public void AGripDragPreviewsWhereTheObjectWouldLand()
    {
        var dragged = new Vec2(140, 80);

        var probe = Render("grips_preview", (canvas, circle) =>
        {
            canvas.Selection.Add(circle);
            canvas.BeginGripDrag(Centre);
            canvas.DragGripTo(dragged);
        });

        // The bottom of the previewed circle, which falls inside the real one
        // where nothing is drawn -- so ink here is the preview or nothing.
        var ink = probe.SampleNear(new Vec2(dragged.X, dragged.Y - Radius));

        Assert.NotNull(ink);
        Assert.True(IsPreviewAmber(ink!.Value),
            $"the drag should preview the moved circle, got {Describe(ink.Value)}");
    }

    [Fact]
    public void AnAbandonedDragLeavesTheObjectAndItsHandleWhereTheyWere()
    {
        var probe = Render("grips_cancelled", (canvas, circle) =>
        {
            canvas.Selection.Add(circle);
            canvas.BeginGripDrag(Centre);
            canvas.DragGripTo(new Vec2(140, 80));
            canvas.CancelGripDrag();
        });

        var ink = probe.SampleNear(Centre);
        Assert.NotNull(ink);
        Assert.True(IsGripBlue(ink!.Value),
            $"a cancelled drag should put the handle back cold, got {Describe(ink.Value)}");

        probe.AssertBlank(140, 55, "where the abandoned preview used to be");
    }

    /// <summary>The grip colour, (90,175,255): the selection blue.</summary>
    private static bool IsGripBlue((byte R, byte G, byte B) pixel) =>
        pixel.B > 180 && pixel.B > pixel.G + 40 && pixel.G > pixel.R;

    // Warm against warm: the held grip is (255,120,80) and the preview is
    // (255,190,90), so the two are told apart by the ratio of green to red
    // and not by absolute values. A stroked curve is antialiased against the
    // background and arrives dimmed -- the hue survives that, the brightness
    // does not, and a threshold on the brightness would have both colours
    // passing both tests somewhere along the edge.

    /// <summary>The held grip, (255,120,80): green well under half of red.</summary>
    private static bool IsHotGrip((byte R, byte G, byte B) pixel) =>
        pixel.R > 120 && pixel.G * 100 < pixel.R * 60 && pixel.B <= pixel.G;

    /// <summary>The preview colour, (255,190,90): green around three quarters of red.</summary>
    private static bool IsPreviewAmber((byte R, byte G, byte B) pixel) =>
        pixel.R > 120 && pixel.G * 100 > pixel.R * 65 && pixel.B * 100 < pixel.R * 55;

    private static string Describe((byte R, byte G, byte B) pixel) =>
        $"({pixel.R},{pixel.G},{pixel.B})";
}
