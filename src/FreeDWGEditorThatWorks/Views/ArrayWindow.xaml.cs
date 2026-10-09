using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeDwg.Core.Editing;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene;

namespace FreeDWGEditorThatWorks.Views;

/// <summary>
/// The numbers an array is made from. Kept outside the dialog so they
/// survive it stepping aside for a pick, and the next array starts from the
/// last one's counts.
/// </summary>
public sealed class ArrayChoices
{
    public bool Polar { get; set; }

    public int Columns { get; set; } = 3;
    public int Rows { get; set; } = 2;
    public double ColumnSpacing { get; set; }
    public double RowSpacing { get; set; }
    public double AngleDegrees { get; set; }

    public int Count { get; set; } = 6;
    public double FillDegrees { get; set; } = 360;
    public Vec2 Center { get; set; }
    public bool RotateItems { get; set; } = true;

    /// <summary>
    /// Spacing and centre worked out from what is selected: a step of one
    /// and a half times its size, which is AutoCAD's default and leaves a
    /// visible gap; and a centre below it, so a polar array shows as a ring
    /// before the real centre is picked.
    /// </summary>
    public void FitTo(Bounds2 selection)
    {
        double size = Math.Max(selection.Width, selection.Height);
        if (size <= 0) size = 10;

        ColumnSpacing = (selection.Width > 0 ? selection.Width : size) * 1.5;
        RowSpacing = (selection.Height > 0 ? selection.Height : size) * 1.5;
        Center = new Vec2(selection.Center.X, selection.MinY - size);
    }

    public ArrayPattern Pattern => Polar
        ? new PolarArray(Center, Count, FillDegrees * Math.PI / 180, RotateItems)
        : new RectangularArray(Columns, Rows, ColumnSpacing, RowSpacing, AngleDegrees * Math.PI / 180);
}

/// <summary>
/// Rectangular or polar, with the copies previewed on the drawing behind it.
/// </summary>
/// <remarks>
/// Modal, so nothing else can change the selection while it is open. Picking
/// the centre therefore closes it with <see cref="PickCentreRequested"/>
/// set; the shell takes the click and opens it again on the same choices.
/// </remarks>
public partial class ArrayWindow : Window
{
    private readonly ArrayChoices _choices;
    private readonly DrawingUnits _units;
    private readonly int _selected;
    private readonly Action<ArrayPattern?> _preview;
    private bool _ready;

    public ArrayWindow(ArrayChoices choices, DrawingUnits units, int selected, Action<ArrayPattern?> preview)
    {
        InitializeComponent();

        _choices = choices;
        _units = units;
        _selected = selected;
        _preview = preview;

        string suffix = FreeDwg.Core.Scene.Units.Suffix(units);
        ColumnSpacingUnit.Text = suffix;
        RowSpacingUnit.Text = suffix;

        ColumnsBox.Text = Whole(choices.Columns);
        RowsBox.Text = Whole(choices.Rows);
        ColumnSpacingBox.Text = Number(choices.ColumnSpacing);
        RowSpacingBox.Text = Number(choices.RowSpacing);
        AngleBox.Text = Number(choices.AngleDegrees);

        CountBox.Text = Whole(choices.Count);
        FillBox.Text = Number(choices.FillDegrees);
        CenterXBox.Text = Number(choices.Center.X);
        CenterYBox.Text = Number(choices.Center.Y);
        RotateBox.IsChecked = choices.RotateItems;

        Kind.SelectedIndex = choices.Polar ? 1 : 0;

        _ready = true;
        Update();
    }

    /// <summary>The array to make, once the Array button has been pressed.</summary>
    public ArrayPattern? Chosen { get; private set; }

    /// <summary>Closed so the centre can be clicked on the drawing.</summary>
    public bool PickCentreRequested { get; private set; }

    private static string Whole(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selection changes bubble up from the text boxes inside the tabs.
        if (!ReferenceEquals(e.OriginalSource, Kind)) return;
        if (_ready) Update();
    }

    private void OnFieldChanged(object sender, RoutedEventArgs e)
    {
        if (_ready) Update();
    }

    /// <summary>
    /// Reads the boxes on the tab in front into the choices, or says which
    /// one is wrong. The preview follows on every keystroke that makes sense.
    /// </summary>
    private string? Read()
    {
        _choices.Polar = Kind.SelectedIndex == 1;

        static bool WholeNumber(TextBox box, int min, out int value) =>
            int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min;

        static bool Degrees(TextBox box, out double value) =>
            double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

        bool Length(TextBox box, out double value) =>
            FreeDwg.Core.Scene.Units.TryParseLength(box.Text, _units, out value);

        if (!_choices.Polar)
        {
            if (!WholeNumber(ColumnsBox, 1, out int columns)) return "Columns has to be a whole number, 1 or more.";
            if (!WholeNumber(RowsBox, 1, out int rows)) return "Rows has to be a whole number, 1 or more.";
            if (!Length(ColumnSpacingBox, out double columnSpacing)) return "Column spacing has to be a length.";
            if (!Length(RowSpacingBox, out double rowSpacing)) return "Row spacing has to be a length.";
            if (!Degrees(AngleBox, out double angle)) return "Angle has to be a number of degrees.";

            _choices.Columns = columns;
            _choices.Rows = rows;
            _choices.ColumnSpacing = columnSpacing;
            _choices.RowSpacing = rowSpacing;
            _choices.AngleDegrees = angle;
            return null;
        }

        if (!WholeNumber(CountBox, 2, out int count)) return "Items has to be a whole number, 2 or more.";
        if (!Degrees(FillBox, out double fill) || fill == 0 || Math.Abs(fill) > 360)
            return "Fill angle has to be between -360 and 360, and not 0.";
        if (!Length(CenterXBox, out double x) || !Length(CenterYBox, out double y))
            return "The centre has to be two coordinates.";

        _choices.Count = count;
        _choices.FillDegrees = fill;
        _choices.Center = new Vec2(x, y);
        _choices.RotateItems = RotateBox.IsChecked == true;
        return null;
    }

    private void Update()
    {
        string? problem = Read();
        var pattern = problem is null ? _choices.Pattern : null;

        if (pattern is { IsValid: false })
        {
            problem = pattern.Items > ArrayPattern.MaxItems
                ? $"That is {pattern.Items:N0} items; the most an array makes is {ArrayPattern.MaxItems:N0}."
                : pattern.Items <= 1
                    ? "That is one item: the original, with nothing to copy."
                    : "A spacing of zero would put every copy on top of the last.";
            pattern = null;
        }

        OkButton.IsEnabled = pattern is not null;
        _preview(pattern);

        if (pattern is null)
        {
            Summary.Text = problem;
            Summary.Foreground = System.Windows.Media.Brushes.Firebrick;
            return;
        }

        Summary.ClearValue(TextBlock.ForegroundProperty);
        string what = _selected == 1 ? "the selected object" : $"the {_selected} selected objects";
        Summary.Text = $"{pattern.Items - 1} cop{(pattern.Items == 2 ? "y" : "ies")} of {what}, "
            + $"{(pattern.Items - 1) * _selected:N0} new objects in all.";
    }

    private void OnPickCentre(object sender, RoutedEventArgs e)
    {
        Read();
        PickCentreRequested = true;
        DialogResult = false;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Read() is not null) return;

        var pattern = _choices.Pattern;
        if (!pattern.IsValid) return;

        Chosen = pattern;
        DialogResult = true;
    }
}
