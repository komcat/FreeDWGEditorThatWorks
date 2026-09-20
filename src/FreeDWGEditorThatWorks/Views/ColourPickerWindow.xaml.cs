using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeDwg.Core.Styling;
using FreeDWGEditorThatWorks.ViewModels;

namespace FreeDWGEditorThatWorks.Views;

/// <summary>
/// Picks a colour from a palette, or by typing one.
/// </summary>
public partial class ColourPickerWindow : Window
{
    /// <summary>Stops the hex field and the swatches fighting over the preview.</summary>
    private bool _updating;

    public ColourPickerWindow(Rgb current)
    {
        InitializeComponent();

        Chosen = current;

        Swatches.ItemsSource = ColourPalette.Swatches
            .Select(colour => new Swatch(colour))
            .ToList();

        Show(current);
    }

    /// <summary>The colour picked, meaningful once the dialog returns true.</summary>
    public Rgb Chosen { get; private set; }

    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Rgb colour }) Show(colour);
    }

    private void OnHexChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;

        // Typed freely, so most keystrokes are half a colour. Showing the
        // last good one rather than complaining is what makes it usable.
        if (StyleChoices.TryColour(HexBox.Text, out var colour)) Show(colour, echo: false);
    }

    private void Show(Rgb colour, bool echo = true)
    {
        Chosen = colour;

        _updating = true;
        try
        {
            if (echo) HexBox.Text = $"#{colour.Packed:X6}";
        }
        finally
        {
            _updating = false;
        }

        Preview.Background = PropertyRow.Chip(colour);
        PreviewName.Text = StyleChoices.ColourName(colour);
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    /// <summary>One cell of the palette.</summary>
    private sealed class Swatch
    {
        public Swatch(Rgb colour)
        {
            Colour = colour;
            Brush = PropertyRow.Chip(colour);
            Name = StyleChoices.ColourName(colour);
        }

        public Rgb Colour { get; }
        public Brush Brush { get; }
        public string Name { get; }
    }
}
