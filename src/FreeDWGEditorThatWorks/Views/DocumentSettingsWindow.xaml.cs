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
    private bool _ready;

    public DocumentSettingsWindow(SceneDrawing drawing, Action changed)
    {
        InitializeComponent();

        _drawing = drawing;
        _changed = changed;

        UnitsBox.ItemsSource = Units.All
            .Select(units => new { Name = Units.Name(units), Value = units })
            .ToList();

        UnitsBox.SelectedValue = drawing.Units;
        PrecisionSlider.Value = drawing.LinearPrecision;

        _ready = true;
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
    private void ShowPrecision() =>
        PrecisionText.Text = Units.Describe(1234.56789, _drawing.Units, _drawing.LinearPrecision);

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
