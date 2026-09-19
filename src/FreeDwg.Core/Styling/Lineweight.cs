namespace FreeDwg.Core.Styling;

/// <summary>
/// A plot line width in hundredths of a millimetre, which is how DWG stores it.
/// ByLayer/ByBlock/Default have already been resolved away by the importer, so
/// every value here is a concrete width.
/// </summary>
public readonly struct Lineweight : IEquatable<Lineweight>
{
    /// <summary>Width in 1/100 mm. Zero means "thinnest the device can draw".</summary>
    public readonly short Hundredths;

    public Lineweight(short hundredths) { Hundredths = hundredths; }

    /// <summary>AutoCAD's out-of-the-box default plot width, 0.25 mm.</summary>
    public static readonly Lineweight Default = new(25);

    /// <summary>Hairline: one device pixel regardless of zoom.</summary>
    public static readonly Lineweight Thinnest = new(0);

    public double Millimeters => Hundredths / 100.0;

    public bool Equals(Lineweight other) => Hundredths == other.Hundredths;
    public override bool Equals(object? obj) => obj is Lineweight l && Equals(l);
    public override int GetHashCode() => Hundredths;
    public override string ToString() => $"{Millimeters:0.00} mm";
}
