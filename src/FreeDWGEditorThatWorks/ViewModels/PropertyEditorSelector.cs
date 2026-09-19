using System.Windows;
using System.Windows.Controls;

namespace FreeDWGEditorThatWorks.ViewModels;

/// <summary>
/// Chooses between typing a value and picking one from a list, per row.
/// </summary>
/// <remarks>
/// A single grid holds lengths, angles, switches, layer names, colours and
/// plot widths. The first two want a text box; the rest have a fixed set of
/// answers and want a list, because a field with a fixed set of answers
/// should not have to be spelled from memory -- misspelling one is the
/// commonest way to have an edit quietly refused.
/// </remarks>
public sealed class PropertyEditorSelector : DataTemplateSelector
{
    public DataTemplate? Text { get; set; }
    public DataTemplate? Choice { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container) =>
        item is PropertyRow { HasChoices: true } ? Choice : Text;
}
