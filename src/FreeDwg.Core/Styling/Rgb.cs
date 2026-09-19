namespace FreeDwg.Core.Styling;

/// <summary>
/// A fully resolved 24-bit colour. Core never sees AutoCAD colour indices or
/// ByLayer/ByBlock -- those are file-format concepts and are resolved during
/// import.
/// </summary>
public readonly struct Rgb : IEquatable<Rgb>
{
    public readonly byte R, G, B;

    public Rgb(byte r, byte g, byte b) { R = r; G = g; B = b; }

    public static readonly Rgb Black = new(0, 0, 0);
    public static readonly Rgb White = new(255, 255, 255);

    /// <summary>Relative luminance in 0..1 (Rec. 709 coefficients).</summary>
    public double Luminance => (0.2126 * R + 0.7152 * G + 0.0722 * B) / 255.0;

    public uint Packed => (uint)((R << 16) | (G << 8) | B);

    public bool Equals(Rgb other) => R == other.R && G == other.G && B == other.B;
    public override bool Equals(object? obj) => obj is Rgb c && Equals(c);
    public override int GetHashCode() => (int)Packed;
    public static bool operator ==(Rgb a, Rgb b) => a.Equals(b);
    public static bool operator !=(Rgb a, Rgb b) => !a.Equals(b);
    public override string ToString() => $"#{Packed:X6}";
}
