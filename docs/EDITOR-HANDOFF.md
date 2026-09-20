# Handoff: the reader is done, the editor draws

Written at commit `1325ee0` and updated as the milestones land; current as of
the grips, dimensions, layer editing and corner work below. Everything here
is either in the repo or in the commit messages; this is the map, not a
second copy.

## Start here

The reader is complete and the editor draws, modifies and undoes. **What it
cannot do is save.** That is E5, and it is the whole of what stands between
this and a program someone could use on a real file. The groundwork for it --
`SourceHandle` carried on everything, every mutation going through the
command stack -- has been in since before the editor phase began, on purpose.

Picking this up cold, read in this order:

1. `CLAUDE.md`, for the conventions that are load-bearing. Several of them
   are load-bearing because breaking them was tried.
2. **The gap that is not yet closed**, below. It is E5's first problem, and
   it is an architectural one rather than a coding one.
3. `tests/FreeDwg.Tests/README.md`, before touching anything the render tests
   cover.

590 tests pass in about a second. Two kinds of them exist because ordinary
tests could not catch what they catch: the render tests assert **pixels at
world coordinates**, because three bugs so far were invisible to the object
model; and `StartupTests` runs the real executable and waits for a **window**,
because a `StaticResource` that resolves to nothing compiles clean, passes
everything else, and kills the app before it draws.

## Where things stand

The reader is M1–M5 of the original plan. Everything from E1 down landed in
the editor phase.

| | | |
|---|---|---|
| M1 | `0b8b13a` | load a DWG and render it |
| M2 | `e11b3fb` | blocks, layers panel |
| M3 | `e1a6430` | text, linetypes, lineweights |
| M4 | `25bc589` | splines, ellipses, hatches, dimensions |
| M5 | `6b2495d` | paper space, viewports |
| — | `1325ee0` | the render harness became `tests/` |
| E1 | `6a5d4e8` | selection, hit testing and the spatial index |
| — | `15bbf56` | icon toolbar and tool palette |
| E2 | `a53c135` | command stack, undo/redo, change log; new documents and six draw tools |
| E4 | `25f2d64` | object snap, ortho, grid; one canvas mode |
| E3 | `cfeb9e1` | move, copy, rotate, scale, mirror |
| E4 | `679c2fd` | curve intersection: trim, extend, fillet, chamfer, tangent |
| — | `f5cc9a9` `dc545a3` | the corner radius moves onto the canvas, and becomes findable |
| — | `21b2134` | the properties panel, then `5244d4a` `ec3c7e9` `c4b33e8` on top of it |
| E4 | `d5e7d1a` | perpendicular, tangent and tracking snaps |
| E4 | `f112317` | the intersection snap |
| E4 | `6d0f6d7` `71e381c` | polar tracking, measured from the last segment |
| — | `c76a19b` | tangent and perpendicular aim at the object, not at the answer |
| E4 | `f1bd651` | typed lengths, and document units |
| E3 | `4cd4e0d` | grips: handles on the selection, stretch and move |
| E4 | `4cd4e0d` | dimensions: linear, aligned, radius and diameter |
| E4 | `4cd4e0d` | fillet and chamfer on a polyline corner; sizes typed on the canvas |
| — | `4cd4e0d` | layers: new, delete, rename, recolour |
| — | — | the layers panel moves to the right, with a filter |
| E5 | — | **save — not started** |

E1, E2 and E3 are done. E4 is done but for polygon, text, hatch, spline,
block insertion, offset, array and explode. E5 has not been started.

Dimensions and layer editing arrived after the milestone list was written and
do not fit a letter: dimensioning was always filed under E5 because writing
one back is E5's problem, but *drawing* one never was.

The commit messages carry the reasoning for each decision and a `Known gaps`
paragraph apiece. They are worth reading before changing that area — several
record a wrong first attempt and why it was wrong.

**Dimensions.** `SDimension` is a real entity: two origins, a point the
dimension line runs through, and everything else worked out from them. A
`Linear` one measures the separation of its origins along a fixed axis, so a
horizontal dimension across two points at different heights gives the
horizontal gap and the extension lines make up the difference; an `Aligned`
one reads its axis off its own origins, so it gives the true distance and
stays aligned when a grip moves one of them. `DimensionMath` is the layout,
as arithmetic, with no sink near it -- which matters because a dimension is a
dozen coincidences that all have to agree at once.

Three things worth knowing. Each extension line takes its own direction
towards its own end of the dimension line, not a direction shared by the
pair: the dimension line can lie *between* the two origins, and a shared
direction leaves one of them hanging in mid air short of the line it exists
to meet. The number sits above its own line unconditionally -- not on the far
side from the geometry -- because that is the side it is read from, which is
what every drawing shows and what the first version of this got wrong. And
the ISO sizes are millimetres of paper, so `DimensionStyle.For` converts them
into the drawing's units rather than copying the numbers, which is the
difference between a dimension and an invisible one in a drawing measured in
metres.

A dimension with no room for its own marks turns them out, which is what a
chamfer of five millimetres needs: two 2.5 arrowheads head to head meet
exactly and paint a solid diamond rather than a measurement. `DimensionMath.
Fit` decides it, and it is two separate questions -- the arrows run out of
room *along* the line and the number runs out of room *across* the extension
lines, so a small feature usually wants the arrows out and the number left
where it is. The arrow tips never move. What moves is which way the bodies
face, and the drawn line then runs past the measured ends to give them
something to sit on: `DimensionFit.StrokeStart` and the geometry's
`LineStart` are deliberately different points.

Radius and diameter measure an object rather than two points, which is the
one case where a tool's first click is aimed at something and its second is
a point. That is `CanvasTool.WantsEntity`, asked per click rather than
answered once from the kind of tool -- a draw tool never wants an object and
an entity tool always does, and nothing before this changed its mind
half way through. The tool holds the centre and the radius rather than the
object it read them from, so it still has no reference into the drawing.

There is no DIMSCALE, so a drawing meant for 1:100 gets text sized for 1:1.
Angular, baseline and continue are not written. And **imported DIMENSIONs
still arrive as exploded anonymous blocks**: the reader was never changed,
because the two only have to be reconciled when one is written back, which
is E5.

Dimensions have a palette group of their own rather than a corner of Draw,
which is where AutoCAD puts them and what a drawing office does with them:
they are annotation, they go on their own layer, and they are reached for as
a job of their own once the geometry is finished.

**Fillet and chamfer work on a polyline.** Two clicks on two segments of the
same polyline round or cut the vertex they share, and it stays one polyline:
the corner becomes the two tangent points, with the arc between them held as
a bulge. Exploding first was thought to be the prerequisite and is not --
and a rounded rectangle that is still a rectangle is worth more than one
that has become four lines and an arc. The closed polyline's wrap is
handled, since otherwise the corner where a rectangle joins up is the one
corner that cannot be rounded. Two things are still refused rather than
approximated: a corner that already carries an arc (a tangent circle to a
curve is a different problem) and a size that would eat past the next vertex.

The fillet radius and the chamfer distance are typed **on the canvas** now,
in the same box the length goes in -- `CursorEntry` says which of the three
it is showing. They are also two separate numbers: sharing one meant that
setting a 2 mm chamfer silently made every later fillet 2 mm as well.

The panel they live in is on the right of the drawing now, with the full
height of the window, and has a box to find a layer by typing part of its
name. A real file arrives with fifty layers called things like
`Structural_Section_StairA` and the old panel -- sharing one narrow column
with Properties -- showed eight of them.

The filter brought one hazard with it, and it is the kind that does damage
before anyone notices: the panel used to read a row's *position* back off
the ListBox and use it as the layer's index. With rows hidden those are
different numbers, so renaming the third row showing would have renamed the
third layer in the drawing. `LayerItem.Index` carries it instead, and
nothing in the panel asks the list where a row is any more.

**Layers are editable.** New, delete, rename, recolour, and the three
switches, all through the command stack. Two things make this less trivial
than it sounds. An entity names its layer by *position*, so deleting one
renumbers every entity in every layout, the contents of every block
definition, every viewport's frozen-layer set and the current layer --
`LayerTable` does that in one place, and a layer with anything still on it is
refused outright rather than guessing whether the geometry should be erased
or moved. And because ByLayer is resolved away at import, recolouring a layer
has to go and recolour its entities or the swatch and the drawing disagree;
`ChangeLayer` restyles the ones still drawn in the layer's old style, which is
exactly the set that was following it, and leaves an explicit override alone.

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

**E3 — Modify tools. Done.**
Move, copy, rotate, scale and mirror all work on the selection, through the
command stack, with a live preview and undo.

`SceneEntity.Transform` is the third piece of double dispatch, after emitting
and hit testing. It is deliberately *not* virtual: the override is
`TransformGeometry`, and the wrapper drops the bounds cache afterwards. That
was the trap this milestone was warned about, and making it structurally
impossible beat remembering. The layout's own bounds and its spatial index
are a level up, so `TransformEntities` invalidates those.

The arc-winding trap came back exactly where it was predicted. A mirror turns
the plane over, so a mirrored arc sweeps the other way and a mirrored bulge
negates — and endpoints and bounds are identical either way, so only pixels
say whether it is inside out. `Mat3.IsMirror` is how every entity asks, and
there is a render test for it.

Undo applies the inverse rather than restoring a snapshot: exact to double
rounding for an affine transform, and it avoids every entity having to know
how to copy and restore its own geometry. A transform with no inverse is
refused at construction instead of failing at undo time.

`SceneEntity.Clone` is shallow by default — most entities are value fields
all the way down — with `CloneGeometry` for the few holding arrays. A block
reference is deliberately *not* deep copied, since instancing a definition is
the whole point of one. A clone carries no `SourceHandle`: it was never in
the file, and giving it the original's handle would have a save overwrite the
original with the copy.

**Grips.** Handles on the selection, dragged to reshape one object rather
than transform a set of them. A grip is one of two things and the enum says
which: a `Move` grip drags the whole entity, which is a translation and goes
through `TransformEntities`; a `Shape` grip changes the geometry, and goes
through `ReplaceEntities` after being applied to a *clone*. That second
route is the properties panel's, reused: it is what makes a stretch undoable
without every entity needing a way to save and restore its own geometry, and
`EditPlan.Replace` carries the handle across so the file still recognises
the object.

`CollectGrips` is the fourth thing entities do for themselves, and
`MoveGrip` is a non-virtual wrapper over `MoveGripGeometry` for the same
reason `Transform` is over `TransformGeometry` -- a stretched entity that
kept its cached bounds would be culled where it used to be. Which handles
each kind offers is its own business: a line gives two ends and a middle
that moves the whole thing, a circle a centre and four quadrants that are
all the same radius, an arc its three defining points, a polyline every
vertex plus the midpoint of every *bulged* segment, a spline its control
points, a hatch nothing at all.

The arc is re-solved through three points, the way the arc tool builds one,
so the middle point is what says which way round it goes and a dragged end
cannot flip the sweep quietly. Dragging a bulge midpoint across its chord
turns that segment the other way, which is the same fact stated as
`tan(sweep / 4)`.

Where the handles are is `GripSet`, in Core: it collects them off a
selection and answers which one a world point is nearest, which is the half
worth testing. What is left in `CadCanvas` is squares of a fixed pixel size
in device space, because a grip is a target for the mouse and has to stay
the same size to aim at however far the view is zoomed out. `GripSet` stops
offering handles past a hundred selected objects, as AutoCAD does: a
crossing window over a drawing would otherwise bury the geometry under
squares nobody could pick out.

Taking hold of a grip is not a mode. It is what a left press does when the
cursor is on one, which meant the canvas needed a single answer to what the
left button is in the middle of -- `LeftGesture`, one enum covering pick,
band and grip, for exactly the reason `Mode` is one enum. The drag snaps
like any other point, measures ortho and polar from where the grip started,
and takes a typed length: aim it and type 50.

Known gaps: an elliptical arc offers its axis handles but not its ends; a
viewport offers none, since resizing one has to answer what happens to the
view inside it; and a polyline vertex cannot yet be added or removed, only
moved.

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

**Curve-curve intersection landed next, and unlocked four tools at once.**
`Geometry/Intersection` works on `CurvePiece` -- a segment or an arc -- so
there are three cases to solve rather than one per pair of entity types.
Entities reduce themselves through `CollectCurves`, exactly for lines, arcs,
circles and polylines, and by flattening for ellipses and splines. All of it
is closed form: an intersection a fraction of a unit out is a gap, and gaps
are what this whole family of operations exists to remove.

`Intersection.Unbounded` is the extend case, where the crossings *outside*
the segment's own range are the entire point.

Trim, extend, fillet and chamfer all go through `Editing`, and all express
themselves as an `EditPlan` -- these entities out, those in. Trimming a
circle leaves an arc and trimming the middle of a line leaves two lines, so
none of them is a transform and none is purely an add or a delete. One
`ReplaceEntities` command covers all four, and a replacement inherits the
original's `SourceHandle`, so a trimmed line is still the line the file knows
about.

Where you click each line is the whole user interface for fillet and
chamfer: it says which half survives. Radius zero brings the lines to a sharp
corner, which is quietly one of AutoCAD's most used features. Lines only --
arc-to-line fillets are a much larger problem and line-to-line is the
overwhelming majority of real use.

**The radius lives on the canvas as `CornerRadius`,** not on the tool. It was
copied into the tool at construction *and* pushed in again on change, which
is two places to forget and exactly how a setting ends up silently doing
nothing. The canvas applies it when a tool starts and whenever it changes, so
both orders -- type then pick, pick then type -- are the same path, and both
are tested. It shows in the status bar at all times as well, so the current
radius is visible without having to start the tool to find out.

Tangent mate is the odd one out: it moves a circle until it touches a line,
keeps the circle's identity and handle, and so goes through the transform
command rather than a replacement. It keeps the circle on the side it
started, because a circle that jumped across the line it was being mated to
would be a surprise.

**The properties panel** took over from the fillet radius box. The middle
column is Properties over Layers now, a two-column grid grouped by category
with a description pane under it -- the shape every CAD and every IDE puts
this sort of thing in, so it needs no explaining.

It holds the editor settings that used to be loose on the toolbar (fillet
radius, ortho, grid, grid spacing) and, when one object is selected, its
geometry: a line's ends with its length and angle derived, a circle's centre
and radius, an arc's angles in degrees, and the layer by name. Derived
figures are shown but read-only -- a length is a consequence of two ends, not
a third thing to set.

Edits go through the command stack like everything else. The panel edits a
*copy* and swaps it in with `ReplaceEntities`, so nothing needed a way to
overwrite an entity's geometry in place, the edit is undoable, and the
replacement keeps the handle the file knows the object by. A value that will
not parse puts the old text back rather than leaving the panel and the
drawing disagreeing.

Layer, colour and lineweight are dropdowns rather than typed text, since a
field with a fixed set of answers should not have to be spelled from memory.
The colour row carries a chip of the colour it names, as the layers panel
does. Both lists are checked by a test that every value offered survives
being read back -- a list holding an answer the parser then refuses would be
a trap.

Colour and lineweight names go through `StyleChoices`, which puts names back
on the handful of values people pick and falls back to a hex triple or a
plain measurement otherwise, so nothing in a real file is unrepresentable.
"By layer" copies the layer's colour rather than linking to it, and the row
says so: ByLayer is resolved at import, so the scene holds a colour and there
is nowhere to record that it should keep following.

Several objects at once show what they have in common: layer, colour and
lineweight always, plus the one geometric figure they share when they are all
the same kind, such as the radius of a set of circles. A field they disagree
on reads `*varies*` rather than picking one of the answers to display, which
would be a lie about the rest, and choosing the marker back is a no-op rather
than an error.

A batch edit is **one** command, so putting forty objects on another layer
takes one press of Ctrl+Z to undo rather than forty. `EditPlan.Swap` builds
the one-for-one plan, and `ReplaceEntities` learned to put each replacement
back at its own original's index when the counts match. That matters more
than it sounds: a selection is in the order it was picked, not the order the
entities sit in, and entity order is painting order, so restoring in pick
order would quietly reshuffle the drawing. There is a test for exactly that.

The last entry of the colour list opens a picker, where AutoCAD puts Select
Colour. The palette is generated from hue and lightness rather than typed
out, so the rows are even and no value is a transcription error; the middle
row is fully saturated, which is what puts the six pure hues the dropdown
names into the grid, so the two agree. A test checks that every swatch
survives being named and read back.

AutoCAD's 255-entry colour index would be the authentic palette, but Core
resolves indices to 24-bit colour at import and has nowhere to put one back,
so reproducing that table from memory would risk being quietly wrong about
colours nobody could then correct.

The chooser reaches `PropertySource` as a delegate supplied by the shell,
which keeps the panel testable with a stand-in that answers at once. It takes
a callback rather than returning a colour because the shell has to *defer*
the dialog: opening a modal window while the grid is still committing the
cell it was picked in is a way to wedge WPF's input system.

**Numeric entry arrived** with the length overlay. While a tool is picking,
a box beside the cursor shows how long the run is; typing a digit opens it
and Enter places the point at that distance, keeping the direction the cursor
is pointing in. Direction from the mouse and length from the keyboard is how
CAD has always taken a measured line, and it needs no angle field of its own
because polar tracking already sets the angle exactly.

The canvas keeps the keyboard and hands typing over only when it is plainly
a measurement, so Escape, Enter and Delete keep reaching the tool. Focusing
the box up front would take those keys away for the whole of every draw.

**Units** are `Drawing.Units`, read from the file's INSUNITS header and
defaulting to millimetres. A coordinate is a bare number and the unit says
what it counts, so changing it relabels the drawing rather than rescaling it
-- which is what INSUNITS means and what the settings dialog says on its
face. The one place conversion happens is typing: `3ft` in a millimetre
drawing has to arrive as 914.4.

`ResolvePoint` sets the current aim as well as returning it. It used not to,
which left the method half doing its job -- anything calling it directly got
the answer while the preview, the length readout and a typed length all still
looked at the previous position. Four canvas tests failed on it immediately.

Numbers are parsed with the invariant culture: a CAD user types a decimal
point, whatever their machine thinks the separator is.

**Perpendicular, tangent and tracking** came later. The first two are the
snaps that cannot be precomputed per entity, because the answer moves with
the point the line is being drawn *from*: they live in `Geometry/Projection`,
work on `CurvePiece`, and are folded into the same nearest-wins pass as the
rest, so pointing at a real endpoint still beats a computed right angle.

A perpendicular foot beyond the end of a segment is refused rather than
clamped. Clamped, it is the endpoint, which endpoint snap already offers, and
offering it again under a name that promises a right angle would be a lie.

**What you aim at is not always what you get,** and these two are where that
matters. A tangent's touch point can be a quarter of the way round the rim
from the cursor, so the thing being pointed at is the *circle* and the answer
is worked out from it. `SnapCandidate.Reach` carries that distinction: the
distance the candidate is ranked by, which for most snaps is simply the
distance to the point itself. Built the other way round -- requiring the
cursor to be near the computed point -- tangent is unusable, because you
would have to know where the touch point was before you could aim at it.

Snaps whose answer is under the cursor outrank the ones worked out from it,
whatever the distances say. Pointing at the end of a line that happens to
start on a circle gives that end, not a tangent point elsewhere on the rim,
even when the rim is a hair nearer.

Tracking acquires a point whenever the cursor rests on one and lines the next
point up with it: level, above, or at the crossing of two acquired points,
which is what someone reaching for the corner of two existing features
actually wants. AutoCAD makes you hover for about a second first; here it is
taken at once, which is less deliberate but needs no timer, and the dashed
guides only appear when the cursor is genuinely lined up, so the extra points
cost nothing on screen. Only the last two stay acquired, and a new tool or
Escape forgets them.

The guides matter as much as the snap. A point placed level with a corner on
the far side of the sheet is otherwise indistinguishable from one placed by
hand nearby, and the line back to the corner is the whole explanation --
which is why `SnapResult` carries them.

**Intersection** was the last of the eight, and cost almost nothing: the
geometry had been there since trim, so wiring it up was collecting the curve
pieces near the cursor and pairing them. Two details are load-bearing. Pieces
are filtered against the cursor box before pairing, since pairing is
quadratic and a dense hatch could otherwise offer thousands; and pieces of
the *same* entity are never paired, because adjacent polyline segments meet
at every vertex and reporting those would offer each vertex twice, once
correctly as an endpoint and once under a marker meaning something else. The
cost is that a polyline crossing itself offers nothing, which is rare and far
less confusing than the alternative.

**Polar tracking** then replaced the hand-rolled horizontal-and-vertical
alignment with rays at a settable angle, which turned out to be one
mechanism serving two features: tracking runs the rays out of acquired
points, polar runs them out of the point the line started at, and they differ
only in where the rays begin. A crossing between one of each -- a known
height met at a known angle -- falls out for free and is the most useful
thing in the whole arrangement.

Ortho and polar are mutually exclusive in the toolbar. Ortho *forces* the
direction and polar only *attracts* to it, so with both on polar could never
be the answer, and a lit button that can never do anything is the same bug as
the two-modes-at-once one.

`Polar.Direction` cleans its axis components to exact zero. Straight out of a
cosine a vertical ray carries 6e-17 of sideways drift per unit of length, and
snapping exists precisely so that points land exactly.

Angles are measured from the **previous segment** by default, which is what
drafting usually means by one: the next run of a polyline turns thirty
degrees from the last, not thirty degrees from the horizon. The canvas passes
the point before the origin as well as the origin, and with only one point
picked there is no previous run, so it falls back to east on its own rather
than needing a special case. `Polar relative` in the properties panel turns
it off.

It applies only to the rays out of the point being drawn from. Tracking rays
stay absolute -- lining up level with a corner is the whole point of them,
and rotating them with the last segment would take that away. That is a
deliberate divergence from AutoCAD, which applies one setting to both.

Still to do, and still dark in the palette: polygon, which needs somewhere to
ask for a side count; text, which needs an editor; hatch and block insertion,
which need a boundary and a definition chooser respectively; spline; offset,
which needs real curve offsetting; array, which needs row and column counts;
and explode, which is what trim and extend are waiting on for polylines.

**E5 — Save.** Delta-apply onto the original document, then `DwgWriter`.
Write R2000 (AC1015) first. Note ACadSharp cannot write AC1021 (R2007) at
all; every other version from R14 up is supported.

Regenerating dimensions after an edit is the one place the anonymous-block
shortcut stops paying: at that point real dimension layout has to be written.
It is not needed before E5.

## Where to go next, ranked

1. **E5, save.** Nothing else changes what this program *is*. Start with the
   `DwgSession` described above; the writer is the easy half. Note that the
   change log now reports layers as well as entities, and that `SDimension`
   and the imported exploded-block dimensions have to be reconciled there.
2. **Explode.** The smallest piece that unlocks another: trim and extend
   handle lines, arcs and circles only, so a polyline has to be broken up
   first, and today there is no way to break one up.
3. **The rest of numeric entry** -- an angle field, and XY. Lengths alone
   already cover most of drawing to size, and they now finish a grip drag as
   well as a picked point.
4. **The remaining draw tools**, in the order the palette lists them.
5. **Multi-object stretch.** Grips move one object at a time. The crossing
   window that takes every vertex inside it and moves the lot is a different
   command, and wants the grip set to hold a selection of *handles* rather
   than a hot one.
6. **Angular, baseline and continue dimensions.** The buttons are on the
   palette already, greyed, each saying in its tooltip what it is waiting
   for. Angular needs a dimension line that is an arc, which is a second
   layout rather than a variation on the linear one; baseline and continue
   need to pick an existing dimension to carry on from.
7. **A dimension style dialog**, and DIMSCALE with it. The sizes are a
   `DimensionStyle` on each dimension and a default on the canvas, so there
   is somewhere for one to write to.

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
- Typed entry is **lengths only**, and only while a tool is mid-pick. There
  is no angle field (polar tracking sets the angle instead), no absolute or
  relative XY, and no compound `5'6"`. So a circle still cannot be given a
  diameter and a rectangle cannot be given two sides: both take their size
  from wherever the second click lands.
- Tracking acquires a point the moment the cursor rests on one, with no
  dwell. AutoCAD waits about a second. No timer is simpler and has not been a
  nuisance, but a drawing dense enough to acquire something in passing would
  want one.
- The drawing settings dialog holds units and decimal places only. Grid
  spacing, ortho, snap modes and the corner radius are in the properties
  panel, which is a reasonable place for them but not the obvious one to look
  for a setting.
- `Drawing.Units` labels lengths. Angles are always degrees and areas are
  never shown, so nothing else has yet had to learn what a unit is.
- The snap search is a spatial-index query per mouse move, which is fine, but
  it collects every candidate from every nearby entity before choosing. A
  drawing with a very dense block under the cursor would feel it.
- `SInsert.Placement` was called `Transform` until it collided with
  `SceneEntity.Transform`. Any old notes saying `insert.Transform` mean that.
- The tool buttons act on `Click`, so setting `IsChecked` programmatically
  lights one without starting its tool. `SyncModeButtons` only ever drives
  them from the canvas, which is the safe direction, but scripting the UI
  from outside would find the lie.
- Trim and extend handle lines, arcs and circles. A polyline has to be
  exploded first, which is a tool that does not exist yet.
- Rotate reads its angle straight off the second point, so dragging right is
  zero. With coordinate entry it should take a reference direction instead,
  the way Scale already takes a reference distance.
- A non-uniform scale would turn a circle into an ellipse, which `SCircle`
  cannot represent; it approximates with the uniform scale. Nothing produces
  one today, and a tool that did would have to replace the entity rather than
  transform it.
