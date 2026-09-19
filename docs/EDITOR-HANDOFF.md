# Handoff: the reader is done, the editor is under way

Written at commit `1325ee0` and updated as the editor milestones land.
Everything below is either in the repo or in the commit messages; this is the
map, not a second copy.

## Where things stand

The reader is complete: M1–M5 of the original plan. E1 has landed on top of
it. 165 tests pass in about half a second.

| | | |
|---|---|---|
| M1 | `0b8b13a` | load a DWG and render it |
| M2 | `e11b3fb` | blocks, layers panel |
| M3 | `e1a6430` | text, linetypes, lineweights |
| M4 | `25bc589` | splines, ellipses, hatches, dimensions |
| M5 | `6b2495d` | paper space, viewports |
| — | `1325ee0` | the render harness became `tests/` |
| E1 | — | selection, hit testing and the spatial index |
| — | — | icon toolbar and tool palette |
| E2 | — | command stack, undo/redo, change log |
| E4 | — | new documents and six draw tools |
| E4 | — | object snap, ortho, grid; one canvas mode |

The commit messages carry the reasoning for each decision and a `Known gaps`
paragraph apiece. They are worth reading before changing that area — several
record a wrong first attempt and why it was wrong.

**Entities understood today:** LINE, CIRCLE, ARC, ELLIPSE, LWPOLYLINE,
POLYLINE2D/3D, SPLINE, TEXT, MTEXT, HATCH, SOLID, INSERT (incl. MINSERT),
DIMENSION (via its anonymous block), VIEWPORT.

Anything else is counted by `ImportDiagnostics` and shown in the status bar.
That tally is the backlog, ranked by what real files actually contain — it is
how the missing SOLID that left dimensions without arrowheads was found.

## What the editor is already set up for

Four decisions were made during the reader specifically to make this phase
additive. They are the things not to undo.

**`SourceHandle` on every entity, layer, block and layout.** A DWG holds far
more than this model: xdata, extension dictionaries, proxy objects from
vertical products, annotation scales. If saving regenerates a `CadDocument`
from the scene, all of that is silently destroyed and the user's file
degrades a little every time they open it here. If saving applies *deltas* to
the document that was loaded, anything we do not understand survives
untouched. This is the difference between a DWG editor and one that works.

**Blocks are instanced, not copied.** `SInsert` holds a reference plus a
`Mat3`. Editing a definition will therefore update every instance for free.

**`Drawing.ActiveLayout`.** Layers, blocks and linetypes are shared; switching
sheets moves one field. `Entities` and `Bounds` forward to the active layout,
so most code never needed to know layouts exist.

**`EmitContext`** already threads per-viewport frozen layers, nesting depth
and pixels-per-unit through the emit tree. Selection highlighting and grip
drawing will want the same channel.

## The gap that is not yet closed

`DwgLoader.Load` returns a `Drawing` and **throws the `CadDocument` away**.
Delta-save needs it. Core cannot hold it — Core must not see ACadSharp.

Suggested shape: a `DwgSession` in `FreeDwg.Interop.Acad` owning both the
`CadDocument` and the `Drawing`, exposing `Save(path)`. Core stays clean, the
shell holds a session rather than a bare drawing, and the handle→object
mapping lives on the side that knows what a handle is.

Change tracking has to come from the command layer: a dirty set of
`SourceHandle`s plus created/deleted lists. Do not try to diff two documents.

## Milestones

**E1 — Selection and hit testing. Done.** What landed, and where it differs
from what was sketched here:

- `SceneEntity.DistanceTo(Vec2, PickContext)` per entity, plus
  `IntersectsRect` — crossing selection needs a shape-versus-rectangle test,
  and a distance to a point cannot stand in for one. `PickContext` mirrors
  `EmitContext` for the reason `EmitContext` exists: block children are on
  their own layers, and a tolerance has to be divided by a block's scale on
  the way in.
- Analytic wherever the entity is analytic: line, circle, arc, bulged
  polyline, text box. Ellipses and splines flatten at the pick tolerance
  rather than at the zoom, which keeps the flattening error to a quarter of
  the radius a click is allowed to miss by.
- `Picking/SpatialIndex` is a BVH, not a grid. A CAD drawing is the worst
  case for a grid: a title block in one corner, the model in another, and
  entities from a millimetre of hatch to a kilometre of setting-out line. It
  answers with positions, which callers sort — entity order *is* painting
  order, and an unsorted query quietly reshuffles what is drawn over what.
  `Layout.Index` caches it; `Layout.Add` and `InvalidateBounds` drop it.
- Highlighting is a sink decorator (`StyleOverrideSink`), not a flag on the
  context. The style a sink receives comes from the entity, not from
  `EmitContext`, so there was nothing to thread an override down — but every
  style passes through the sink, and wrapping it reaches inside a block for
  free. `SelectionRenderTests` is a pixel test for exactly that.

Three things are deliberately approximate, and none is worth fixing until
someone notices it:

- Window selection tests entity **bounds**, not geometry. A spline's bounds
  are its control hull, so one lying near the edge of the window is
  occasionally missed.
- Crossing selection through a **rotated** block tests against the bounds of
  the inverse-transformed rectangle. That over-selects slightly at its
  corners and never under-selects.
- A hatch is picked anywhere inside it, solid or pattern. AutoCAD wants a
  pattern line; clicking a visibly filled area and hitting nothing reads as a
  bug to everyone who is not AutoCAD.

Two UI decisions worth knowing before changing them: left-drag no longer pans
(it bands, so right-drag pans as well as middle), and a plain click
*replaces* the selection where AutoCAD would add to it — Shift or Ctrl
extends.

**The toolbars came next, ahead of the tools they drive.** The shell has an
icon toolbar and a Draw/Modify palette holding all 28 tools, drawn as path
data in `Resources/Icons.xaml`. The ones that are still disabled say in their
tooltip what they are waiting for.

The app now opens on a blank Untitled drawing rather than on nothing, so the
draw tools have somewhere to put things before anything is loaded.
`CadCanvas.Tool` is the tool in force; setting it abandons whatever the
previous tool had half-picked, and clears the selection, because drawing and
selecting are different modes and carrying a selection into a draw tool only
makes the next Delete a surprise.

**One mode owns the left click.** `CadCanvas.Mode` is an enum — Select, Draw
or ZoomWindow — and every way of changing it goes through one setter that
raises one event. The shell redraws every mode button from that event rather
than setting canvas state and button state side by side.

That replaced a real bug. A tool flag and a separate `ZoomWindowArmed` flag
could both be set: the toolbar showed a draw tool *and* zoom window lit,
while only the tool actually decided what a click did. Two booleans that must
never both be true are a bug waiting to be found, and this one was found by
someone looking at the screenshot and asking which button was in charge.

`CadCanvas.PickAt` and `PlaceToolPoint` take **world** coordinates and are
public. The mouse handlers are thin wrappers over them, which is what lets
`CanvasTests` drive a whole draw-select-erase-undo cycle without a mouse.
Synthesising mouse messages does not work here: WPF reads the pointer from
the live mouse device, so a fake click is either ignored or lands wherever
the real cursor is, and a test that moves the real cursor clicks on whatever
the person at the machine is doing.

**E2 — Command stack. Done.** `IEditCommand` with `Apply`/`Undo`/`Describe`,
`CommandStack` holding done and undone lists, `AddEntities` and
`DeleteEntities`. Held per drawing rather than per application, because an
undo stack belongs to the document it describes.

The dirty tracking is `ChangeLog`, and `Summarize()` builds it by replaying
the done stack rather than maintaining it as edits land. That sounds wasteful
and is not: it runs when someone asks rather than on every mouse move, and it
means undo cannot leave the log claiming a change that has been taken back.
Maintaining the sets incrementally needs every command to know how to remove
itself from them, which is where this kind of bookkeeping goes wrong.

`DeleteEntities` remembers the position of everything it removes, because
entity order is painting order: an entity restored on top of what it used to
sit under has not really been restored.

**E3 — Grips.** Move, and drag endpoints. The first real test of whether the
scene model is comfortable to mutate; expect to find that some entity caches
(`Bounds`, `BlockDefinition.Bounds`, and now `Layout.Index`) need
invalidating more carefully than they do now. `SceneEntity.InvalidateBounds`
exists but nothing calls it yet, and nothing carries it up to the layout that
indexed the entity. E3 is where that has to be sorted out.

**E4 — Draw tools and snapping. Mostly done.** Line, polyline, rectangle,
circle, three-point arc and ellipse all draw, on the current layer, through
the command stack, with a live preview and undo, and they snap.

A `DrawTool` takes world points and gives back a scene entity. It has no
reference to a drawing at all, so it cannot reach past the command stack;
that rule is a type signature rather than a comment. The shell turns clicks
into points, stamps the entity with the current layer, and commits.

The preview draws through the same `IDrawingSink` as the scene, so a
previewed arc bulges the way the real one will. A preview with its own
drawing path is free to disagree with the result, which is the one thing a
preview must never do.

Arcs are three-point (start, a point on the arc, end) because it is the only
form that needs no separate answer for which way round the arc goes: the
middle point says. `ArcMath.TryArcThrough` is the arithmetic, and the sign of
the sweep it returns is the same trap as ever — left to right over the top is
*clockwise*, and the tests state it both ways round.

**Object snap, ortho and the grid are in.** `Snapping/SnapEngine` resolves a
cursor position into a point, and the order is strict rather than combined:
object snap, then ortho, then grid. An object snap is the user pointing at a
specific existing point and has to win outright — squaring it up afterwards
would move it off the thing they aimed at.

Entities offer their own snap points through `CollectSnapPoints`, which is
the third thing they do for themselves after emitting and hit testing. A
bulged polyline segment offers the midpoint of its *arc*, not of its chord;
an arc offers only the quadrants its sweep actually reaches; a block instance
offers its contents transformed into place, because symbols are the thing
most worth snapping to in a real drawing. A hatch offers nothing: its
boundary is derived and already flattened, so its vertices are neither
authoritative nor few.

The grid switch does both jobs — showing the grid and snapping to it. AutoCAD
keeps GRID and SNAP apart, which is two settings to explain and a standing
source of "why is it not snapping to the grid I can see". The spacing steps
up by decades so that zooming out does not ask for a million lines, and you
snap to the spacing you can see.

The canvas draws the snap marker with the shapes AutoCAD uses — square for an
endpoint, triangle for a midpoint, circle for a centre, diamond for a
quadrant. That is not decoration: it is the only way to tell that a point
landed on the thing you aimed at rather than a pixel away.

Still to do here: intersection and perpendicular snaps, tangent, polygon
(which needs somewhere to ask for a side count), text (which needs an
editor), and trim/extend/fillet/chamfer, which all want the curve-curve
intersection that an intersection snap would need anyway.

**E5 — Save.** Delta-apply onto the original document, then `DwgWriter`.
Write R2000 (AC1015) first. Note ACadSharp cannot write AC1021 (R2007) at
all; every other version from R14 up is supported.

Regenerating dimensions after an edit is the one place the anonymous-block
shortcut stops paying: at that point real dimension layout has to be written.
It is not needed before E5.

## Traps already paid for

- **Arc winding is invisible except in pixels.** WPF names sweep directions
  for how the arc *looks*; the world-to-device Y flip mirrors the plane and
  the two cancel. A mirrored block flips it back. Getting it wrong turns
  every bulge inside out while endpoints and bounds still look correct.
- **A probe placed where another entity passes will pass for the wrong
  reason.** Six probes in the blocks fixture were sitting on the cross arms;
  three of them were "passing". Take probes off-axis.
- **The diagnostics tally must only list things that do not work.** A test
  asserting TEXT was unsupported survived three milestones past text landing.
- **Running the app locks its DLLs.** Builds fail with MSB3027 until it is
  closed. Ask first.

## Smaller things worth picking up

Raised by a real file, not yet investigated: a pattern hatch appeared to run
straight through an inner square. `Hatch.Style` (Normal / Outer / Ignore) is
currently **not read**, so island behaviour is whatever ACadSharp's
`ExplodePattern` defaults to. Island coverage in the tests is for *solid*
hatches only; the pattern path is untested for islands.

From the `Known gaps` paragraphs, roughly in order of how often they will be
noticed:

- SHX fonts are substituted with an outline font, not stroked. Reported in
  the status bar, so at least it is honest.
- Paper space does not draw the sheet, so a layout reads as line work on the
  model background.
- POINT is unsupported. Harmless for dimension definition points, which live
  on `defpoints` and do not plot.
- Gradient-filled hatches draw nothing.
- Dimension text below `MinTextHeightPixels` is skipped, and stock dimension
  styles are small enough for that at drawing extents.
- Linetypes keep dash lengths only; embedded text and shape elements are
  dropped.
- TEXT's Aligned and Fit modes are placed but not stretched.
- Text does not mirror inside a mirrored block.
- Non-rectangular clipped viewports use their bounding rectangle.
- One `StreamGeometry` allocation per figure; batch when it starts to matter.
- The rubber band is painted by `CadCanvas` in device space, outside the
  sink. Fine while it is the only overlay; grips will want somewhere to live.
- The palette scrolls rather than reflows, and is three columns because 28
  buttons in two did not fit a 700px window. A fourth group would want a real
  layout rather than another column.
- There is no coordinate entry: every tool is mouse-only, so nothing can be
  drawn to an exact size. With snapping in, this is now the single biggest
  thing between here and real work.
- The snap search is a spatial-index query per mouse move, which is fine, but
  it collects every candidate from every nearby entity before choosing. A
  drawing with a very dense block under the cursor would feel it.
