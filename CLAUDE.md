# FreeDWG Editor

A 2D DWG viewer, on its way to being an editor. WPF on .NET 10. DWG and DXF
parsing is done by [ACadSharp](https://github.com/DomCR/ACadSharp) (MIT);
this codebase is the scene model, the renderer and the shell.

```
dotnet build FreeDWGEditorThatWorks.slnx
dotnet test                 # 609 tests, ~1s
```

`tests/FreeDwg.Tests/README.md` explains how the render tests work and how to
debug one. Read it before changing anything they cover.

## Layout

```
src/FreeDwg.Core/          net10.0          scene model, geometry, renderer
  Commands/                                 every mutation, undoably
  Tools/                                    draw, modify and entity tools
  Editing/                                  trim, extend, fillet, chamfer
  Picking/                                  hit testing, selection, spatial index
  Snapping/                                 object snap, ortho, the grid
src/FreeDwg.Interop.Acad/  net10.0          ACadSharp -> scene  (the only project that sees ACadSharp)
src/FreeDWGEditorThatWorks/ net10.0-windows WPF shell, canvas, WPF sink
  Resources/Icons.xaml                      toolbar icons, as path data
  Resources/Toolbars.xaml                   the one button template they share
tests/FreeDwg.Tests/       net10.0-windows  xunit
docs/EDITOR-HANDOFF.md                      state of play and the plan for the editor phase
```

**Core must not reference WPF or ACadSharp.** This is enforced by the project
references, not by discipline. It is what keeps the file format out of the
model the editor will mutate, and it is why `dotnet test`'s geometry half runs
with no WPF and no parser present. Do not add either reference to Core.

## Conventions that are load-bearing

- **World coordinates are `double`, Y-up.** Device space is Y-down. The only
  place the two meet is `Camera` and the sink.
- **Geometry is transformed to device space inside the sink; pens are not.**
  A pushed `DrawingContext` transform would scale pen width, and plot widths
  are millimetres-at-plot-scale, so lines would fatten as you zoom. Text is
  the deliberate exception: its height *is* in drawing units.
- **Entities emit themselves** through `IDrawingSink` (`SceneEntity.Emit`), so
  the render loop needs no type switch and the sink can be retargeted. They
  hit test themselves too, through `DistanceTo` and `IntersectsRect` against a
  `PickContext`. Distances are to the geometry as *drawn*: a circle is its
  rim, a solid hatch is its area.
- **Styles are resolved at import.** ByLayer is gone by the time Core sees an
  entity; ByBlock survives as a `StyleInheritance` flag because it depends on
  which INSERT is drawing it.
- **`SourceHandle` is carried on every entity, layer, block and layout** so
  that saving can apply deltas to the original document instead of
  regenerating it. See the handoff doc; this is the whole basis of not
  destroying data we do not model.
- **Nothing mutates the drawing except an `IEditCommand`.** The delta a save
  will apply *is* the command stack, so an edit made behind its back is an
  edit the writer cannot see. `CommandStack.Summarize()` replays the stack
  into a `ChangeLog` rather than tracking it as edits happen, so undo cannot
  leave it claiming a change that is no longer there.
- **A `CanvasTool` takes world points and returns a result** — an entity for
  a `DrawTool`, a `Mat3` for a `ModifyTool`. It holds no reference to a
  drawing, so it *cannot* reach past the command stack, and the whole set
  tests as arithmetic with no mouse involved. The canvas holds exactly one
  tool field, for the same reason `Mode` is an enum.
- **`SceneEntity.Transform` is not virtual;** `TransformGeometry` is. The
  wrapper drops the bounds cache afterwards, so an entity cannot move and
  leave itself culled where it used to be. The layout's bounds and index are
  a level up and the command invalidates those.
- **Intersection works on `CurvePiece`, not on entities.** Everything reduces
  to segments and arcs first, so there are three cases rather than a hundred.
  `CollectCurves` is how an entity offers itself up.
- **The properties panel writes through the command stack like everything
  else.** It edits a *copy* and swaps it in with `ReplaceEntities`, so the
  edit is undoable and the entity keeps the handle the file knows it by.
- **A setting the tools use lives on the canvas, not copied into each tool.**
  `ApplyToolSettings` pushes the fillet radius, the chamfer distance and the
  dimension sizes into whatever tool is in force, from `SetMode` and from
  `RefreshToolSettings`, so a tool started before or after the number is
  typed -- or before or after the document units change -- behaves the same.
  Two copies of a setting drift, and the one that is forgotten is a value
  that silently does nothing. The fillet radius and the chamfer distance are
  *separate* numbers: they were one once, and setting a chamfer quietly
  changed every fillet after it.
- **A layer row carries its own index.** The layers list is filtered, so a
  row's *position* is not the layer's position in `Drawing.Layers` -- and
  using the position would rename, recolour or delete a layer nobody
  pointed at, silently and after the fact. Nothing in the panel asks the
  ListBox where a row is; `LayerItem.Index` is the only answer.
- **`CursorEntry` is the single answer to "what does the box beside the
  cursor edit".** A length while a point is being placed, otherwise the size
  the corner tool in hand works to. One enum, one destination for a typed
  number, for the reason `Mode` is one enum.
- **The app is smoke tested by running it.** `StartupTests` launches the real
  executable and waits for a window. A `StaticResource` that resolves to
  nothing, or a handler that fires mid-XAML-parse, compiles clean, passes
  every other test, and kills the app before it draws. That has happened
  twice.
- **Entities offer their own snap points** (`CollectSnapPoints`), the third
  thing they do for themselves after emitting and hit testing.
- **And their own grips** (`CollectGrips`), the fourth. A grip is either
  `Move` -- drag the whole entity, which the shell does through
  `TransformEntities` -- or `Shape`, which the entity answers in
  `MoveGripGeometry`. `MoveGrip` is the non-virtual wrapper that drops the
  bounds cache, exactly as `Transform` is. A shape drag edits a *clone* and
  swaps it in with `ReplaceEntities`, so it is undoable without any entity
  knowing how to restore its own geometry, and the replacement keeps the
  handle. Where the grips *are* is `GripSet`, in Core and testable; the
  canvas only paints squares of a fixed pixel size at those points.
- **`CadCanvas` holds one field for what the left button is doing.** Pick,
  band and grip are one `LeftGesture` enum, for the reason `Mode` is one
  enum: a bool per kind of drag lets two be true, and then the mouse-up has
  two answers for what it just finished.
- **A dimension is an entity, not a picture of one.** `SDimension` holds
  three points and works out the extension lines, arrowheads and number from
  them, so a grip drag re-measures and the number follows. Linear measures
  the separation along a fixed axis; aligned reads its axis off its own
  origins; radius and diameter hold a centre and a rim point instead, and
  the leader aims at wherever the number was dropped so the arrow slides
  round the rim. The number sits above its own line, unconditionally,
  because that is the side it is read from. `DimensionMath` is the
  arithmetic and has no sink anywhere near it. **Imported DIMENSIONs still
  come in as exploded anonymous blocks** -- reconciling the two is E5's
  problem, since it is the same problem as writing one back.
- **A dimension too small for its own marks turns them out.**
  `DimensionMath.Fit` asks two separate questions, because they are two
  different collisions: the arrows are on the line and run out of room along
  it, the number sits above the line and runs out of room across it. A small
  feature usually wants the arrows out and the number left alone. "Fits"
  asks for a little clear line between the arrows rather than merely for
  room, because two that meet exactly tip to tail read as one solid diamond.
  The arrow *tips* never move -- they are the points the number is of -- and
  the drawn line runs past them so the turned-out arrows have something to
  sit on. The measured ends and the stroked ends are different things.
- **A tool says per click whether it wants an object or a point**
  (`CanvasTool.WantsEntity`). A radius dimension takes a circle and then a
  point, so it cannot be answered once from what kind of tool the canvas is
  holding. Draw tools never want one and entity tools always do; this is the
  only place it changes mid-tool.
- **Filleting two segments of one polyline edits it in place.** The corner
  vertex becomes the two tangent points and the arc rides between them as a
  bulge, so a rounded rectangle is still one polyline with the handle the
  file knows it by. This is what explode was wrongly thought to be a
  prerequisite for. The bulge's sign is the turn's sign -- get it wrong and
  the arc bulges out of the shape with both its ends still exactly right,
  which is why there is a render test for it.
- **Dimensions go on a `Dimensions` layer, made green on demand.** The layer
  and the first dimension are one `Composite` command, so undo takes both.
  Annotation is not geometry: it belongs where it can be turned off in one
  go, whatever layer is current.
- **Layers are edited through commands too** (`AddLayer`, `DeleteLayer`,
  `ChangeLayer`). An entity names its layer by *position*, so removing one
  renumbers every entity in every layout, inside every block, and in every
  viewport's freeze set -- that is `LayerTable`'s job and it lives in one
  place. A layer with anything on it cannot be deleted; deciding whether the
  user's geometry is erased or moved is not a delete key's call.
- **Recolouring a layer recolours what was following it.** ByLayer is
  resolved at import, so an entity holds the colour rather than a reference
  to it, and a layer whose swatch disagreed with its geometry would be
  lying. `ChangeLayer` restyles exactly the entities still drawn in the
  layer's *old* style -- which is the set that was following it; anything
  given a colour of its own differs from it and keeps what it was given.
- **`CadCanvas.Mode` is the single answer to "what does a left click do".**
  It is an enum and not a set of flags on purpose: the version with a tool
  flag and a separate zoom-window flag let both be set, which lit two toolbar
  buttons while only one of them decided anything. The toolbar is redrawn
  from `ModeChanged` rather than set alongside the canvas.
- Curves with no closed form re-sample per frame from
  `EmitContext.PixelsPerUnit` rather than being flattened at import, and from
  `PickContext.Tolerance` when they are being picked.
- **A coordinate is a bare number; `Drawing.Units` says what it counts.**
  Same as DWG's INSUNITS, and read from it on import. Changing the unit
  relabels the drawing rather than rescaling it. Conversion happens only when
  a length is *typed*, so `2in` lands correctly in a millimetre drawing.
  A file that names no unit, or one not modelled, reads as `Unitless` and is
  **reported** -- it used to fall back to millimetres, which put a unit on
  the readout that nothing in the file had said.
- **A text style with a fixed height wins over the entity's.** Non-zero
  `TextStyle.Height` means AutoCAD never asked, so whatever number is on the
  entity is not the answer. Getting this backwards drew thirty-seven of
  thirty-eight labels at the wrong size in one ordinary sample file, and
  dropped text whose height was legitimately zero.
- **A DWG names a font *file*, not a family.** `arialn.ttf` is "Arial
  Narrow", `times.ttf` is "Times New Roman". `FontResolver.ResolveFile` is a
  shell-installed hook, like `TextMetrics.Measure`, because only the shell
  owns a font stack. Anything that will not resolve is substituted *and
  reported*; silently handing WPF a family nobody has is a drawing in the
  wrong typeface with a status bar saying nothing. Extension-less `TXT`,
  `SIMPLEX` and `romanc` are SHX, not TrueType.
- **Culling and picking go through `Layout.Index`,** a BVH. It answers with
  positions into the entity list and callers sort them, because entity order
  is painting order.
- **Toolbar icons are path data on a 24x24 grid,** stroked with the button's
  own `Foreground` and never filled, so one set serves both backgrounds and
  any DPI. A button is a `Tag` and a tooltip; the template does the rest.
  `IconTests` reads the references straight out of `MainWindow.xaml`, because
  a `StaticResource` that resolves to nothing stops the app from starting.

## Testing

Render tests write a DWG, read it back, draw it off screen through the real
canvas and sink, then assert what is painted **at world coordinates**. Three
bugs so far were invisible to the object model and only showed up in pixels.

The recurring trap: a probe placed where some *other* entity also passes will
pass for the wrong reason. Take probes off-axis where a fixture puts geometry
on the axes. `FREEDWG_TEST_RENDERS=<dir> dotnet test` dumps every frame to PNG.

## Notes

- Running the app locks its output DLLs; a build will fail with MSB3027 until
  the window is closed. Ask before killing it.
- Fixture DWGs are generated at test time, not committed. `.gitignore` blocks
  `*.dwg`/`*.dxf` outside `tests/fixtures/`.
