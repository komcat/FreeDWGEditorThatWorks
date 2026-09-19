# FreeDWG Editor

A 2D DWG viewer, on its way to being an editor. WPF on .NET 10. DWG and DXF
parsing is done by [ACadSharp](https://github.com/DomCR/ACadSharp) (MIT);
this codebase is the scene model, the renderer and the shell.

```
dotnet build FreeDWGEditorThatWorks.slnx
dotnet test                 # 327 tests, ~1s
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
  `CornerRadius` is applied in `SetMode` and again whenever it changes, so a
  tool started before or after the number is typed behaves the same. Two
  copies of a setting drift, and the one that is forgotten is a value that
  silently does nothing.
- **The app is smoke tested by running it.** `StartupTests` launches the real
  executable and waits for a window. A `StaticResource` that resolves to
  nothing, or a handler that fires mid-XAML-parse, compiles clean, passes
  every other test, and kills the app before it draws. That has happened
  twice.
- **Entities offer their own snap points** (`CollectSnapPoints`), the third
  thing they do for themselves after emitting and hit testing.
- **`CadCanvas.Mode` is the single answer to "what does a left click do".**
  It is an enum and not a set of flags on purpose: the version with a tool
  flag and a separate zoom-window flag let both be set, which lit two toolbar
  buttons while only one of them decided anything. The toolbar is redrawn
  from `ModeChanged` rather than set alongside the canvas.
- Curves with no closed form re-sample per frame from
  `EmitContext.PixelsPerUnit` rather than being flattened at import, and from
  `PickContext.Tolerance` when they are being picked.
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
