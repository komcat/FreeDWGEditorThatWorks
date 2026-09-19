using FreeDwg.Core.Geometry;

namespace FreeDwg.Tests.Rendering;

/// <summary>
/// Asks a rendered frame what is drawn at a given point in drawing
/// coordinates.
/// </summary>
/// <remarks>
/// Probes are stated in world coordinates, never pixels, so a fixture can be
/// reasoned about from its own geometry. The recurring mistake when writing
/// them is choosing a point that some <em>other</em> entity also passes
/// through: such a probe passes for the wrong reason and silently stops
/// testing anything. Prefer points that only the geometry under test can
/// reach, and use <see cref="Dump"/> when one surprises you.
/// </remarks>
public sealed class Probe
{
    /// <summary>How far from the requested point to look, in pixels.</summary>
    private const int SearchRadius = 3;

    /// <summary>Summed channel difference from the background that counts as ink.</summary>
    private const int InkThreshold = 24;

    private readonly RenderResult _frame;

    public Probe(RenderResult frame) => _frame = frame;

    public RenderResult Frame => _frame;

    /// <summary>True when anything is painted within the search radius.</summary>
    public bool HasInk(Vec2 world) => SampleNear(world) is not null;

    /// <summary>The most saturated non-background pixel near a world point.</summary>
    public (byte R, byte G, byte B)? SampleNear(Vec2 world)
    {
        Vec2 screen = _frame.WorldToScreen(world);
        int cx = (int)Math.Round(screen.X);
        int cy = (int)Math.Round(screen.Y);

        (byte, byte, byte)? best = null;
        int bestDistance = 0;

        for (int y = cy - SearchRadius; y <= cy + SearchRadius; y++)
        {
            for (int x = cx - SearchRadius; x <= cx + SearchRadius; x++)
            {
                if (Sample(x, y) is not var (r, g, b, distance) || distance <= InkThreshold) continue;
                if (distance <= bestDistance) continue;

                bestDistance = distance;
                best = (r, g, b);
            }
        }

        return best;
    }

    /// <summary>One pixel, no neighbourhood search.</summary>
    public (byte R, byte G, byte B)? SampleExact(Vec2 world)
    {
        Vec2 screen = _frame.WorldToScreen(world);
        int x = (int)Math.Round(screen.X);
        int y = (int)Math.Round(screen.Y);

        if (Sample(x, y) is not var (r, g, b, distance) || distance <= InkThreshold) return null;
        return (r, g, b);
    }

    /// <summary>Fraction of sample points along a world segment that carry ink.</summary>
    public double InkRatioAlong(Vec2 a, Vec2 b, int samples = 400)
    {
        int ink = 0;
        for (int i = 0; i < samples; i++)
            if (SampleExact(Vec2.Lerp(a, b, i / (double)(samples - 1))) is not null)
                ink++;

        return ink / (double)samples;
    }

    /// <summary>Extent of the ink inside a world-space box, or null if there is none.</summary>
    public Bounds2 InkExtentIn(Vec2 min, Vec2 max, int steps = 160)
    {
        var bounds = Bounds2.Empty;

        for (int iy = 0; iy < steps; iy++)
        {
            for (int ix = 0; ix < steps; ix++)
            {
                double wx = min.X + (max.X - min.X) * ix / (steps - 1.0);
                double wy = min.Y + (max.Y - min.Y) * iy / (steps - 1.0);

                var point = new Vec2(wx, wy);
                if (SampleExact(point) is not null) bounds = bounds.Union(point);
            }
        }

        return bounds;
    }

    /// <summary>
    /// An ASCII map of the ink over a world-space window. Not used by the
    /// tests themselves; it is what turns "this probe failed" into "the
    /// geometry is fine and the probe is on the wrong point".
    /// </summary>
    public string Dump(Vec2 center, double halfWidth, double halfHeight, int columns = 61, int rows = 25)
    {
        var text = new System.Text.StringBuilder();

        for (int r = 0; r < rows; r++)
        {
            double wy = center.Y + halfHeight - 2 * halfHeight * r / (rows - 1);

            for (int c = 0; c < columns; c++)
            {
                double wx = center.X - halfWidth + 2 * halfWidth * c / (columns - 1);
                var sample = SampleExact(new Vec2(wx, wy));
                text.Append(sample is null ? '.' : Glyph(sample.Value));
            }

            text.Append("  y=").Append(wy.ToString("0.##")).AppendLine();
        }

        return text.ToString();
    }

    private static char Glyph((byte R, byte G, byte B) c) =>
        c.R > c.G && c.R > c.B ? 'R' :
        c.G > c.R && c.G > c.B ? 'G' :
        c.B > c.R ? 'B' : '#';

    private (byte R, byte G, byte B, int Distance)? Sample(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _frame.Width || y >= _frame.Height) return null;

        int i = y * _frame.Stride + x * 4;
        byte b = _frame.Pixels[i];
        byte g = _frame.Pixels[i + 1];
        byte r = _frame.Pixels[i + 2];

        int distance = Math.Abs(b - _frame.Background.B)
                     + Math.Abs(g - _frame.Background.G)
                     + Math.Abs(r - _frame.Background.R);

        return (r, g, b, distance);
    }
}
