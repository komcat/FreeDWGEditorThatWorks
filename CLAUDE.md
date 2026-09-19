# FreeDWG Editor

A 2D DWG viewer, on its way to being an editor. WPF on .NET 10. DWG and DXF
parsing is done by [ACadSharp](https://github.com/DomCR/ACadSharp) (MIT);
this codebase is the scene model, the renderer and the shell.

```
dotnet build FreeDWGEditorThatWorks.slnx
dotnet test                 # 126 tests, ~0.5s
```

`tests/FreeDwg.Tests/README.md` explains how the render tests work and how to
debug one. Read it before changing anything they cover.

## Layout

```
src/FreeDwg.Core/          net10.0          scene model, geometry, renderer
src/FreeDwg.Interop.Acad/  net10.0          ACadSharp -> scene  (the only project that sees ACadSharp)
src/FreeDWGEditorThatWorks/ net10.0-windows WPF shell, canvas, WPF sink
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
  the render loop needs no type switch and the sink can be retargeted.
- **Styles are resolved at import.** ByLayer is gone by the time Core sees an
  entity; ByBlock survives as a `StyleInheritance` flag because it depends on
  which INSERT is drawing it.
- **`SourceHandle` is carried on every entity, layer, block and layout** so
  that saving can apply deltas to the original document instead of
  regenerating it. See the handoff doc; this is the whole basis of not
  destroying data we do not model.
- Curves with no closed form re-sample per frame from
  `EmitContext.PixelsPerUnit` rather than being flattened at import.

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
