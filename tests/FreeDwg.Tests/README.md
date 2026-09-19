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
  inside it, which is what `SelectionRenderTests` watches for.

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
