namespace FreeDwg.Core.Styling;

/// <summary>
/// A dash pattern, in drawing units. Positive entries are dashes, negative
/// entries are gaps, and zero is a dot.
/// </summary>
/// <remarks>
/// DWG also allows text and shape elements embedded in a linetype (the little
/// "GAS" labels along a gas line, for instance). Those are dropped here and
/// only the dash lengths are kept, so such a line draws as its dashes alone.
/// </remarks>
public sealed class Linetype
{
    public static readonly Linetype Continuous = new("Continuous", Array.Empty<double>());

    public Linetype(string name, IReadOnlyList<double> pattern)
    {
        Name = name;
        Pattern = pattern;

        double total = 0;
        foreach (double segment in pattern) total += Math.Abs(segment);
        PatternLength = total;
    }

    public string Name { get; }
    public IReadOnlyList<double> Pattern { get; }

    /// <summary>Sum of the absolute segment lengths, one full cycle of the pattern.</summary>
    public double PatternLength { get; }

    /// <summary>True when there is nothing to dash and the line strokes solid.</summary>
    public bool IsContinuous => Pattern.Count == 0 || PatternLength <= 0;

    public override string ToString() => IsContinuous ? Name : $"{Name} ({Pattern.Count} segments)";
}
