using FreeDwg.Core.Editing;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;

namespace FreeDwg.Core.Commands;

/// <summary>
/// Changes the sizes and the scale new dimensions are drawn with.
/// </summary>
/// <remarks>
/// A command rather than a setting poked into the drawing, for two reasons:
/// it is undoable like any other change, and it marks the drawing modified --
/// the settings are written to the file's DIM variables on save, so changing
/// them is a change to the file.
/// <para>
/// It reports nothing to the change log. The DIM variables live in the file's
/// header, which a save brings into line with the drawing every time by
/// comparing the two; there is no handle to name.
/// </para>
/// </remarks>
public sealed class ChangeDimensionSettings : IEditCommand
{
    private readonly DimensionSettings? _after;
    private DimensionSettings? _before;

    public ChangeDimensionSettings(DimensionSettings? settings) => _after = settings;

    public string Name => "Dimension style";

    public void Apply(Drawing drawing)
    {
        _before = drawing.Dimensions;
        drawing.Dimensions = _after;
    }

    public void Undo(Drawing drawing) => drawing.Dimensions = _before;

    public void Describe(ChangeLog log) { }

    /// <summary>
    /// The settings change, and optionally every dimension already drawn
    /// brought into line with it, as one step on the undo stack.
    /// </summary>
    /// <remarks>
    /// Only dimensions drawn here are restyled. An imported DIMENSION is a
    /// picture of itself in a block until something regenerates it, and
    /// stretching a picture's text is not resizing a dimension.
    /// </remarks>
    public static IEditCommand Including(Drawing drawing, DimensionSettings settings, bool restyleExisting)
    {
        var change = new ChangeDimensionSettings(settings);
        if (!restyleExisting) return change;

        var style = settings.StyleIn(drawing.Units);
        var parts = new List<IEditCommand> { change };

        foreach (var layout in drawing.Layouts)
        {
            var pairs = layout.Entities
                .OfType<SDimension>()
                .Where(dimension => dimension.DimensionStyle != style)
                .Select(dimension =>
                {
                    var copy = (SDimension)dimension.Clone();
                    copy.DimensionStyle = style;
                    return ((SceneEntity)dimension, (SceneEntity)copy);
                })
                .ToList();

            if (pairs.Count > 0) parts.Add(new ReplaceEntities(layout, EditPlan.Swap(pairs), "Restyle dimensions"));
        }

        return parts.Count == 1 ? change : new Composite("Dimension style", parts.ToArray());
    }

    /// <summary>How many dimensions <see cref="Including"/> would restyle.</summary>
    public static int CountRestylable(Drawing drawing) =>
        drawing.Layouts.Sum(layout => layout.Entities.OfType<SDimension>().Count());
}

/// <summary>
/// Changes the height and typeface new text is placed with.
/// </summary>
/// <remarks>
/// A command for the reasons <see cref="ChangeDimensionSettings"/> is one:
/// it is undoable, it marks the drawing modified, and a save writes it --
/// to TEXTSIZE and the current text style -- so changing it is a change to
/// the file. Like that one it names no handle: the header is brought into
/// line on every save by comparison.
/// </remarks>
public sealed class ChangeTextSettings : IEditCommand
{
    private readonly TextSettings _after;
    private TextSettings _before;

    public ChangeTextSettings(TextSettings settings) => _after = settings;

    public string Name => "Text style";

    public void Apply(Drawing drawing)
    {
        _before = drawing.Text;
        drawing.Text = _after;
    }

    public void Undo(Drawing drawing) => drawing.Text = _before;

    public void Describe(ChangeLog log) { }
}
