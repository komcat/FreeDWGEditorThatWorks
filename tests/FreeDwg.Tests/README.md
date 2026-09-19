# FreeDwg.Tests

```
dotnet test
```

Two kinds of test live here.

**`GeometryTests`** is ordinary arithmetic: de Boor against the Bézier a
clamped cubic reduces to, bulge sagitta, arc and ellipse bounds, matrix
composition order and inversion, camera round-trips. No WPF, no files.

**Everything else renders.** Each scenario writes a DWG with ACadSharp, reads
it back through `DwgLoader`, draws it off screen through the real `CadCanvas`
and `WpfDrawingSink`, and then asks the resulting pixels what is at a given
point *in drawing coordinates*.

That roundabout route is deliberate. Several of the bugs this suite exists to
catch were invisible to the object model and only showed up in pixels:

- arc sweep direction inverted, which turned every bulge inside out while
  endpoints and bounds stayed correct;
- a mirrored block flipping that sweep back again;
- dimensions losing their arrowheads, because an arrowhead is a `SOLID`.

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
