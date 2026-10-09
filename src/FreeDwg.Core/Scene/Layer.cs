using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Scene;

/// <summary>A drawing layer. Visibility is mutable so the UI can toggle it without reimporting.</summary>
public sealed class Layer
{
    public Layer(string name) { Name = name; }

    /// <summary>
    /// Editable, along with the three below it: a layer is a thing the user
    /// renames and recolours, and every change to one goes through
    /// <c>ChangeLayer</c> so that it can be undone like anything else.
    /// </summary>
    public string Name { get; set; }

    public Rgb Color { get; set; } = Rgb.White;
    public Lineweight Lineweight { get; set; } = Lineweight.Default;

    /// <summary>Dash pattern entities on this layer inherit when theirs is ByLayer.</summary>
    public Linetype? Linetype { get; set; }

    public bool IsOn { get; set; } = true;
    public bool IsFrozen { get; set; }
    public bool IsLocked { get; set; }

    /// <summary>
    /// Handle of the DWG record this is. Settable because a layer made here
    /// has none until the first save gives it one, and the second save has to
    /// find the record the first one wrote rather than write another.
    /// </summary>
    public ulong SourceHandle { get; set; }

    public bool IsVisible => IsOn && !IsFrozen;

    /// <summary>
    /// The resolved style an entity drawn on this layer takes. Styles are
    /// resolved at import, so an entity created here has to be given one
    /// outright rather than left saying ByLayer.
    /// </summary>
    public DisplayStyle Style => new(Color, Lineweight, Linetype);

    public override string ToString() => Name;
}
