using System.Collections.Generic;
using System.Linq;
using FreeDwg.Core.Editing;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Snapping;
using FreeDwg.Core.Styling;
using FreeDwg.Core.Tools;
using FreeDWGEditorThatWorks.Controls;

namespace FreeDWGEditorThatWorks.ViewModels;

/// <summary>
/// Builds the rows of the properties panel from the canvas and whatever is
/// selected.
/// </summary>
/// <remarks>
/// Rebuilt wholesale rather than patched, because what belongs in it changes
/// completely with the selection: a circle and a line share almost no fields.
/// The panel is small enough that rebuilding it is cheaper than working out
/// which rows survived.
/// </remarks>
public static class PropertySource
{
    private const string Settings = "Editor";
    private const string General = "General";

    /// <summary>
    /// Shown where the selection disagrees. Asterisked the way AutoCAD marks
    /// it, so it reads as a state rather than as a value anyone typed.
    /// </summary>
    public const string Varies = "*varies*";

    /// <summary>
    /// The last entry of the colour list, which opens the picker. AutoCAD
    /// calls it Select Colour and puts it in the same place, so the habit
    /// transfers.
    /// </summary>
    public const string MoreColours = "More colours...";

    /// <summary>
    /// Asks for a colour, given the current one, and calls back with the
    /// answer if there is one.
    /// </summary>
    /// <remarks>
    /// A callback rather than a return value because the shell has to defer
    /// the dialog: opening a modal window while the grid is still committing
    /// the cell it was edited in is a way to hang WPF's input system. The
    /// panel therefore accepts the choice now and the colour arrives later.
    /// </remarks>
    public delegate void ColourChooser(Rgb current, Action<Rgb> chosen);

    public static List<PropertyRow> Build(CadCanvas canvas, ColourChooser? chooseColour = null)
    {
        var rows = new List<PropertyRow>();

        AddSettings(canvas, rows);
        AddSelection(canvas, rows, chooseColour);

        return rows;
    }

    private static void AddSettings(CadCanvas canvas, List<PropertyRow> rows)
    {
        rows.Add(new PropertyRow(Settings, "Fillet radius",
            "Radius of the arc a fillet inserts. Zero brings the two edges to a sharp corner. "
            + "With the fillet tool in hand you can also just type a number on the canvas.",
            PropertyRow.Number(canvas.FilletRadius),
            text =>
            {
                if (!PropertyRow.TryNumber(text, out double value) || value < 0) return false;

                canvas.FilletRadius = value;
                return true;
            }));

        rows.Add(new PropertyRow(Settings, "Chamfer distance",
            "How far back along each edge a chamfer cuts, which makes the cut itself 45 degrees "
            + "on a square corner. Its own number: a chamfer and a fillet are different measurements.",
            PropertyRow.Number(canvas.ChamferDistance),
            text =>
            {
                if (!PropertyRow.TryNumber(text, out double value) || value < 0) return false;

                canvas.ChamferDistance = value;
                return true;
            }));

        rows.Add(new PropertyRow(Settings, "New text height",
            "How tall new text is, in drawing units. Until it is set it follows the dimension text "
            + "height, so labels and dimensions come out the same size.",
            PropertyRow.Number(canvas.TextHeight),
            text =>
            {
                if (!PropertyRow.TryNumber(text, out double value) || value <= 0) return false;

                canvas.TextHeight = value;
                return true;
            }));

        var textSettings = canvas.Drawing?.Text ?? default;
        rows.Add(new PropertyRow(Settings, "New text font",
            "The typeface new text is placed in. Kept in the drawing as its current text style, so it is "
            + "saved with it.",
            TextChoices.FontName(textSettings.FontFamily),
            text => canvas.Drawing is null
                 || canvas.SetTextSettings(textSettings with { FontFamily = TextChoices.FamilyOf(text.Trim()) })
                 || true,
            choices: TextChoices.Fonts(textSettings.FontFamily)));

        rows.Add(new PropertyRow(Settings, "Ortho",
            "Holds each new point square with the one before it, so lines come out horizontal or vertical.",
            PropertyRow.Flag(canvas.Snapping.Ortho),
            text =>
            {
                if (!PropertyRow.TryFlag(text, out bool value)) return false;

                canvas.Snapping.Ortho = value;
                canvas.Redraw();
                return true;
            }));

        rows.Add(new PropertyRow(Settings, "Polar angle",
            "The angle between the rays polar tracking attracts to, in degrees. Forty-five gives "
            + "the diagonals as well as the axes; fifteen and thirty are the other common answers.",
            PropertyRow.Number(canvas.Snapping.PolarAngle),
            text =>
            {
                if (!PropertyRow.TryNumber(text, out double value) || value < Polar.MinIncrement || value > 180)
                    return false;

                canvas.Snapping.PolarAngle = value;
                canvas.Redraw();
                return true;
            }));

        rows.Add(new PropertyRow(Settings, "Polar relative",
            "Measures polar angles from the segment just drawn rather than from east, so the next "
            + "run turns by the angle rather than arriving at it. Falls back to east when nothing "
            + "has been drawn yet.",
            PropertyRow.Flag(canvas.Snapping.PolarRelative),
            text =>
            {
                if (!PropertyRow.TryFlag(text, out bool value)) return false;

                canvas.Snapping.PolarRelative = value;
                canvas.Redraw();
                return true;
            }));

        rows.Add(new PropertyRow(Settings, "Grid",
            "Shows the drawing grid, and snaps to it when nothing else is near the cursor.",
            PropertyRow.Flag(canvas.Snapping.Grid.IsVisible),
            text =>
            {
                if (!PropertyRow.TryFlag(text, out bool value)) return false;

                canvas.Snapping.Grid.IsVisible = value;
                canvas.Snapping.Modes = value
                    ? canvas.Snapping.Modes | SnapModes.Grid
                    : canvas.Snapping.Modes & ~SnapModes.Grid;

                canvas.Redraw();
                return true;
            }));

        rows.Add(new PropertyRow(Settings, "Grid spacing",
            "Grid spacing in drawing units. It steps up by tens as you zoom out, so the "
            + "lines never crowd together.",
            PropertyRow.Number(canvas.Snapping.Grid.Spacing),
            text =>
            {
                if (!PropertyRow.TryNumber(text, out double value) || value <= 0) return false;

                canvas.Snapping.Grid.Spacing = value;
                canvas.Redraw();
                return true;
            }));
    }

    private static void AddSelection(CadCanvas canvas, List<PropertyRow> rows, ColourChooser? chooseColour)
    {
        var selected = canvas.Selection.Ordered;
        if (selected.Count == 0 || canvas.Drawing is not { } drawing) return;

        if (selected.Count > 1)
        {
            AddShared(canvas, drawing, selected, rows, chooseColour);
            return;
        }

        var entity = selected[0];

        rows.Add(new PropertyRow(General, "Type", "What kind of object this is.", Describe(entity)));

        rows.Add(new PropertyRow(General, "Layer",
            "The layer this object is on. Pick another to move it there.",
            LayerName(drawing, entity.LayerIndex),
            text => MoveToLayer(canvas, drawing, entity, text),
            choices: drawing.Layers.Select(layer => layer.Name).ToList()));

        rows.Add(new PropertyRow(General, "Colour",
            "The colour this object draws in. \"By layer\" copies the layer's colour now rather "
            + "than following it later: ByLayer is resolved when a file is read, so the scene "
            + "holds a colour and not a link.",
            StyleChoices.ColourName(entity.Style.Color),
            text => text == MoreColours
                ? Choose(chooseColour, entity.Style.Color,
                    colour => SetColour(canvas, drawing, entity, $"#{colour.Packed:X6}"))
                : SetColour(canvas, drawing, entity, text),
            choices: WithPicker(StyleChoices.Colours(entity.Style.Color), chooseColour),
            swatch: PropertyRow.Chip(entity.Style.Color)));

        rows.Add(new PropertyRow(General, "Lineweight",
            "Plot width in millimetres. It only shows on screen while the Lineweights button is on; "
            + "otherwise everything strokes as a hairline, as AutoCAD does by default.",
            StyleChoices.WeightName(entity.Style.Lineweight),
            text =>
            {
                if (!StyleChoices.TryWeight(text, out var weight)) return false;

                return Edit(canvas, entity, copy =>
                {
                    copy.Style = copy.Style with { Lineweight = weight };
                    return true;
                });
            },
            choices: StyleChoices.Weights(entity.Style.Lineweight)));

        AddGeometry(canvas, entity, rows);
    }

    /// <summary>What several objects at once have in common.</summary>
    /// <remarks>
    /// Where a properties panel earns its keep: putting forty lines on
    /// another layer is one edit here and forty of them anywhere else. A
    /// field the selection does not agree on shows as varying rather than
    /// picking one of the answers to display, which would be a lie about
    /// thirty-nine of them.
    /// </remarks>
    private static void AddShared(CadCanvas canvas, Drawing drawing,
        IReadOnlyList<SceneEntity> selected, List<PropertyRow> rows, ColourChooser? chooseColour)
    {
        string kinds = selected.Select(Describe).Distinct().Count() == 1
            ? Describe(selected[0]).ToLowerInvariant() + "s"
            : "objects";

        rows.Add(new PropertyRow(General, "Selection",
            "How many objects are selected. Everything below is applied to all of them at once.",
            $"{selected.Count} {kinds}"));

        // Every entity has these three, whatever it is.
        var layers = drawing.Layers.Select(layer => layer.Name).ToList();

        rows.Add(new PropertyRow(General, "Layer",
            "The layer these objects are on. Picking one moves all of them there, in a single step.",
            Shared(selected, entity => LayerName(drawing, entity.LayerIndex), out bool sameLayer),
            // Choosing the marker itself is a no-op rather than a failure:
            // it is what the box already shows, and refusing it would flash
            // an error at someone who changed their mind.
            text => text == Varies
                 || EditAll(canvas, selected, copy => MoveCopyToLayer(drawing, copy, text), "Set layer"),
            choices: WithVaries(layers, sameLayer)));

        var colour = Shared(selected, entity => StyleChoices.ColourName(entity.Style.Color), out bool sameColour);

        rows.Add(new PropertyRow(General, "Colour",
            "The colour these objects draw in. Picking one sets all of them.",
            colour,
            text => text == Varies
                 || (text == MoreColours
                     ? Choose(chooseColour, selected[0].Style.Color, picked => EditAll(canvas, selected,
                         copy => SetCopyColour(drawing, copy, $"#{picked.Packed:X6}"), "Set colour"))
                     : EditAll(canvas, selected, copy => SetCopyColour(drawing, copy, text), "Set colour")),
            choices: WithPicker(WithVaries(StyleChoices.Colours(selected[0].Style.Color), sameColour), chooseColour),
            swatch: sameColour ? PropertyRow.Chip(selected[0].Style.Color) : null));

        var weight = Shared(selected, entity => StyleChoices.WeightName(entity.Style.Lineweight), out bool sameWeight);

        rows.Add(new PropertyRow(General, "Lineweight",
            "Plot width for all of these objects.",
            weight,
            text => text == Varies
                 || EditAll(canvas, selected, copy =>
                 {
                     if (!StyleChoices.TryWeight(text, out var value)) return false;

                     copy.Style = copy.Style with { Lineweight = value };
                     return true;
                 }, "Set lineweight"),
            choices: WithVaries(StyleChoices.Weights(selected[0].Style.Lineweight), sameWeight)));

        AddSharedGeometry(canvas, selected, rows);
    }

    /// <summary>
    /// The geometry rows that make sense across a selection: the ones that
    /// are a single figure rather than a position. Setting the radius of
    /// every circle at once is useful; setting every start point to the same
    /// coordinate would just stack them.
    /// </summary>
    private static void AddSharedGeometry(CadCanvas canvas,
        IReadOnlyList<SceneEntity> selected, List<PropertyRow> rows)
    {
        if (selected.All(entity => entity is SCircle))
        {
            rows.Add(SharedNumber(canvas, selected, "Circle", "Radius", "Radius of every selected circle.",
                entity => ((SCircle)entity).Radius,
                (copy, value) => { if (value <= 0) return false; ((SCircle)copy).Radius = value; return true; }));
            rows.Add(SharedNumber(canvas, selected, "Circle", "Diameter", "Diameter of every selected circle.",
                entity => ((SCircle)entity).Radius * 2,
                (copy, value) => { if (value <= 0) return false; ((SCircle)copy).Radius = value / 2; return true; }));
            return;
        }

        if (selected.All(entity => entity is SArc))
        {
            rows.Add(SharedNumber(canvas, selected, "Arc", "Radius", "Radius of every selected arc.",
                entity => ((SArc)entity).Radius,
                (copy, value) => { if (value <= 0) return false; ((SArc)copy).Radius = value; return true; }));
            return;
        }

        if (selected.All(entity => entity is SText))
        {
            rows.Add(SharedNumber(canvas, selected, "Text", "Height", "Cap height of every selected text.",
                entity => ((SText)entity).Height,
                (copy, value) => { if (value <= 0) return false; ((SText)copy).Height = value; return true; }));
            return;
        }

        if (selected.All(entity => entity is SDimension))
        {
            AddDimensionSizes(canvas, selected, rows);
            return;
        }

        if (selected.All(entity => entity is SEllipse))
        {
            AddEllipseSize(canvas, selected, rows);
            return;
        }

        if (selected.All(entity => entity is SPolyline polyline && RectangleShape.TryRead(polyline, out _)))
        {
            AddRectangleSize(canvas, selected, rows);
            return;
        }

        if (selected.All(entity => entity is SPolyline polyline && RegularPolygon.TryRead(polyline, out _)))
            AddPolygonSize(canvas, selected, rows);
    }

    /// <summary>A text's height and turn, which it has however many lines it runs to.</summary>
    private static void AddTextSize(CadCanvas canvas, SText text, List<PropertyRow> rows)
    {
        rows.Add(Number(canvas, "Text", "Height", "Capital-letter height in drawing units.",
            text.Height,
            (copy, v) =>
            {
                if (v <= 0) return false;

                // The gap between lines goes with the letters, or a taller
                // paragraph would print its lines on top of each other.
                var resized = (SText)copy;
                resized.LineStep *= v / resized.Height;
                resized.Height = v;
                return true;
            }));

        rows.Add(Number(canvas, "Text", "Rotation", "Rotation in degrees.",
            Degrees(text.Rotation),
            (copy, v) => { ((SText)copy).Rotation = Radians(v); return true; }));

        rows.Add(new PropertyRow("Text", "Font",
            "The typeface. (Standard) is the drawing's own text style. Saved as a text style in that face, "
            + "made the way AutoCAD makes one so it draws the same there.",
            TextChoices.FontName(text.FontFamily),
            value => Edit(canvas, text, copy =>
            {
                ((SText)copy).FontFamily = TextChoices.FamilyOf(value.Trim());
                return true;
            }),
            choices: TextChoices.Fonts(text.FontFamily)));

        rows.Add(new PropertyRow("Text", "Bold", "Heavier strokes.",
            PropertyRow.Flag(text.Bold),
            value => PropertyRow.TryFlag(value, out bool on) && Edit(canvas, text, copy =>
            {
                ((SText)copy).Bold = on;
                return true;
            })));

        rows.Add(new PropertyRow("Text", "Italic", "Slanted.",
            PropertyRow.Flag(text.Italic),
            value => PropertyRow.TryFlag(value, out bool on) && Edit(canvas, text, copy =>
            {
                ((SText)copy).Italic = on;
                return true;
            })));

        rows.Add(new PropertyRow("Text", "Justify",
            "Which point of the text sits on its insertion point. The point stays where it is and the "
            + "text arranges itself round it.",
            TextChoices.JustificationName(text.AnchorX, text.AnchorY),
            value =>
            {
                if (!TextChoices.TryJustification(value, out var x, out var y)) return false;
                return canvas.ReplaceText(text, text.Lines, TextFormat.Of(text) with { AnchorX = x, AnchorY = y });
            },
            choices: TextChoices.JustificationNames));
    }

    /// <summary>
    /// The full distances across an ellipse, which is how anyone sizes one;
    /// the major axis and ratio are how the file stores it. Both keep the
    /// centre and the angle.
    /// </summary>
    private static void AddEllipseSize(CadCanvas canvas, IReadOnlyList<SceneEntity> selected, List<PropertyRow> rows)
    {
        rows.Add(SharedNumber(canvas, selected, "Ellipse", "Width",
            "The full distance across along the axis that runs more nearly horizontal. The centre stays put.",
            entity => EllipseShape.Width((SEllipse)entity),
            (copy, value) => EllipseShape.Resize((SEllipse)copy, value, null)));

        rows.Add(SharedNumber(canvas, selected, "Ellipse", "Height",
            "The full distance across along the other axis. The centre stays put.",
            entity => EllipseShape.Height((SEllipse)entity),
            (copy, value) => EllipseShape.Resize((SEllipse)copy, null, value)));
    }

    /// <summary>
    /// A regular polygon's sizes. Any of them scales the whole polygon about
    /// its centre; they are three ways of saying the same size, and which is
    /// on the drawing being worked from is the one worth typing.
    /// </summary>
    private static void AddPolygonSize(CadCanvas canvas, IReadOnlyList<SceneEntity> selected, List<PropertyRow> rows)
    {
        const string category = "Polygon";

        static RegularPolygon Shape(SceneEntity entity)
        {
            RegularPolygon.TryRead((SPolyline)entity, out var polygon);
            return polygon;
        }

        static bool ResizeTo(SceneEntity copy, Func<RegularPolygon, double> radius) =>
            RegularPolygon.Resize((SPolyline)copy, radius(Shape(copy)));

        rows.Add(new PropertyRow(category, "Sides", "How many sides it has.",
            Shared(selected, entity => Shape(entity).Sides.ToString(System.Globalization.CultureInfo.InvariantCulture), out _)));

        rows.Add(SharedNumber(canvas, selected, category, "Side length",
            "The length of each side. The centre stays put.",
            entity => Shape(entity).SideLength,
            (copy, value) => value > 0 && ResizeTo(copy, polygon => polygon.RadiusForSide(value))));

        rows.Add(SharedNumber(canvas, selected, category, "Radius to corners",
            "Centre to each corner -- AutoCAD's inscribed size, the circle the polygon fits inside.",
            entity => Shape(entity).OuterRadius,
            (copy, value) => value > 0 && ResizeTo(copy, _ => value)));

        rows.Add(SharedNumber(canvas, selected, category, "Radius to sides",
            "Centre to the middle of each side -- AutoCAD's circumscribed size, the circle that fits inside it.",
            entity => Shape(entity).InnerRadius,
            (copy, value) => value > 0 && ResizeTo(copy, polygon => polygon.RadiusForInner(value))));
    }

    /// <summary>
    /// Width and height for a polyline that is a rectangle -- which is what
    /// the rectangle tool makes, there being no rectangle in DWG. The first
    /// corner stays put and the sides opposite it move.
    /// </summary>
    private static void AddRectangleSize(CadCanvas canvas, IReadOnlyList<SceneEntity> selected, List<PropertyRow> rows)
    {
        const string category = "Rectangle";

        static RectangleShape Shape(SceneEntity entity)
        {
            RectangleShape.TryRead((SPolyline)entity, out var shape);
            return shape;
        }

        rows.Add(SharedNumber(canvas, selected, category, "Width",
            "The side that runs more nearly horizontal. The first corner stays where it is.",
            entity => Shape(entity).Width,
            (copy, value) => RectangleShape.Resize((SPolyline)copy, value, null)));

        rows.Add(SharedNumber(canvas, selected, category, "Height",
            "The other side. The first corner stays where it is.",
            entity => Shape(entity).Height,
            (copy, value) => RectangleShape.Resize((SPolyline)copy, null, value)));
    }

    /// <summary>
    /// The sizes of the selected dimensions, one or many: set here, they
    /// change those dimensions only. The dimension style button sets what
    /// new ones are drawn with.
    /// </summary>
    private static void AddDimensionSizes(CadCanvas canvas, IReadOnlyList<SceneEntity> selected, List<PropertyRow> rows)
    {
        const string category = "Dimension";

        static DimensionStyle Of(SceneEntity entity) => ((SDimension)entity).DimensionStyle;

        static bool Restyle(SceneEntity copy, Func<DimensionStyle, DimensionStyle> change)
        {
            var dimension = (SDimension)copy;
            dimension.DimensionStyle = change(dimension.DimensionStyle);
            return true;
        }

        rows.Add(SharedNumber(canvas, selected, category, "Text height",
            "Height of the number, in drawing units.",
            entity => Of(entity).TextHeight,
            (copy, v) => v > 0 && Restyle(copy, style => style with { TextHeight = v })));

        rows.Add(SharedNumber(canvas, selected, category, "Arrow size",
            "Length of each arrowhead, in drawing units. Arrows that no longer fit between the "
            + "extension lines turn round and point in from outside.",
            entity => Of(entity).ArrowSize,
            (copy, v) => v > 0 && Restyle(copy, style => style with { ArrowSize = v })));

        rows.Add(SharedNumber(canvas, selected, category, "Extension line gap",
            "How far short of the measured point each extension line stops, so it does not touch "
            + "the geometry it is measuring (DIMEXO).",
            entity => Of(entity).ExtensionOffset,
            (copy, v) => v >= 0 && Restyle(copy, style => style with { ExtensionOffset = v })));

        rows.Add(SharedNumber(canvas, selected, category, "Extension line overrun",
            "How far each extension line carries on past the dimension line (DIMEXE).",
            entity => Of(entity).ExtensionBeyond,
            (copy, v) => v >= 0 && Restyle(copy, style => style with { ExtensionBeyond = v })));

        rows.Add(SharedNumber(canvas, selected, category, "Text gap",
            "Clearance between the number and the dimension line (DIMGAP).",
            entity => Of(entity).TextGap,
            (copy, v) => v >= 0 && Restyle(copy, style => style with { TextGap = v })));

        rows.Add(SharedNumber(canvas, selected, category, "Decimal places",
            "How many decimals the measurement is written to.",
            entity => Of(entity).Decimals,
            (copy, v) => v is >= 0 and <= 8 && v == Math.Floor(v)
                      && Restyle(copy, style => style with { Decimals = (int)v })));
    }

    private static PropertyRow SharedNumber(CadCanvas canvas, IReadOnlyList<SceneEntity> selected,
        string category, string name, string description,
        Func<SceneEntity, double> read, Func<SceneEntity, double, bool> write) =>
        new(category, name, description,
            Shared(selected, entity => PropertyRow.Number(read(entity)), out _),
            text => PropertyRow.TryNumber(text, out double value)
                 && EditAll(canvas, selected, copy => write(copy, value), $"Set {name.ToLowerInvariant()}"));

    /// <summary>
    /// Hands the choice to the shell and reports success immediately. The
    /// colour has not been applied yet and may never be, if the dialog is
    /// cancelled -- but the row is showing what is really set either way,
    /// since nothing has changed at the moment this returns.
    /// </summary>
    private static bool Choose(ColourChooser? chooser, Rgb current, Action<Rgb> apply)
    {
        if (chooser is null) return false;

        chooser(current, apply);
        return false;
    }

    private static IReadOnlyList<string> WithPicker(IReadOnlyList<string> choices, ColourChooser? chooser)
    {
        if (chooser is null) return choices;

        var withPicker = new List<string>(choices) { MoreColours };
        return withPicker;
    }

    /// <summary>The value they all share, or <see cref="Varies"/>.</summary>
    private static string Shared(IReadOnlyList<SceneEntity> selected,
        Func<SceneEntity, string> read, out bool agreed)
    {
        string first = read(selected[0]);
        agreed = selected.All(entity => read(entity) == first);

        return agreed ? first : Varies;
    }

    private static IReadOnlyList<string> WithVaries(IReadOnlyList<string> choices, bool agreed)
    {
        if (agreed) return choices;

        // Shown in the list so the box has something to display while the
        // selection disagrees, rather than looking empty and broken.
        var withMarker = new List<string> { Varies };
        withMarker.AddRange(choices);
        return withMarker;
    }

    /// <summary>
    /// Applies a change to every selected object, as one step on the undo
    /// stack. A change that any one of them refuses is applied to none.
    /// </summary>
    private static bool EditAll(CadCanvas canvas, IReadOnlyList<SceneEntity> selected,
        Func<SceneEntity, bool> change, string name)
    {
        var pairs = new List<(SceneEntity, SceneEntity)>(selected.Count);

        foreach (var entity in selected)
        {
            var copy = entity.Clone();
            if (!change(copy)) return false;

            copy.InvalidateBounds();
            pairs.Add((entity, copy));
        }

        return canvas.ApplyEdits(pairs, selected.Count == 1 ? name : $"{name} on {selected.Count} objects");
    }

    private static bool MoveCopyToLayer(Drawing drawing, SceneEntity copy, string name)
    {
        int index = drawing.Layers.FindIndex(layer =>
            string.Equals(layer.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

        if (index < 0) return false;

        copy.LayerIndex = index;
        copy.Style = drawing.Layers[index].Style;
        return true;
    }

    private static bool SetCopyColour(Drawing drawing, SceneEntity copy, string text)
    {
        if (string.Equals(text.Trim(), StyleChoices.ByLayer, StringComparison.OrdinalIgnoreCase))
        {
            if ((uint)copy.LayerIndex >= (uint)drawing.Layers.Count) return false;

            copy.Style = copy.Style with { Color = drawing.Layers[copy.LayerIndex].Color };
            return true;
        }

        if (!StyleChoices.TryColour(text, out var colour)) return false;

        copy.Style = copy.Style with { Color = colour };
        return true;
    }

    private static void AddGeometry(CadCanvas canvas, SceneEntity entity, List<PropertyRow> rows)
    {
        switch (entity)
        {
            case SLine line:
                Point(canvas, rows, "Line", "Start", line.Start,
                    (copy, p) => ((SLine)copy).Start = p);
                Point(canvas, rows, "Line", "End", line.End,
                    (copy, p) => ((SLine)copy).End = p);

                rows.Add(Derived("Line", "Length", "Distance between the two ends.",
                    Vec2.Distance(line.Start, line.End)));
                rows.Add(Derived("Line", "Angle", "Direction from start to end, in degrees.",
                    Degrees((line.End - line.Start).Angle())));
                return;

            case SCircle circle:
                Point(canvas, rows, "Circle", "Centre", circle.Center,
                    (copy, p) => ((SCircle)copy).Center = p);

                rows.Add(Number(canvas, "Circle", "Radius", "Radius in drawing units.", circle.Radius,
                    (copy, v) => { if (v <= 0) return false; ((SCircle)copy).Radius = v; return true; }));

                rows.Add(Number(canvas, "Circle", "Diameter", "Twice the radius. Setting it keeps the centre.",
                    circle.Radius * 2,
                    (copy, v) => { if (v <= 0) return false; ((SCircle)copy).Radius = v / 2; return true; }));
                return;

            case SArc arc:
                Point(canvas, rows, "Arc", "Centre", arc.Center,
                    (copy, p) => ((SArc)copy).Center = p);

                rows.Add(Number(canvas, "Arc", "Radius", "Radius in drawing units.", arc.Radius,
                    (copy, v) => { if (v <= 0) return false; ((SArc)copy).Radius = v; return true; }));

                rows.Add(Number(canvas, "Arc", "Start angle",
                    "Where the arc begins, in degrees counter-clockwise from east.",
                    Degrees(arc.StartAngle),
                    (copy, v) => { ((SArc)copy).StartAngle = Radians(v); return true; }));

                rows.Add(Number(canvas, "Arc", "Sweep",
                    "How far the arc turns, in degrees. Negative sweeps clockwise.",
                    Degrees(arc.Sweep),
                    (copy, v) => { ((SArc)copy).Sweep = Radians(v); return true; }));
                return;

            case SEllipse ellipse:
                Point(canvas, rows, "Ellipse", "Centre", ellipse.Center,
                    (copy, p) => ((SEllipse)copy).Center = p);

                AddEllipseSize(canvas, [ellipse], rows);
                rows.Add(Number(canvas, "Ellipse", "Ratio",
                    "Minor axis as a fraction of the major one.", ellipse.Ratio,
                    (copy, v) => { if (v is <= 0 or > 1) return false; ((SEllipse)copy).Ratio = v; return true; }));
                return;

            case SPolyline polyline:
                rows.Add(Derived("Polyline", "Vertices", "How many points the run is made of.",
                    polyline.Vertices.Length));

                rows.Add(new PropertyRow("Polyline", "Closed",
                    "Whether the last vertex joins back to the first.",
                    PropertyRow.Flag(polyline.Closed),
                    text =>
                    {
                        if (!PropertyRow.TryFlag(text, out bool value)) return false;

                        return Edit(canvas, polyline, copy => { ((SPolyline)copy).Closed = value; return true; });
                    }));

                if (RectangleShape.TryRead(polyline, out _)) AddRectangleSize(canvas, [polyline], rows);
                else if (RegularPolygon.TryRead(polyline, out _)) AddPolygonSize(canvas, [polyline], rows);
                return;

            case SText text when text.Lines.Count > 1:
                // One row cannot hold several lines, and writing one back
                // would flatten the rest away; the canvas editor can.
                rows.Add(Derived("Text", "Text",
                    "Several lines. Double-click the text on the drawing to edit it.",
                    string.Join(" / ", text.Lines)));
                AddTextSize(canvas, text, rows);
                return;

            case SText text:
                rows.Add(new PropertyRow("Text", "Text",
                    "The words. Double-click the text on the drawing to edit it there, with more than one line.",
                    text.Lines.Count > 0 ? text.Lines[0] : "",
                    value => Edit(canvas, text, copy =>
                    {
                        ((SText)copy).Lines = [value];
                        return true;
                    })));
                AddTextSize(canvas, text, rows);
                return;

            case SInsert insert:
                rows.Add(Derived("Block", "Name", "The block definition this places.", insert.Block.Name));
                return;

            case SDimension dimension:
                rows.Add(Derived("Dimension", "Measurement", "What the dimension measures, as written on it.",
                    dimension.MeasurementText));
                AddDimensionSizes(canvas, [dimension], rows);
                return;
        }
    }

    // ---- row helpers -------------------------------------------------------

    private static void Point(CadCanvas canvas, List<PropertyRow> rows, string category, string name,
        Vec2 value, Action<SceneEntity, Vec2> set)
    {
        var entity = canvas.Selection.Ordered[0];

        rows.Add(new PropertyRow(category, $"{name} X", $"{name} point, X coordinate.",
            PropertyRow.Number(value.X),
            text => PropertyRow.TryNumber(text, out double x)
                 && Edit(canvas, entity, copy => { set(copy, new Vec2(x, value.Y)); return true; })));

        rows.Add(new PropertyRow(category, $"{name} Y", $"{name} point, Y coordinate.",
            PropertyRow.Number(value.Y),
            text => PropertyRow.TryNumber(text, out double y)
                 && Edit(canvas, entity, copy => { set(copy, new Vec2(value.X, y)); return true; })));
    }

    private static PropertyRow Number(CadCanvas canvas, string category, string name, string description,
        double value, Func<SceneEntity, double, bool> set)
    {
        var entity = canvas.Selection.Ordered[0];

        return new PropertyRow(category, name, description, PropertyRow.Number(value),
            text => PropertyRow.TryNumber(text, out double parsed)
                 && Edit(canvas, entity, copy => set(copy, parsed)));
    }

    private static PropertyRow Derived(string category, string name, string description, double value) =>
        new(category, name, description, PropertyRow.Number(value));

    private static PropertyRow Derived(string category, string name, string description, string value) =>
        new(category, name, description, value);

    /// <summary>
    /// Applies a change by editing a copy and swapping it in through the
    /// command stack, which is the only way anything reaches the drawing.
    /// </summary>
    private static bool Edit(CadCanvas canvas, SceneEntity original, Func<SceneEntity, bool> change)
    {
        var copy = original.Clone();
        if (!change(copy)) return false;

        copy.InvalidateBounds();
        return canvas.ApplyEdit(original, copy, $"Edit {Describe(original).ToLowerInvariant()}");
    }

    /// <summary>
    /// Sets an explicit colour, or copies the layer's. The copy is what
    /// "by layer" can mean here: styles are resolved at import, so there is
    /// nowhere to record that the colour should keep following the layer.
    /// </summary>
    private static bool SetColour(CadCanvas canvas, Drawing drawing, SceneEntity entity, string text)
    {
        Rgb colour;

        if (string.Equals(text.Trim(), StyleChoices.ByLayer, StringComparison.OrdinalIgnoreCase))
        {
            if ((uint)entity.LayerIndex >= (uint)drawing.Layers.Count) return false;
            colour = drawing.Layers[entity.LayerIndex].Color;
        }
        else if (!StyleChoices.TryColour(text, out colour))
        {
            return false;
        }

        return Edit(canvas, entity, copy =>
        {
            copy.Style = copy.Style with { Color = colour };
            return true;
        });
    }

    private static bool MoveToLayer(CadCanvas canvas, Drawing drawing, SceneEntity entity, string name)
    {
        int index = drawing.Layers.FindIndex(layer =>
            string.Equals(layer.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

        if (index < 0) return false;

        return Edit(canvas, entity, copy =>
        {
            copy.LayerIndex = index;
            copy.Style = drawing.Layers[index].Style;
            return true;
        });
    }

    private static string LayerName(Drawing drawing, int index) =>
        (uint)index < (uint)drawing.Layers.Count ? drawing.Layers[index].Name : "0";

    private static string Describe(SceneEntity entity) =>
        entity.GetType().Name.TrimStart('S');

    private static double Degrees(double radians) => radians * 180.0 / Math.PI;

    private static double Radians(double degrees) => degrees * Math.PI / 180.0;
}
