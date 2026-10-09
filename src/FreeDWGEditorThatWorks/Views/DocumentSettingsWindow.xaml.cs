using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FreeDwg.Core.Scene;
using SceneDrawing = FreeDwg.Core.Scene.Drawing;

namespace FreeDWGEditorThatWorks.Views;

/// <summary>
/// What the drawing is measured in, and how many decimals to show.
/// </summary>
/// <remarks>
/// Applied as it is changed rather than on a Close button, so the readout
/// behind the dialog moves with it and the effect of a setting is visible
/// while it is being chosen.
/// </remarks>
public partial class DocumentSettingsWindow : Window
{
    private readonly SceneDrawing _drawing;
    private readonly Action _changed;
    private readonly Action<Window>? _editDimensions;
    private bool _ready;

    /// <param name="editDimensions">
    /// Opens the dimension style dialog over this one. The shell's to do,
    /// since it is the shell that runs the command it ends in.
    /// </param>
    public DocumentSettingsWindow(SceneDrawing drawing, Action changed, Action<Window>? editDimensions = null)
    {
        InitializeComponent();

        _drawing = drawing;
        _changed = changed;
        _editDimensions = editDimensions;

        UnitsBox.ItemsSource = Units.All
            .Select(units => new { Name = Units.Name(units), Value = units })
            .ToList();

        UnitsBox.SelectedValue = drawing.Units;
        PrecisionSlider.Value = drawing.LinearPrecision;

        _ready = true;
        ShowPrecision();
    }

    private void OnDimensionStyle(object sender, RoutedEventArgs e)
    {
        _editDimensions?.Invoke(this);
        ShowPrecision();
    }

    private void OnUnitsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || UnitsBox.SelectedValue is not DrawingUnits units) return;

        _drawing.Units = units;
        ShowPrecision();
        _changed();
    }

    private void OnPrecisionChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;

        _drawing.LinearPrecision = (int)PrecisionSlider.Value;
        ShowPrecision();
        _changed();
    }

    /// <summary>Shows the setting as the number it will actually produce.</summary>
    private void ShowPrecision()
    {
        PrecisionText.Text = Units.Describe(1234.56789, _drawing.Units, _drawing.LinearPrecision);

        // The dimension sizes follow the units until they are set, so this
        // line changes with the units box as well as with the dialog.
        var style = DimensionStyle.For(_drawing);
        var scale = DimensionSettings.For(_drawing).Scale;
        DimensionText.Text = $"{Units.Describe(style.TextHeight, _drawing.Units, 4)} text, "
            + $"{Units.Describe(style.ArrowSize, _drawing.Units, 4)} arrows"
            + (scale == 1 ? "" : $", at a scale of {scale:0.####}");
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
