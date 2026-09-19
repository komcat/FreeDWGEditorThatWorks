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

**The toolbars came next, ahead of the tools they will drive.** The shell now
has an icon toolbar and a Draw/Modify palette holding all 28 tools, drawn as
path data in `Resources/Icons.xaml`. Only the pointer, zoom extents and zoom
window are live; everything else is disabled, and each tooltip names the
milestone it is waiting for. That is deliberate rather than lazy: a tool
mutates the scene, and E2's whole point is that nothing mutates it except
through the command stack. The palette is here early because it is what the
window is laid out around, and because an icon set is easier to judge as a
set than one button at a time.

Wiring a tool up when its milestone lands is a `Click` handler and dropping
`IsEnabled="False"`. What does *not* yet exist is the tool-state machine
itself — which tool is in force, what a click means while one is active, how
a tool ends. `CadCanvas.ZoomWindowArmed` is the smallest possible version of
that idea (one flag, one shot) and should be replaced by the real thing in
E4, not extended.

**E2 — Command stack.** A mutation API on `Drawing` that goes through
commands, undo/redo, and the dirty tracking E5 needs. Get this in before any
tool exists, so no tool can mutate the scene directly.

**E3 — Grips.** Move, and drag endpoints. The first real test of whether the
scene model is comfortable to mutate; expect to find that some entity caches
(`Bounds`, `BlockDefinition.Bounds`, and now `Layout.Index`) need
invalidating more carefully than they do now. `SceneEntity.InvalidateBounds`
exists but nothing calls it yet, and nothing carries it up to the layout that
indexed the entity. E3 is where that has to be sorted out.

**E4 — Draw tools and snapping.** Line, polyline, circle, arc. Object snap
(endpoint, midpoint, centre, intersection) is what makes drafting usable and
is the largest chunk here.

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
