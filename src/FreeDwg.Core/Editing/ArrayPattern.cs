using FreeDwg.Core.Geometry;

namespace FreeDwg.Core.Editing;

/// <summary>
/// Where the copies of an array go, as one transform per copy.
/// </summary>
/// <remarks>
/// The original is the first item of every array and is not in the list:
/// an array of three by two is the selection plus five copies. Expressed as
/// transforms so that the array is made by the same clone-and-transform every
/// copy is, and previewed by pushing the same transforms onto the sink.
/// </remarks>
public abstract record ArrayPattern
{
    /// <summary>Beyond this an array is almost certainly a typo, and would bury the drawing.</summary>
    public const int MaxItems = 10_000;

    /// <summary>How many items, the original included.</summary>
    public abstract int Items { get; }

    /// <summary>
    /// One transform per copy. <paramref name="selection"/> is the bounds of
    /// what is being arrayed, which a polar array that keeps its items upright
    /// moves by its centre.
    /// </summary>
    public abstract IReadOnlyList<Mat3> Copies(Bounds2 selection);

    public bool IsValid => Items is > 1 and <= MaxItems && CheckShape();

    protected abstract bool CheckShape();
}

/// <summary>
/// Columns and rows, a fixed step apart. A negative spacing runs the array
/// left or down from the original; the angle turns the whole grid.
/// </summary>
public sealed record RectangularArray(int Columns, int Rows, double ColumnSpacing, double RowSpacing, double Angle = 0)
    : ArrayPattern
{
    public override int Items => Columns * Rows;

    protected override bool CheckShape() =>
        Columns >= 1 && Rows >= 1 &&
        (Columns == 1 || ColumnSpacing != 0) && (Rows == 1 || RowSpacing != 0) &&
        double.IsFinite(ColumnSpacing) && double.IsFinite(RowSpacing) && double.IsFinite(Angle);

    public override IReadOnlyList<Mat3> Copies(Bounds2 selection)
    {
        var turn = Mat3.Rotation(Angle);
        var copies = new List<Mat3>(Math.Max(0, Items - 1));

        for (int row = 0; row < Rows; row++)
        {
            for (int column = 0; column < Columns; column++)
            {
                if (row == 0 && column == 0) continue;
                copies.Add(Mat3.Translation(turn.TransformVector(new Vec2(column * ColumnSpacing, row * RowSpacing))));
            }
        }

        return copies;
    }
}

/// <summary>
/// Copies round a centre, spread over a fill angle. A whole turn spaces them
/// evenly with no copy landing on the original; anything less puts the last
/// one exactly at the end of the fill. A negative fill goes clockwise.
/// </summary>
/// <param name="RotateItems">
/// Whether each copy turns as it goes round, as bolts on a flange do, or
/// stays upright and only moves, as text and chairs round a table usually
/// should.
/// </param>
public sealed record PolarArray(Vec2 Center, int Count, double FillAngle, bool RotateItems = true) : ArrayPattern
{
    public override int Items => Count;

    protected override bool CheckShape() =>
        Count >= 2 && FillAngle != 0 && Math.Abs(FillAngle) <= ArcMath.TwoPi + 1e-9 && double.IsFinite(FillAngle);

    /// <summary>Whether the fill goes all the way round.</summary>
    public bool IsFullCircle => Math.Abs(Math.Abs(FillAngle) - ArcMath.TwoPi) < 1e-9;

    /// <summary>The angle between neighbouring items.</summary>
    public double Step => IsFullCircle ? FillAngle / Count : FillAngle / (Count - 1);

    public override IReadOnlyList<Mat3> Copies(Bounds2 selection)
    {
        var anchor = selection.IsEmpty ? Center : selection.Center;
        var copies = new List<Mat3>(Math.Max(0, Count - 1));

        for (int i = 1; i < Count; i++)
        {
            var turn = Mat3.RotationAbout(i * Step, Center);
            copies.Add(RotateItems ? turn : Mat3.Translation(turn.Transform(anchor) - anchor));
        }

        return copies;
    }
}
