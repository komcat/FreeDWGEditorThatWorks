using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace FreeDWGEditorThatWorks.ViewModels;

/// <summary>
/// One line of the properties panel: a name, a value as text, and what to do
/// when the value is edited.
/// </summary>
/// <remarks>
/// Text rather than a typed value because the panel holds a lengths, angles,
/// layer names and switches side by side, and a grid of strings with a parser
/// per row is a great deal less machinery than a type hierarchy for four
/// kinds of field.
/// <para>
/// A row that fails to parse puts the old text back rather than leaving the
/// drawing and the panel disagreeing, which is the one outcome that would
/// make the panel untrustworthy.
/// </para>
/// </remarks>
public sealed class PropertyRow : INotifyPropertyChanged
{
    private readonly Func<string, bool>? _apply;
    private string _value;

    public PropertyRow(string category, string name, string description, string value,
        Func<string, bool>? apply = null)
    {
        Category = category;
        Name = name;
        Description = description;
        _value = value;
        _apply = apply;
    }

    /// <summary>The heading this row sits under.</summary>
    public string Category { get; }

    public string Name { get; }

    /// <summary>Shown under the grid for whichever row is selected.</summary>
    public string Description { get; }

    public bool IsReadOnly => _apply is null;

    public string Value
    {
        get => _value;
        set
        {
            if (value == _value) return;

            if (_apply is null || !_apply(value))
            {
                // Put the old text back. Raised even though the field did not
                // change, because the grid is showing the rejected text and
                // needs to be told to go and re-read it.
                Notify();
                return;
            }

            _value = value;
            Notify();
        }
    }

    /// <summary>Updates the displayed text without running the setter.</summary>
    public void Refresh(string value)
    {
        if (value == _value) return;

        _value = value;
        Notify();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify([CallerMemberName] string name = nameof(Value)) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));

    // ---- the parsers the rows share ---------------------------------------

    /// <summary>
    /// Invariant culture throughout: a drawing typed on a machine that writes
    /// 7,5 and one that writes 7.5 have to mean the same thing.
    /// </summary>
    public static bool TryNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    public static string Number(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    public static bool TryFlag(string text, out bool value)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "yes" or "true" or "on" or "1": value = true; return true;
            case "no" or "false" or "off" or "0": value = false; return true;
            default: value = false; return false;
        }
    }

    public static string Flag(bool value) => value ? "Yes" : "No";
}
