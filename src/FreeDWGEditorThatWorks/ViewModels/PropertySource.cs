using System.Collections.Generic;
using System.Linq;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Snapping;
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

    public static List<PropertyRow> Build(CadCanvas canvas)
    {
        var rows = new List<PropertyRow>();

        AddSettings(canvas, rows);
        AddSelection(canvas, rows);

        return rows;
    }

    private static void AddSettings(CadCanvas canvas, List<PropertyRow> rows)
    {
        rows.Add(new PropertyRow(Settings, "Fillet radius",
            "Radius of the arc a fillet inserts, and the distance a chamfer cuts back. "
            + "Zero brings the two lines to a sharp corner.",
            PropertyRow.Number(canvas.CornerRadius),
            text =>
            {
                if (!PropertyRow.TryNumber(text, out double value) || value < 0) return false;

                canvas.CornerRadius = value;
                return true;
            }));

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

    private static void AddSelection(CadCanvas canvas, List<PropertyRow> rows)
    {
        var selected = canvas.Selection.Ordered;
        if (selected.Count == 0 || canvas.Drawing is not { } drawing) return;

        if (selected.Count > 1)
        {
            rows.Add(new PropertyRow(General, "Selection",
                "Several objects are selected. Pick one on its own to see and edit its geometry.",
                $"{selected.Count} objects"));
            return;
        }

        var entity = selected[0];

        rows.Add(new PropertyRow(General, "Type", "What kind of object this is.", Describe(entity)));

        rows.Add(new PropertyRow(General, "Layer",
            "The layer this object is on. Type the name of another layer to move it.",
            LayerName(drawing, entity.LayerIndex),
            text => MoveToLayer(canvas, drawing, entity, text)));

        AddGeometry(canvas, entity, rows);
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

                rows.Add(Derived("Circle", "Diameter", "Twice the radius.", circle.Radius * 2));
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

                rows.Add(Derived("Ellipse", "Major axis", "Half the long diameter.",
                    ellipse.MajorAxis.Length));
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
                return;

            case SText text:
                rows.Add(new PropertyRow("Text", "Text", "The first line of the text.",
                    text.Lines.Count > 0 ? text.Lines[0] : "",
                    value => Edit(canvas, text, copy =>
                    {
                        ((SText)copy).Lines = [value];
                        return true;
                    })));

                rows.Add(Number(canvas, "Text", "Height", "Capital-letter height in drawing units.",
                    text.Height,
                    (copy, v) => { if (v <= 0) return false; ((SText)copy).Height = v; return true; }));

                rows.Add(Number(canvas, "Text", "Rotation", "Rotation in degrees.",
                    Degrees(text.Rotation),
                    (copy, v) => { ((SText)copy).Rotation = Radians(v); return true; }));
                return;

            case SInsert insert:
                rows.Add(Derived("Block", "Name", "The block definition this places.", insert.Block.Name));
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
