# Next session

Written 19 September 2026, at commit `c99423f`. This is a short note about
where today stopped and what to pick up. The map is
[EDITOR-HANDOFF.md](EDITOR-HANDOFF.md) — architecture, milestones and the
traps already paid for live there and are current. This file goes stale on
purpose; delete it when it has been used.

## The tree, right now

```
dotnet build FreeDWGEditorThatWorks.slnx
dotnet test                 # 609 tests, ~1s, all green
```

The app launches and was driven by hand today: drawing, grips, dimensions,
fillet on a rectangle, the layers panel. Working tree clean, three commits
on `master`, no remote.

| | |
|---|---|
| `4cd4e0d` | grips, dimensions (linear, aligned, radius, diameter), editable layers, fillet and chamfer on a polyline corner |
| `e7025df` | the layers panel moves to the right of the drawing and gets a name filter |
| `c99423f` | units, text height and fonts, as real files actually state them |

## Start here

**Dimensions from real files do not draw at all in three of the samples.**
This is the thread to pull, and it is the one that connects to what landed
today. `DwgLoader.ConvertDimension` draws a DIMENSION by instancing its
anonymous block, and returns `null` when there is not one — which is every
dimension in `architectural_example-imperial.dwg` (14),
`civil_example-imperial.dwg` (13) and `mechanical_example-imperial.dwg` (17).
They are simply missing from the drawing, with only the unsupported tally to
say so.

There is now an `SDimension` that lays one out from its definition points, so
the fix is to build one of those when the block is absent rather than
dropping the entity. That also starts closing the gap the handoff calls out
for E5: imported dimensions and drawn ones are two different things in the
model, and a save has to reconcile them.

Then, in the order the sample folder justifies:

1. **POINT.** 1,602 of them across fourteen files, 1,321 in one. Far and away
   the commonest thing we throw away, and the cheapest to add — it is a
   marker at a coordinate, with PDMODE deciding which marker.
2. **ATTDEF** (73) — block attribute definitions, which is why some title
   blocks come in empty.
3. **MULTILEADER** (36) and **LEADER** (22) — annotation, and a leader is
   most of the way to the radial dimension layout that already exists.

## How today's bugs were found, and how to find the next ones

By opening other people's drawings and printing what came back. That is now
a committed tool rather than a thing to rebuild:

```
set FREEDWG_SAMPLES=C:\Users\komgr\OneDrive\Desktop\example dwg
dotnet test --filter Survey -l "console;verbosity=detailed"
```

It reports, per file, the unit, the extents, the fonts actually resolved, the
diagnostics, and then the unsupported tally ranked across the whole folder.
It asserts nothing. It does not need to: three separate import bugs were
invisible to every fixture in the suite and obvious within a minute of
reading its output, because a fixture written here only ever contains what
was thought of.

Worth repeating it against a different folder — the fourteen files are all
Autodesk samples and Western-European in origin. Nothing has been opened yet
that uses a CJK bigfont, a unit other than mm/inch/metre, or a drawing whose
INSUNITS disagrees with its geometry.

## Things not to be surprised by

- **SHX fonts are Arial.** `romans`, `simplex` and `txt` are in half the
  samples. The text is legible and correctly placed but the wrong shape.
  Stroking them means parsing `.shx`, which is a project of its own.
- **No annotative scaling.** `architectural_-_annotation_scaling_and_multileaders.dwg`
  exists to test exactly that and we ignore it. Text on an annotative style
  draws at its paper height, so it will look wrong at model scale.
- **No DIMSCALE.** Read from no file, applied to nothing. A drawing meant for
  1:100 gets dimension text sized for 1:1.
- **Imported dimensions are exploded blocks; drawn ones are `SDimension`.**
  Deliberate, and fine until E5.
- **Running the app locks its DLLs.** A build fails with MSB3027 until the
  window is closed.

## Loose ends from today, small

- The fillet/chamfer turn-out threshold is `2 × arrow + gap`, about 5.6 mm at
  ISO sizes. It looked right on a 5 mm chamfer; it has not been tried on a
  real drawing full of small features.
- `Units.TryParseLength` still converts `2in` as though a Unitless drawing
  were in millimetres. A guess, but refusing the entry is worse.
- The layers filter matches names only. Colour, state and in-use are what
  AutoCAD filters on as well.
