using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Scene;

/// <summary>A drawing layer. Visibility is mutable so the UI can toggle it without reimporting.</summary>
public sealed class Layer
{
    public Layer(string name) { Name = name; }

    public string Name { get; }
    public Rgb Color { get; init; } = Rgb.White;
    public Lineweight Lineweight { get; init; } = Lineweight.Default;

    /// <summary>Dash pattern entities on this layer inherit when theirs is ByLayer.</summary>
    public Linetype? Linetype { get; init; }

    public bool IsOn { get; set; } = true;
    public bool IsFrozen { get; set; }
    public bool IsLocked { get; set; }

    /// <summary>Handle of the originating DWG record, for delta-save later.</summary>
    public ulong SourceHandle { get; init; }

    public bool IsVisible => IsOn && !IsFrozen;

    /// <summary>
    /// The resolved style an entity drawn on this layer takes. Styles are
    /// resolved at import, so an entity created here has to be given one
    /// outright rather than left saying ByLayer.
    /// </summary>
    public DisplayStyle Style => new(Color, Lineweight, Linetype);

    public override string ToString() => Name;
}
