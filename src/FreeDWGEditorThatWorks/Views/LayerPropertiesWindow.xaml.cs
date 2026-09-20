using System.Windows;
using System.Windows.Media;
using FreeDwg.Core.Commands;
using FreeDwg.Core.Styling;
using FreeDWGEditorThatWorks.ViewModels;

namespace FreeDWGEditorThatWorks.Views;

/// <summary>
/// Everything a layer is, in one dialog: what it is called, what it is drawn
/// in, and whether it is showing.
/// </summary>
/// <remarks>
/// It edits a <see cref="LayerProperties"/> rather than a
/// <see cref="FreeDwg.Core.Scene.Layer"/>, so a cancelled dialog cannot have
/// changed anything and the accepted result goes back through the command
/// stack like every other edit.
/// </remarks>
public partial class LayerPropertiesWindow : Window
{
    private Rgb _colour;

    public LayerPropertiesWindow(string title, LayerProperties properties)
    {
        InitializeComponent();

        Title = title;
        NameBox.Text = properties.Name;

        WeightBox.ItemsSource = StyleChoices.Weights(properties.Lineweight);
        WeightBox.Text = StyleChoices.WeightName(properties.Lineweight);

        OnBox.IsChecked = properties.IsOn;
        FrozenBox.IsChecked = properties.IsFrozen;
        LockedBox.IsChecked = properties.IsLocked;

        Result = properties;
        ShowColour(properties.Color);
    }

    /// <summary>What was chosen, meaningful once the dialog returns true.</summary>
    public LayerProperties Result { get; private set; }

    private void ShowColour(Rgb colour)
    {
        _colour = colour;

        var brush = new SolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B));
        brush.Freeze();

        ColourChip.Background = brush;
        ColourName.Text = StyleChoices.ColourName(colour);
    }

    private void OnPickColour(object sender, RoutedEventArgs e)
    {
        var picker = new ColourPickerWindow(_colour) { Owner = this };
        if (picker.ShowDialog() == true) ShowColour(picker.Chosen);
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();

        // A layer with no name cannot be told apart in the list or written to
        // a file, so the dialog simply will not close on one.
        if (name.Length == 0)
        {
            NameBox.Focus();
            NameBox.SelectAll();
            return;
        }

        // A width that does not parse leaves the one it had, rather than
        // silently becoming the thinnest line in the drawing.
        var weight = StyleChoices.TryWeight(WeightBox.Text, out var parsed) ? parsed : Result.Lineweight;

        Result = Result with
        {
            Name = name,
            Color = _colour,
            Lineweight = weight,
            IsOn = OnBox.IsChecked == true,
            IsFrozen = FrozenBox.IsChecked == true,
            IsLocked = LockedBox.IsChecked == true,
        };

        DialogResult = true;
    }
}
