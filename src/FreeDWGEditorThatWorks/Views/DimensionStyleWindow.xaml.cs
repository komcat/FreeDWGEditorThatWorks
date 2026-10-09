using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeDwg.Core.Scene;

namespace FreeDWGEditorThatWorks.Views;

/// <summary>
/// The sizes new dimensions are drawn with, and their overall scale.
/// </summary>
/// <remarks>
/// Applied on OK rather than as it is typed, unlike the drawing settings:
/// this one can resize every dimension in the drawing, which is one step on
/// the undo stack and wants to be taken once, deliberately.
/// </remarks>
public partial class DimensionStyleWindow : Window
{
    private readonly DrawingUnits _units;
    private bool _ready;

    public DimensionStyleWindow(DimensionSettings current, DrawingUnits units, int existing)
    {
        InitializeComponent();
        _units = units;

        string suffix = FreeDwg.Core.Scene.Units.Suffix(units);
        foreach (var label in new[] { TextHeightUnit, ArrowSizeUnit, ExtensionOffsetUnit, ExtensionBeyondUnit, TextGapUnit })
            label.Text = suffix;

        RestyleBox.Content = existing switch
        {
            0 => "Also resize dimensions already drawn (there are none)",
            1 => "Also resize the 1 dimension already drawn",
            _ => $"Also resize the {existing} dimensions already drawn",
        };
        RestyleBox.IsEnabled = existing > 0;
        RestyleBox.IsChecked = existing > 0;

        Show(current);
        _ready = true;
        Validate();
    }

    /// <summary>The settings chosen, once OK has been pressed.</summary>
    public DimensionSettings? Chosen { get; private set; }

    /// <summary>Whether the dimensions already drawn are to be resized as well.</summary>
    public bool RestyleExisting => RestyleBox.IsChecked == true;

    private void Show(DimensionSettings settings)
    {
        var sizes = settings.Sizes;

        TextHeightBox.Text = Format(sizes.TextHeight);
        ArrowSizeBox.Text = Format(sizes.ArrowSize);
        ExtensionOffsetBox.Text = Format(sizes.ExtensionOffset);
        ExtensionBeyondBox.Text = Format(sizes.ExtensionBeyond);
        TextGapBox.Text = Format(sizes.TextGap);
        DecimalsBox.Text = sizes.Decimals.ToString(CultureInfo.InvariantCulture);
        ScaleBox.Text = Format(settings.Scale);
    }

    private static string Format(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private void OnFieldChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready) Validate();
    }

    /// <summary>
    /// Reads every box, or says which one is wrong. OK is only offered when
    /// all of them are sizes, so a typo cannot be applied to forty dimensions.
    /// </summary>
    private DimensionSettings? Read(out string? problem)
    {
        // The first thing wrong is the one worth saying.
        string? wrong = null;

        double? Length(TextBox box, string name, bool allowZero)
        {
            if (FreeDwg.Core.Scene.Units.TryParseLength(box.Text, _units, out double value) &&
                (value > 0 || (allowZero && value == 0)))
                return value;

            wrong ??= allowZero ? $"{name} has to be a length of zero or more." : $"{name} has to be a length above zero.";
            return null;
        }

        var text = Length(TextHeightBox, "Text height", false);
        var arrow = Length(ArrowSizeBox, "Arrow size", false);
        var offset = Length(ExtensionOffsetBox, "Extension line gap", true);
        var beyond = Length(ExtensionBeyondBox, "Extension line overrun", true);
        var gap = Length(TextGapBox, "Text gap", true);

        if (!int.TryParse(DecimalsBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int decimals) ||
            decimals is < 0 or > 8)
            wrong ??= "Decimal places has to be a whole number from 0 to 8.";

        if (!double.TryParse(ScaleBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double scale) ||
            scale <= 0)
            wrong ??= "Overall scale has to be a number above zero.";

        problem = wrong;
        if (wrong is not null) return null;

        return new DimensionSettings(
            new DimensionStyle(text!.Value, arrow!.Value, offset!.Value, beyond!.Value, gap!.Value, _units, decimals),
            scale);
    }

    private void Validate()
    {
        var settings = Read(out string? problem);
        OkButton.IsEnabled = settings is not null;

        if (settings is not { } valid)
        {
            Summary.Text = problem;
            Summary.Foreground = System.Windows.Media.Brushes.Firebrick;
            return;
        }

        var drawn = valid.StyleIn(_units);
        string suffix = FreeDwg.Core.Scene.Units.Suffix(_units);

        Summary.ClearValue(TextBlock.ForegroundProperty);
        Summary.Text = valid.Scale == 1
            ? $"Drawn at {Format(drawn.TextHeight)} {suffix} text and {Format(drawn.ArrowSize)} {suffix} arrows."
            : $"Drawn at {Format(drawn.TextHeight)} {suffix} text and {Format(drawn.ArrowSize)} {suffix} arrows "
              + $"-- the sizes above times {Format(valid.Scale)}.";
    }

    private void OnIso(object sender, RoutedEventArgs e)
    {
        int decimals = int.TryParse(DecimalsBox.Text, out int typed) && typed is >= 0 and <= 8 ? typed : 2;
        Show(new DimensionSettings(DimensionStyle.ForUnits(_units, decimals), 1.0));
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Read(out _) is not { } settings) return;

        Chosen = settings;
        DialogResult = true;
    }
}
