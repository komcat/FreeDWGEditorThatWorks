# FreeDwg.Tests

```
dotnet test
```

Two kinds of test live here.

**`IconTests`** parses the shell's icon dictionary, checks every icon has
ink and stays inside its 24x24 grid, and cross-checks the keys against the
references in `MainWindow.xaml` in both directions. A `StaticResource` that
resolves to nothing stops the app from starting, and nothing else here builds
a window to find out. With `FREEDWG_TEST_RENDERS` set it also writes
`icons.png`, a labelled contact sheet — which is how to tell an icon that
parses from an icon that reads.

**`StartupTests`** launches the real executable and waits for a window. It
is the only test that builds one, and it exists because a `StaticResource`
that resolves to nothing, or a handler that fires while the XAML is still
being parsed and reaches for an element further down the file, compiles
clean, passes everything else here, and kills the app before it draws a
pixel. Waiting for the process to *exit* is not enough: an unhandled
exception on the UI thread can leave it alive under Windows error reporting,
so a crash would look like a pass.

**`IntersectionTests` and `EditingTests`** cover where curves cross and what
trim, extend, fillet and chamfer make of it. Every crossing is asserted to
full precision, because "near enough" is the failure being guarded against,
not an acceptable result.

**`TransformTests`** covers moving entities and the modify tools. The case
worth knowing is the mirrored arc: its endpoints and its bounds are identical
whether or not the sweep was reversed, so the model-level test asserts the
sign of the sweep and `DrawingRenderTests` checks the pixels as well. This is
the third time that trap has been paid for.

**`SnapTests`** asserts exact equality on snapped points rather than
nearness, which is the whole point: two lines that meet to within a pixel are
two lines that do not meet.

**`CommandTests` and `ToolTests`** are arithmetic too: what each tool builds
from the points it is given, and that undo puts a drawing back exactly,
entity order included. `CanvasTests` then drives the real `CadCanvas` through
draw, select, erase and undo using its world-coordinate entry points — not
synthesised mouse messages, which WPF ignores in favour of the live mouse
device, and which would move the real cursor on whoever is at the machine.

**`GripTests`** is arithmetic too: what each entity offers as handles and
what dragging one does to it. Two cases there are worth knowing. The arc
re-solves through three points, so the sweep sign is the whole answer and is
invisible in its endpoints and its bounds -- the same trap as the mirrored
arc, from the other direction. And a polyline's bulge is a *signed* sweep:
positive is counter-clockwise, so the arc from (10,0) to (20,0) with bulge 1
goes round the bottom, which is the expectation this suite got wrong first.

**`GripRenderTests`** then checks the squares are painted, because a grip is
device-space decoration and pixels are the only place it exists. Every probe
there is at the centre of a circle: the rim is drawn and the middle is not,
so ink there can only be a handle. The held grip and the drag preview are
both warm colours and are told apart by the ratio of green to red, since a
stroked curve arrives antialiased and any brightness threshold would have
each of them passing the other's test somewhere along the edge.

**`DimensionTests`** is arithmetic about a shape that is a dozen
coincidences at once: the extension lines have to reach the dimension line
exactly, the arrow tips have to sit on its ends, and the number has to be the
distance the arrows actually span. Two cases earn their place. A horizontal
dimension across two points at *different heights* measures the horizontal
gap and not the distance between them -- a fixture with level points cannot
tell linear from aligned. And the dimension line can lie between the two
origins, which is the case that caught a real bug: each extension line has to
travel its own way to reach it, and a shared direction left one of them
hanging short of the line it exists to meet.

**`DimensionRenderTests`** then looks at the pixels, because a dimension is
the first entity here that draws three ways at once -- stroked lines, a
filled arrowhead and a block of text -- and nothing in the object model says
whether all three arrive. The arrowhead and the extension-line gap are
smaller than the three pixels a probe looks around itself, so those two
render the fixture again framed on one corner of it; probing them at the
fixture's own zoom would pass or fail on the neighbouring ink.

The fillet cases in `EditingTests` now include a polyline's own corner. The
one worth knowing is the arc's direction: the same two tangent points on the
same circle swept the other way have identical vertices and identical
bounds, so only `DrawingRenderTests` can tell you whether the corner was
rounded or bulged out of the shape. That is the third time this suite has
paid for arc winding.

The fit cases are worth reading before changing how a dimension lays itself
out: a five-wide dimension is exactly two arrowheads across, and it is the
case a small chamfer produces. `DimensionRenderTests` checks the middle of
that one is clear, with a probe placed off the line by more than half its
width and less than half an arrowhead -- so it is blank only because the
arrows really have gone outside.

**`LayerTests`** covers making, deleting and editing layers. Nearly all of it
is about one fact: an entity names its layer by *position*, so deleting one
from the middle leaves every entity past it pointing at its neighbour -- in
every layout, inside every block definition, and in every viewport's frozen
set. That is a bug which draws an ordinary drawing in the wrong colours and
hides the wrong geometry in one viewport only.

**`GeometryTests`, `PickingTests` and `SelectionTests`** are ordinary
arithmetic: de Boor against the Bézier a clamped cubic reduces to, bulge
sagitta, arc and ellipse bounds, matrix composition order and inversion,
camera round-trips, and what each entity considers a hit. No WPF, no files.

The picking tests state distances against the curve itself, never against a
flattening of it, and the spatial index is checked against the linear scan it
replaced rather than against hand-written expectations — the property that
matters there is that nothing changed except the cost.

**Everything else renders.** Each scenario writes a DWG with ACadSharp, reads
it back through `DwgLoader`, draws it off screen through the real `CadCanvas`
and `WpfDrawingSink`, and then asks the resulting pixels what is at a given
point *in drawing coordinates*.

That roundabout route is deliberate. Several of the bugs this suite exists to
catch were invisible to the object model and only showed up in pixels:

- arc sweep direction inverted, which turned every bulge inside out while
  endpoints and bounds stayed correct;
- a mirrored block flipping that sweep back again;
- dimensions losing their arrowheads, because an arrowhead is a `SOLID`;
- a selected block highlighting its own outline but not the geometry
  inside it, which is what `SelectionRenderTests` watches for;
- a grip square left behind at the position a drag has already left, which
  would offer an edit on geometry that is no longer there;
- an edit that leaves the layout's bounds or its spatial index stale, which
  passes every model-level test and then draws nothing at all. That is what
  `DrawingRenderTests` is for: it builds a drawing by clicking, the way the
  editor does, and then looks at the pixels.

## Fixtures

Fixture drawings are generated, not committed, so no binary DWGs live in the
repository and every render test doubles as a write-then-read round trip.
They are written once per test run into the temp directory, and each scenario
renders once and shares the frame across its cases via `IClassFixture`.

## When a probe fails

The recurring mistake is a probe placed where some *other* entity also passes.
Such a probe passes for the wrong reason and quietly stops testing anything —
in the blocks fixture, six probes aimed at a ring were sitting on the cross
arms, and three of them were "passing".

So before assuming the renderer is wrong, look at what is actually drawn:

```
set FREEDWG_TEST_RENDERS=C:\temp\renders
dotnet test
```

Each scenario then writes a PNG there. `Probe.Dump` prints an ASCII map of the
ink over a world-space window, which is usually quicker than opening the image:

```csharp
Console.WriteLine(fixture.Probe.Dump(center: new Vec2(30, 80), halfWidth: 18, halfHeight: 9));
```

Prefer probe points that only the geometry under test can reach. Where a
block puts geometry on the axes, take ring and curve probes off-axis at 45°.
