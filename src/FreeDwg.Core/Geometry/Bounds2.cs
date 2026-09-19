namespace FreeDwg.Core.Geometry;

/// <summary>
/// An axis-aligned bounding rectangle in world units. The empty box is stored
/// inverted (min = +inf, max = -inf) so that <see cref="Union(Bounds2)"/> is a
/// well-behaved monoid and accumulating over zero entities yields Empty.
/// </summary>
public readonly struct Bounds2
{
    public readonly double MinX, MinY, MaxX, MaxY;

    private Bounds2(double minX, double minY, double maxX, double maxY)
    {
        MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
    }

    public static readonly Bounds2 Empty = new(
        double.PositiveInfinity, double.PositiveInfinity,
        double.NegativeInfinity, double.NegativeInfinity);

    public static Bounds2 FromPoint(Vec2 p) => new(p.X, p.Y, p.X, p.Y);

    public static Bounds2 FromCorners(Vec2 a, Vec2 b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    public static Bounds2 FromPoints(IEnumerable<Vec2> points)
    {
        var b = Empty;
        foreach (var p in points) b = b.Union(p);
        return b;
    }

    public bool IsEmpty => MinX > MaxX || MinY > MaxY;
    public double Width => IsEmpty ? 0 : MaxX - MinX;
    public double Height => IsEmpty ? 0 : MaxY - MinY;
    public Vec2 Min => new(MinX, MinY);
    public Vec2 Max => new(MaxX, MaxY);
    public Vec2 Center => IsEmpty ? Vec2.Zero : new((MinX + MaxX) * 0.5, (MinY + MaxY) * 0.5);

    public Bounds2 Union(Vec2 p) => new(
        Math.Min(MinX, p.X), Math.Min(MinY, p.Y),
        Math.Max(MaxX, p.X), Math.Max(MaxY, p.Y));

    public Bounds2 Union(Bounds2 o)
    {
        if (o.IsEmpty) return this;
        if (IsEmpty) return o;
        return new(Math.Min(MinX, o.MinX), Math.Min(MinY, o.MinY),
                   Math.Max(MaxX, o.MaxX), Math.Max(MaxY, o.MaxY));
    }

    public bool Intersects(Bounds2 o)
    {
        if (IsEmpty || o.IsEmpty) return false;
        return MinX <= o.MaxX && MaxX >= o.MinX && MinY <= o.MaxY && MaxY >= o.MinY;
    }

    public bool Contains(Vec2 p) =>
        !IsEmpty && p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;

    public Bounds2 Inflate(double amount) =>
        IsEmpty ? this : new(MinX - amount, MinY - amount, MaxX + amount, MaxY + amount);

    public override string ToString() => IsEmpty ? "<empty>" : $"[{Min} .. {Max}]";
}
