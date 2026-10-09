using System.Collections.ObjectModel;
using System.Linq;
using System.ComponentModel;
using System.Windows.Data;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeDwg.Core.Commands;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Styling;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Snapping;
using FreeDwg.Core.Tools;
using FreeDwg.Core.Workspace;
using FreeDWGEditorThatWorks.Controls;
using FreeDwg.Interop.Acad;
using FreeDWGEditorThatWorks.ViewModels;
using FreeDWGEditorThatWorks.Views;
using Microsoft.Win32;
using SceneDrawing = FreeDwg.Core.Scene.Drawing;
using SceneLayout = FreeDwg.Core.Scene.Layout;

namespace FreeDWGEditorThatWorks;

public partial class MainWindow : Window
{
    private static readonly Rgb DarkBackground = new(33, 40, 48);
    private static readonly Rgb LightBackground = new(255, 255, 255);

    private readonly ObservableCollection<LayerItem> _layers = new();
    private ICollectionView? _layerView;

    /// <summary>
    /// How the user has foldered the layers. Not part of the drawing -- DWG
    /// has no such thing -- so it lives in a file beside it rather than on
    /// the command stack. See <see cref="LayerGroups.SidecarPath"/>.
    /// </summary>
    private LayerGroups _layerGroups = new();

    /// <summary>
    /// Why the groups file beside this drawing could not be read, if it
    /// could not. While this is set nothing is written back: overwriting a
    /// file we failed to understand would lose whatever was in it.
    /// </summary>
    private string? _layerGroupsProblem;

    /// <summary>What was last read or written, so an edit that changes nothing in it writes nothing.</summary>
    private string? _layerGroupsSaved;
    private readonly ObservableCollection<PropertyRow> _properties = new();

    /// <summary>
    /// The tool buttons, so exactly one can be lit. The canvas owns which
    /// mode is in force; this list is only how that is shown.
    /// </summary>
    private readonly RadioButton[] _toolButtons;

    /// <summary>
    /// False until the constructor has finished.
    /// </summary>
    /// <remarks>
    /// Setting a property in XAML can raise its change event while the parser
    /// is still working, before the elements further down the file exist. A
    /// handler that runs then and reaches for one of them gets a null, and
    /// the app dies before it draws anything.
    /// </remarks>
    private bool _ready;


    private ImportDiagnostics? _diagnostics;
    private string _documentName = "Untitled";

    /// <summary>
    /// The open drawing together with the document it was read from, which
    /// is what a save writes onto. The canvas holds the same drawing.
    /// </summary>
    private DwgSession? _session;

    public MainWindow()
    {
        InitializeComponent();

        LayerList.ItemsSource = _layers;

        // The filter runs over the view rather than over the collection, so
        // the rows that are hidden are still there to be counted and still
        // carry the layer index they always did.
        _layerView = CollectionViewSource.GetDefaultView(_layers);
        _layerView.Filter = row => ((LayerItem)row).Matches(LayerFilterBox.Text.Trim());

        // Grouped in the view rather than the collection, so the rows stay a
        // flat list that is simple to rebuild.
        var grouped = new CollectionViewSource { Source = _properties };
        grouped.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PropertyRow.Category)));
        PropertyGrid.ItemsSource = grouped.View;

        _toolButtons =
        [
            SelectButton, LineButton, PolylineButton, RectangleButton,
            CircleButton, ArcButton, EllipseButton, TextButton,
            DimensionButton, AlignedDimensionButton,
            RadiusDimensionButton, DiameterDimensionButton,
            MoveButton, CopyButton, RotateButton, ScaleButton, MirrorButton,
            TrimButton, ExtendButton, FilletButton, ChamferButton, TangentButton,
        ];

        Canvas.CursorMoved += (_, world) =>
        {
            var drawing = Canvas.Drawing;
            var units = drawing?.Units ?? DrawingUnits.Millimetres;
            int decimals = drawing?.LinearPrecision ?? 3;

            CoordinateText.Text =
                $"X {Units.Format(world.X, units, decimals),10}   "
                + $"Y {Units.Format(world.Y, units, decimals),10}   {Units.Suffix(units)}";

            // A corner size is not attached to a point being placed, so
            // nothing else would move its box along with the cursor.
            UpdateLengthOverlay();
        };

        Canvas.SelectionChanged += (_, _) =>
        {
            UpdateStatus();
            RebuildProperties();
        };

        // The canvas owns what a left click does; every button that claims to
        // set a mode is only a view of that. Syncing them all from one event
        // is what stops two of them being lit at once.
        Canvas.ModeChanged += (_, _) =>
        {
            // A point was placed or the tool changed: whatever was typed was
            // for the point that has gone.
            _typedFirst = _typedSecond = false;
            SyncModeButtons();
            UpdateTextOverlay();
            UpdateStatus();
            RebuildProperties();
            UpdateLengthOverlay();
        };

        Canvas.PendingPointChanged += (_, _) => UpdateLengthOverlay();

        // The text box sits on the text, so it moves when the view does.
        Canvas.ViewChanged += (_, _) => UpdateTextOverlay();
        Canvas.LengthTypingStarted += (_, typed) => StartTypingLength(typed);
        Canvas.TextTypingStarted += (_, typed) =>
        {
            UpdateTextOverlay();
            TextEntryBox.Focus();
            TextEntryBox.SelectedText = typed;
            TextEntryBox.CaretIndex = TextEntryBox.Text.Length;
        };
        Canvas.TextEditRequested += (_, text) => StartEditingText(text);
        Canvas.EntryFieldRequested += (_, _) =>
        {
            UpdateLengthOverlay();
            FocusEntryBox(LengthBox);
        };
        Canvas.DrawingEdited += (_, _) =>
        {
            if (Canvas.Drawing is { } drawing) PopulateLayers(drawing);

            // A renamed layer is saved under its new name, or it would fall
            // out of its group the next time the file is opened.
            SaveLayerGroups();
            UpdateHistoryButtons();
            UpdateTitle();
            UpdateStatus();
            UpdateEmptyHint();
            RebuildProperties();
        };

        Canvas.Snapping.Modes = SnapModes.Objects | SnapModes.Tracking;

        InputBindings.Add(new KeyBinding(new RelayCommand(OpenAsync), Key.O, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(NewDrawing), Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => Save(saveAs: false)), Key.S, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => Save(saveAs: true)), Key.S, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(new RelayCommand(Canvas.Undo), Key.Z, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(Canvas.Redo), Key.Y, ModifierKeys.Control));

        _ready = true;
        NewDrawing();
        RebuildProperties();
    }

    private void OnNewClick(object sender, RoutedEventArgs e) => NewDrawing();

    /// <summary>
    /// Starts an empty drawing. Also what the window opens on, so that the
    /// draw tools have somewhere to put things before anything is loaded.
    /// </summary>
    private void NewDrawing()
    {
        if (!ConfirmDiscard()) return;

        // Backed by a fresh document rather than a bare scene, so that the
        // first save has a file to write rather than having to invent one.
        _session = DwgSession.CreateNew();
        var drawing = _session.Drawing;

        _diagnostics = null;
        _documentName = "Untitled";
        _layerGroups = new LayerGroups();
        _layerGroupsProblem = null;
        _layerGroupsSaved = null;
        Canvas.Drawing = drawing;

        // A blank sheet has no extents to fit, so pick a working scale rather
        // than leaving the camera wherever the last drawing left it.
        Canvas.Camera.Center = Vec2.Zero;
        Canvas.Camera.Scale = 1.0;
        Canvas.Redraw();

        PopulateLayouts(drawing);
        PopulateLayers(drawing);
        Canvas.UseSelect();
        Canvas.UseSelect();

        UpdateHistoryButtons();
        UpdateTitle();
        UpdateStatus();
        UpdateEmptyHint();
    }

    /// <summary>
    /// The hint stands in for an empty sheet, so it comes and goes with the
    /// geometry rather than only at startup -- undoing back to nothing gets
    /// it back.
    /// </summary>
    private void UpdateEmptyHint() =>
        EmptyHint.Visibility = Canvas.Drawing is { } drawing && drawing.Entities.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void OnToolPicked(object sender, RoutedEventArgs e)
    {
        CanvasTool? tool = (sender as FrameworkElement)?.Name switch
        {
            nameof(LineButton) => new LineTool(),
            nameof(PolylineButton) => new PolylineTool(),
            nameof(RectangleButton) => new RectangleTool(),
            nameof(CircleButton) => new CircleTool(),
            nameof(ArcButton) => new ArcTool(),
            nameof(EllipseButton) => new EllipseTool(),
            nameof(TextButton) => new TextTool(),
            nameof(DimensionButton) => new LinearDimensionTool(),
            nameof(AlignedDimensionButton) => new AlignedDimensionTool(),
            nameof(RadiusDimensionButton) => new RadiusDimensionTool(),
            nameof(DiameterDimensionButton) => new DiameterDimensionTool(),
            nameof(MoveButton) => new MoveTool(),
            nameof(CopyButton) => new CopyTool(),
            nameof(RotateButton) => new RotateTool(),
            nameof(ScaleButton) => new ScaleTool(),
            nameof(MirrorButton) => new MirrorTool(),
            nameof(TrimButton) => new TrimTool(),
            nameof(ExtendButton) => new ExtendTool(),
            nameof(FilletButton) => new FilletTool(),
            nameof(ChamferButton) => new ChamferTool(),
            nameof(TangentButton) => new TangentMateTool(),
            _ => null,
        };

        // A modify tool with nothing selected has nothing to modify, and
        // silently doing nothing would read as the button being broken.
        if (tool is { NeedsSelection: true } && Canvas.Selection.IsEmpty)
        {
            StatusText.Text = $"Select something first, then pick {tool.Name}.";
            SyncModeButtons();
            Canvas.Focus();
            return;
        }

        if (tool is null) Canvas.UseSelect();
        else Canvas.UseTool(tool);

        Canvas.Focus();
    }

    /// <summary>
    /// Lights exactly the button that matches the canvas's mode, and unlights
    /// every other one.
    /// </summary>
    /// <remarks>
    /// The bug this replaces: a tool and the zoom window were separate flags,
    /// so both buttons could be lit while only one of them decided what a
    /// click did. Reading the state back out of the canvas rather than
    /// setting it in two places is what makes that impossible.
    /// </remarks>
    private void SyncModeButtons()
    {
        ZoomWindowToggle.IsChecked = Canvas.Mode == CanvasMode.ZoomWindow;


        RadioButton? active = Canvas.Mode switch
        {
            CanvasMode.Select => SelectButton,
            CanvasMode.Draw => ButtonFor(Canvas.Tool),
            _ => null,
        };

        foreach (var button in _toolButtons)
            button.IsChecked = ReferenceEquals(button, active);
    }

    private RadioButton? ButtonFor(CanvasTool? tool) => tool switch
    {
        LineTool => LineButton,
        PolylineTool => PolylineButton,
        RectangleTool => RectangleButton,
        CircleTool => CircleButton,
        ArcTool => ArcButton,
        EllipseTool => EllipseButton,
        TextTool => TextButton,
        LinearDimensionTool => DimensionButton,
        AlignedDimensionTool => AlignedDimensionButton,
        RadiusDimensionTool => RadiusDimensionButton,
        DiameterDimensionTool => DiameterDimensionButton,
        MoveTool => MoveButton,
        CopyTool => CopyButton,
        RotateTool => RotateButton,
        ScaleTool => ScaleButton,
        MirrorTool => MirrorButton,
        TrimTool => TrimButton,
        ExtendTool => ExtendButton,
        // Chamfer derives from fillet, so it has to be asked about first.
        ChamferTool => ChamferButton,
        FilletTool => FilletButton,
        TangentMateTool => TangentButton,
        _ => null,
    };

    /// <summary>
    /// Rebuilds the properties panel. Wholesale, because what belongs in it
    /// changes completely with the selection: a circle and a line share
    /// almost no fields.
    /// </summary>
    private void RebuildProperties()
    {
        if (!_ready) return;

        // Keep the cursor on the same field across a rebuild, so editing one
        // value does not throw the reader back to the top of the list.
        string? wasOn = (PropertyGrid.SelectedItem as PropertyRow)?.Name;

        _properties.Clear();
        foreach (var row in PropertySource.Build(Canvas, ChooseColour)) _properties.Add(row);

        PropertyScopeText.Text = Canvas.Selection.Count switch
        {
            0 => "nothing selected",
            1 => Canvas.Selection.Ordered[0].GetType().Name.TrimStart('S').ToLowerInvariant(),
            var many => $"{many} objects",
        };

        if (wasOn is not null)
            PropertyGrid.SelectedItem = _properties.FirstOrDefault(row => row.Name == wasOn);

        ShowPropertyHelp();
    }

    /// <summary>
    /// Opens the colour picker, once the grid has finished with the cell.
    /// </summary>
    /// <remarks>
    /// Deferred deliberately. The request arrives while the DataGrid is
    /// committing the cell the choice was made in, and opening a modal window
    /// inside that commit is a way to wedge WPF's input system. Letting the
    /// commit finish first costs a frame and nothing else.
    /// </remarks>
    private void ChooseColour(Rgb current, Action<Rgb> chosen) =>
        Dispatcher.BeginInvoke(() =>
        {
            var picker = new ColourPickerWindow(current) { Owner = this };

            if (picker.ShowDialog() == true) chosen(picker.Chosen);
            else RebuildProperties();
        });

    private void OnPropertyRowSelected(object sender, SelectionChangedEventArgs e) => ShowPropertyHelp();

    /// <summary>
    /// The help pane under the grid, as a property grid has. It is where a
    /// field gets to explain itself without a tooltip nobody hovers over.
    /// </summary>
    private void ShowPropertyHelp()
    {
        if (PropertyGrid.SelectedItem is not PropertyRow row)
        {
            PropertyHelpName.Text = "";
            PropertyHelpText.Text = "Select a row to see what it does.";
            return;
        }

        PropertyHelpName.Text = row.Name;
        PropertyHelpText.Text = row.Description;
    }

    /// <summary>
    /// The grid switch does both jobs: showing it and snapping to it. They
    /// are separate settings in AutoCAD, which is two things to explain and
    /// a standing source of "why is it not snapping to the grid I can see".
    /// </summary>
    private void OnGridToggled(object sender, RoutedEventArgs e)
    {
        Canvas.Snapping.Grid.IsVisible = GridToggle.IsChecked == true;
        OnSnapToggled(sender, e);
    }

    private void OnSnapToggled(object sender, RoutedEventArgs e)
    {
        // Ortho forces the direction and polar only attracts to it, so with
        // both on polar can never be the answer. Rather than leave a lit
        // button that does nothing, picking one drops the other.
        if (ReferenceEquals(sender, OrthoToggle) && OrthoToggle.IsChecked == true)
            PolarToggle.IsChecked = false;

        if (ReferenceEquals(sender, PolarToggle) && PolarToggle.IsChecked == true)
            OrthoToggle.IsChecked = false;

        var modes = SnapModes.None;

        if (EndpointSnapToggle.IsChecked == true) modes |= SnapModes.Endpoint;
        if (MidpointSnapToggle.IsChecked == true) modes |= SnapModes.Midpoint;
        if (CenterSnapToggle.IsChecked == true) modes |= SnapModes.Center;
        if (QuadrantSnapToggle.IsChecked == true) modes |= SnapModes.Quadrant;
        if (PerpendicularSnapToggle.IsChecked == true) modes |= SnapModes.Perpendicular;
        if (TangentSnapToggle.IsChecked == true) modes |= SnapModes.Tangent;
        if (IntersectionSnapToggle.IsChecked == true) modes |= SnapModes.Intersection;
        if (TrackingSnapToggle.IsChecked == true) modes |= SnapModes.Tracking;
        if (PolarToggle.IsChecked == true) modes |= SnapModes.Polar;
        if (GridToggle.IsChecked == true) modes |= SnapModes.Grid;

        Canvas.Snapping.Modes = modes;
        Canvas.Snapping.Ortho = OrthoToggle.IsChecked == true;

        Canvas.Redraw();
        Canvas.Focus();
    }

    private void OnEraseClick(object sender, RoutedEventArgs e)
    {
        Canvas.EraseSelection();
        Canvas.Focus();
    }

    private void OnUndoClick(object sender, RoutedEventArgs e)
    {
        Canvas.Undo();
        Canvas.Focus();
    }

    private void OnRedoClick(object sender, RoutedEventArgs e)
    {
        Canvas.Redo();
        Canvas.Focus();
    }

    /// <summary>
    /// Says what Undo and Redo would take back, and greys out what has
    /// nothing to act on, at the moment the menu is looked at.
    /// </summary>
    private void OnEditMenuOpened(object sender, RoutedEventArgs e)
    {
        UndoMenuItem.IsEnabled = Canvas.Commands?.CanUndo == true;
        RedoMenuItem.IsEnabled = Canvas.Commands?.CanRedo == true;
        UndoMenuItem.Header = Canvas.Commands?.UndoName is { } undo ? $"_Undo {undo}" : "_Undo";
        RedoMenuItem.Header = Canvas.Commands?.RedoName is { } redo ? $"_Redo {redo}" : "_Redo";
        EraseMenuItem.IsEnabled = !Canvas.Selection.IsEmpty;
    }

    private void OnZoomWindowMenu(object sender, RoutedEventArgs e)
    {
        // The toolbar toggle follows from ModeChanged, as it does for every mode.
        Canvas.UseZoomWindow();
        Canvas.Focus();
    }

    /// <summary>Closing goes through the window, so unsaved work is asked about.</summary>
    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private void UpdateHistoryButtons()
    {
        UndoButton.IsEnabled = Canvas.Commands?.CanUndo == true;
        RedoButton.IsEnabled = Canvas.Commands?.CanRedo == true;

        UndoButton.ToolTip = Canvas.Commands?.UndoName is { } undo ? $"Undo {undo}  (Ctrl+Z)" : "Undo  (Ctrl+Z)";
        RedoButton.ToolTip = Canvas.Commands?.RedoName is { } redo ? $"Redo {redo}  (Ctrl+Y)" : "Redo  (Ctrl+Y)";
    }

    private void UpdateTitle()
    {
        string dirty = Canvas.Commands?.IsModified == true ? "*" : "";
        Title = $"FreeDWG Editor - {_documentName}{dirty}";
    }

    private async void OnOpenClick(object sender, RoutedEventArgs e) => await OpenAsync();

    private async Task OpenAsync()
    {
        if (!ConfirmDiscard()) return;

        var dialog = new OpenFileDialog
        {
            Title = "Open drawing",
            Filter = "CAD drawings (*.dwg;*.dxf)|*.dwg;*.dxf|DWG (*.dwg)|*.dwg|DXF (*.dxf)|*.dxf|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true) return;

        string path = dialog.FileName;
        StatusText.Text = $"Loading {Path.GetFileName(path)}...";
        Cursor = Cursors.Wait;

        try
        {
            // Importing a large drawing takes seconds; keep the UI responsive.
            var diagnostics = new ImportDiagnostics();
            var session = await Task.Run(() => DwgSession.Open(path, diagnostics));
            SceneDrawing drawing = session.Drawing;

            _session = session;
            _diagnostics = diagnostics;
            _documentName = Path.GetFileName(path);

            // Before the canvas has it: a group saved hidden switches its
            // layers off, and the first frame should already show that.
            LoadLayerGroups(drawing);
            Canvas.Drawing = drawing;

            PopulateLayouts(drawing);
            PopulateLayers(drawing);
            Canvas.UseSelect();

            UpdateHistoryButtons();
            UpdateTitle();
            UpdateStatus();
            UpdateEmptyHint();

            if (_layerGroupsProblem is { } problem)
                StatusText.Text = problem;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to open {Path.GetFileName(path)}: {ex.Message}";
            MessageBox.Show(this, ex.ToString(), "Could not open drawing",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        Save(saveAs: false);
        Canvas.Focus();
    }

    private void OnSaveAsClick(object sender, RoutedEventArgs e)
    {
        Save(saveAs: true);
        Canvas.Focus();
    }

    /// <summary>
    /// Writes the drawing, asking where first if it has never been saved or
    /// if <paramref name="saveAs"/> says to. False if nothing was written.
    /// </summary>
    /// <remarks>
    /// Synchronous, on purpose. A save reads the command stack and marks it
    /// saved at the end; an edit made while the file was being written in the
    /// background would be marked saved without being in the file.
    /// </remarks>
    private bool Save(bool saveAs)
    {
        if (_session is null || Canvas.Commands is not { } commands) return false;

        string? path = _session.Path;
        bool newPath = saveAs || path is null;

        if (newPath)
        {
            var dialog = new SaveFileDialog
            {
                Title = saveAs ? "Save drawing as" : "Save drawing",
                Filter = $"DWG drawing, {_session.FormatName} (*.dwg)|*.dwg|DXF drawing (*.dxf)|*.dxf",
                FileName = path is null ? "Untitled.dwg" : Path.GetFileName(path),
                InitialDirectory = path is null ? "" : Path.GetDirectoryName(path),
                AddExtension = true,
                DefaultExt = ".dwg",
                OverwritePrompt = true,
            };

            if (path is not null && string.Equals(Path.GetExtension(path), ".dxf", StringComparison.OrdinalIgnoreCase))
                dialog.FilterIndex = 2;

            if (dialog.ShowDialog(this) != true) return false;
            path = dialog.FileName;
        }

        StatusText.Text = $"Saving {Path.GetFileName(path)}...";
        Cursor = Cursors.Wait;

        try
        {
            var report = _session.Save(commands, path);
            _documentName = Path.GetFileName(report.Path);

            // The groups belong beside whichever file the drawing now is.
            if (newPath)
            {
                _layerGroupsProblem = null;
                _layerGroupsSaved = null;
            }
            SaveLayerGroups();

            UpdateTitle();
            StatusText.Text = report.Summary();

            // Anything the writer could not express is said out loud: a file
            // that quietly comes back without an edit in it is the worst way
            // for a save to fail.
            if (report.Notes.Count > 0)
            {
                MessageBox.Show(this,
                    "The drawing was saved, but not everything in it was written as drawn:\n\n"
                    + string.Join("\n", report.Notes.Take(15).Select(note => "  - " + note))
                    + (report.Notes.Count > 15 ? $"\n  ... and {report.Notes.Count - 15} more" : ""),
                    "Saved with notes", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to save {Path.GetFileName(path)}: {ex.Message}";
            MessageBox.Show(this, ex.ToString(), "Could not save drawing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    /// <summary>
    /// Asks whether to keep unsaved work before it is thrown away. True if
    /// it is fine to carry on.
    /// </summary>
    private bool ConfirmDiscard()
    {
        if (Canvas.Commands?.IsModified != true) return true;

        var answer = MessageBox.Show(this, $"Save changes to {_documentName}?", "FreeDWG Editor",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        return answer switch
        {
            MessageBoxResult.Yes => Save(saveAs: false),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ConfirmDiscard()) e.Cancel = true;
        base.OnClosing(e);
    }

    private void PopulateLayouts(SceneDrawing drawing)
    {
        LayoutTabs.ItemsSource = drawing.Layouts;
        LayoutTabs.SelectedItem = drawing.ActiveLayout;

        // A drawing with nothing but model space has no tabs worth showing.
        LayoutTabs.Visibility = drawing.Layouts.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnLayoutSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Canvas.Drawing is not { } drawing) return;
        if (LayoutTabs.SelectedItem is not SceneLayout layout) return;
        if (ReferenceEquals(drawing.ActiveLayout, layout)) return;

        drawing.ActiveLayout = layout;

        // A selection belongs to the sheet it was made on.
        Canvas.Selection.Clear();

        // Model space and a sheet are in different units and nowhere near each
        // other, so the view has to be reframed rather than kept.
        Canvas.ZoomExtents();

        PopulateLayers(drawing);
        UpdateStatus();
        UpdateEmptyHint();
    }

    private void PopulateLayers(SceneDrawing drawing)
    {
        var counts = new int[drawing.Layers.Count];
        foreach (var entity in drawing.Entities)
            if ((uint)entity.LayerIndex < (uint)counts.Length)
                counts[entity.LayerIndex]++;

        // Each row under its group's header, groups in the user's order and
        // the ungrouped last. The view groups in order of first appearance,
        // so the rows go in already sorted that way.
        var headers = new Dictionary<LayerGroup, LayerGroupItem>();
        var ungrouped = new LayerGroupItem(null, int.MaxValue, OnLayerGroupChanged);
        var rows = new List<LayerItem>();

        for (int i = 0; i < drawing.Layers.Count; i++)
        {
            var row = new LayerItem(drawing.Layers[i], i, counts[i], OnLayerVisibilityChanged);

            if (_layerGroups.GroupOf(drawing.Layers[i]) is not { } group)
                row.Group = ungrouped;
            else if (!headers.TryGetValue(group, out var header))
                row.Group = headers[group] =
                    new LayerGroupItem(group, _layerGroups.Groups.IndexOf(group), OnLayerGroupChanged);
            else
                row.Group = header;

            row.Group.Members.Add(row);
            rows.Add(row);
        }

        _layers.Clear();
        foreach (var row in rows.OrderBy(row => row.Group!.Order).ThenBy(row => row.Index))
            _layers.Add(row);

        // With no groups there is nothing to fold, and the panel is the flat
        // list it was -- not one folder called "Other layers".
        if (_layerView is not null)
        {
            bool grouped = headers.Count > 0;
            if (grouped && _layerView.GroupDescriptions.Count == 0)
                _layerView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LayerItem.Group)));
            else if (!grouped && _layerView.GroupDescriptions.Count > 0)
                _layerView.GroupDescriptions.Clear();
        }

        // Selecting a row is how the current layer is chosen, so the list has
        // to start on whatever the drawing says that is -- by index, since a
        // filtered row's position is not its index.
        LayerList.SelectedItem = _layers.FirstOrDefault(row => row.Index == drawing.CurrentLayerIndex);

        UpdateLayerFilter();
    }

    /// <summary>
    /// Re-runs the filter and says how much of the list it is showing.
    /// </summary>
    /// <remarks>
    /// The count carries both numbers while anything is hidden. A panel that
    /// showed "3" with no hint that there were another forty-six would have
    /// the user hunting for a layer that is right there.
    /// </remarks>
    private void UpdateLayerFilter()
    {
        if (!_ready) return;

        string filter = LayerFilterBox.Text.Trim();
        _layerView?.Refresh();

        int shown = _layers.Count(row => row.Matches(filter));

        LayerCountText.Text = shown == _layers.Count
            ? _layers.Count.ToString()
            : $"{shown} / {_layers.Count}";

        LayerFilterPrompt.Visibility = LayerFilterBox.Text.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        LayerFilterClear.Visibility = LayerFilterBox.Text.Length == 0
            ? Visibility.Collapsed
            : Visibility.Visible;

        UpdateLayerButtons();
    }

    private void OnLayerFilterChanged(object sender, TextChangedEventArgs e) => UpdateLayerFilter();

    private void OnClearLayerFilter(object sender, RoutedEventArgs e) => ClearLayerFilter();

    /// <summary>Escape empties the box, as it does everywhere else here.</summary>
    private void OnLayerFilterKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        ClearLayerFilter();
        e.Handled = true;
    }

    private void ClearLayerFilter()
    {
        if (LayerFilterBox.Text.Length == 0) return;

        LayerFilterBox.Clear();

        // Back to the drawing: the box has done its job and the next
        // keystroke almost certainly belongs to a tool.
        Canvas.Focus();
    }

    /// <summary>
    /// Which layer the selected row is, or -1. The row's position is not it:
    /// the list is filtered.
    /// </summary>
    private int SelectedLayerIndex =>
        LayerList.SelectedItem is LayerItem row ? row.Index : -1;

    /// <summary>
    /// Greys the delete button when the selected layer is one that cannot
    /// go, rather than letting it be pressed and then explaining.
    /// </summary>
    private void UpdateLayerButtons()
    {
        bool hasDrawing = Canvas.Drawing is not null;
        int index = SelectedLayerIndex;

        LayerNewButton.IsEnabled = hasDrawing;
        LayerEditButton.IsEnabled = hasDrawing && index >= 0;
        LayerDeleteButton.IsEnabled = hasDrawing && index >= 0
                                   && Canvas.WhyLayerCannotBeDeleted(index) is null;
        LayerGroupButton.IsEnabled = hasDrawing && LayerList.SelectedItems.Count > 0;
    }

    private void OnNewLayer(object sender, RoutedEventArgs e)
    {
        if (Canvas.Drawing is not { } drawing) return;

        var suggested = new LayerProperties(
            LayerTable.UniqueName(drawing, "Layer"), Rgb.White, Lineweight.Default,
            null, IsOn: true, IsFrozen: false, IsLocked: false);

        var dialog = new LayerPropertiesWindow("New layer", suggested) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        if (Canvas.AddLayer(dialog.Result) is not { } layer) return;

        // A layer made on purpose is the one about to be drawn on -- and a
        // filter left over from finding something else would hide it.
        ClearLayerFilter();

        drawing.CurrentLayerIndex = drawing.Layers.IndexOf(layer);
        PopulateLayers(drawing);
    }

    private void OnEditLayer(object sender, RoutedEventArgs e) => EditSelectedLayer();

    /// <summary>Double-clicking a row is the other way to the same dialog.</summary>
    private void OnLayerDoubleClick(object sender, MouseButtonEventArgs e) => EditSelectedLayer();

    private void EditSelectedLayer()
    {
        if (Canvas.Drawing is not { } drawing) return;

        int index = SelectedLayerIndex;
        if ((uint)index >= (uint)drawing.Layers.Count) return;

        var properties = LayerProperties.Of(drawing.Layers[index]);
        var dialog = new LayerPropertiesWindow($"Layer {properties.Name}", properties) { Owner = this };

        if (dialog.ShowDialog() != true) return;

        Canvas.EditLayer(index, dialog.Result);
        PopulateLayers(drawing);
    }

    private void OnDeleteLayer(object sender, RoutedEventArgs e)
    {
        int index = SelectedLayerIndex;
        if (index < 0) return;

        // The button is greyed for these, so reaching here means the drawing
        // changed under it -- say why rather than doing nothing.
        if (Canvas.WhyLayerCannotBeDeleted(index) is { } why)
        {
            MessageBox.Show(this, why, "Delete layer", MessageBoxButton.OK, MessageBoxImage.Information);
            UpdateLayerButtons();
            return;
        }

        Canvas.DeleteLayer(index);
    }

    /// <summary>The selected row is the layer new geometry is drawn on.</summary>
    private void OnLayerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (Canvas.Drawing is not { } drawing) return;

        // Filtering a row out deselects it, and that is not the user saying
        // they no longer have a current layer.
        if (SelectedLayerIndex < 0) return;

        drawing.CurrentLayerIndex = SelectedLayerIndex;

        UpdateLayerButtons();
        UpdateStatus();
    }

    private void OnLayerVisibilityChanged()
    {
        Canvas.Redraw();
        UpdateStatus();

        // A layer switched on its own row can turn its group mixed, or all
        // one way, and that is what the groups file keeps.
        SaveLayerGroups();
    }

    // ---- layer groups -------------------------------------------------------

    /// <summary>Every row picked, in the order they were picked.</summary>
    private List<LayerItem> SelectedLayerRows => LayerList.SelectedItems.Cast<LayerItem>().ToList();

    /// <summary>A group header was switched, folded or unfolded.</summary>
    private void OnLayerGroupChanged()
    {
        Canvas.Redraw();
        UpdateStatus();
        SaveLayerGroups();
    }

    private void LoadLayerGroups(SceneDrawing drawing)
    {
        _layerGroupsProblem = null;

        try
        {
            _layerGroups = LayerGroups.Load(drawing);
            _layerGroupsSaved = _layerGroups.ToJson(drawing);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _layerGroups = new LayerGroups();
            _layerGroupsSaved = null;
            _layerGroupsProblem =
                $"Layer groups in {Path.GetFileName(LayerGroups.SidecarPath(drawing.SourcePath!))} could not be read "
                + $"and will not be overwritten: {ex.Message}";
        }
    }

    /// <summary>
    /// Writes the groups beside the drawing. A new drawing has nowhere to
    /// put them until it is saved, so they last as long as the window.
    /// </summary>
    private void SaveLayerGroups()
    {
        if (_layerGroupsProblem is not null) return;
        if (Canvas.Drawing is not { SourcePath: not null } drawing) return;

        string json = _layerGroups.ToJson(drawing);
        if (json == _layerGroupsSaved) return;

        try
        {
            if (_layerGroups.Save(drawing)) _layerGroupsSaved = json;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not save the layer groups: {ex.Message}";
        }
    }

    /// <summary>After the groups change shape: rebuild the panel and keep them.</summary>
    private void LayerGroupsReshaped()
    {
        if (Canvas.Drawing is { } drawing) PopulateLayers(drawing);
        SaveLayerGroups();
    }

    private void OnNewLayerGroup(object sender, RoutedEventArgs e) => GroupSelectedLayers();

    private void GroupSelectedLayers()
    {
        var rows = SelectedLayerRows;
        if (rows.Count == 0) return;

        string prompt = rows.Count == 1
            ? $"A new group for {rows[0].Name}:"
            : $"A new group for these {rows.Count} layers:";

        var dialog = new NameWindow("New layer group", prompt, _layerGroups.UniqueName("Group")) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        _layerGroups.Create(dialog.Result, rows.Select(row => row.Layer));
        LayerGroupsReshaped();
    }

    /// <summary>
    /// The row menu names the groups, so it is made as it opens rather than
    /// written out in the markup.
    /// </summary>
    private void OnLayerMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // A group header has a menu of its own; leave it be.
        if (!IsOnLayerRow(e.OriginalSource as DependencyObject)) return;

        var rows = SelectedLayerRows;
        var menu = LayerList.ContextMenu;
        menu.Items.Clear();

        if (rows.Count == 0 || Canvas.Drawing is null)
        {
            e.Handled = true;
            return;
        }

        var layers = rows.Select(row => row.Layer).ToList();
        string what = rows.Count == 1 ? rows[0].Name : $"{rows.Count} layers";

        menu.Items.Add(Item($"New group from {what}...", GroupSelectedLayers));

        var targets = _layerGroups.Groups
            .Where(group => !layers.All(group.Layers.Contains))
            .ToList();

        if (targets.Count > 0)
        {
            var move = new MenuItem { Header = "Move to group" };
            foreach (var target in targets)
            {
                move.Items.Add(Item(target.Name, () =>
                {
                    _layerGroups.Assign(layers, target);
                    LayerGroupsReshaped();
                }));
            }
            menu.Items.Add(move);
        }

        if (layers.Any(layer => _layerGroups.GroupOf(layer) is not null))
        {
            menu.Items.Add(Item("Remove from group", () =>
            {
                _layerGroups.Assign(layers, null);
                LayerGroupsReshaped();
            }));
        }

        static MenuItem Item(string header, Action click)
        {
            // A TextBlock, so an underscore in a layer name is not an access key.
            var item = new MenuItem { Header = new TextBlock { Text = header } };
            item.Click += (_, _) => click();
            return item;
        }
    }

    /// <summary>Whether a right-click landed on a layer row, as opposed to a group header.</summary>
    private bool IsOnLayerRow(DependencyObject? element)
    {
        while (element is not null && !ReferenceEquals(element, LayerList))
        {
            if (element is ListBoxItem) return true;
            if (element is GroupItem) return false;

            element = element is System.Windows.Media.Visual
                ? System.Windows.Media.VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return false;
    }

    /// <summary>The "Other layers" header is not a group, and has nothing to rename or ungroup.</summary>
    private void OnLayerGroupMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (HeaderOf(sender) is not { IsGroup: true }) e.Handled = true;
    }

    private void OnRenameLayerGroup(object sender, RoutedEventArgs e)
    {
        if (HeaderOf(sender)?.Group is not { } group) return;

        var dialog = new NameWindow("Rename layer group", "Name:", group.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        group.Name = dialog.Result;
        LayerGroupsReshaped();
    }

    /// <summary>The folder goes; its layers stay exactly as they were, ungrouped.</summary>
    private void OnUngroupLayers(object sender, RoutedEventArgs e)
    {
        if (HeaderOf(sender)?.Group is not { } group) return;

        _layerGroups.Ungroup(group);
        LayerGroupsReshaped();
    }

    /// <summary>The header a menu or its owner belongs to: the view's group, whose Name is it.</summary>
    private static LayerGroupItem? HeaderOf(object sender) =>
        (sender as FrameworkElement)?.DataContext is CollectionViewGroup { Name: LayerGroupItem header }
            ? header
            : null;

    private void OnZoomExtentsClick(object sender, RoutedEventArgs e) => Canvas.ZoomExtents();

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        if (Canvas.Drawing is not { } drawing) return;

        new DocumentSettingsWindow(drawing, () =>
        {
            // The units decide how big a new dimension's text is and how its
            // number reads, so a tool already in hand has to be told.
            Canvas.RefreshToolSettings();

            RebuildProperties();
            UpdateStatus();
            UpdateLengthOverlay();
        }, EditDimensionStyle)
        { Owner = this }.ShowDialog();
    }

    /// <summary>The last array's numbers, so the next one starts from its counts.</summary>
    private readonly ArrayChoices _arrayChoices = new();

    /// <summary>
    /// Arrays the selection: the dialog, with the copies previewed behind it,
    /// and -- for a polar array -- a click on the drawing for the centre.
    /// </summary>
    /// <remarks>
    /// The dialog is modal, so picking the centre closes it and this opens it
    /// again on the same choices once the point is in. Escape during the pick
    /// just brings the dialog back with the centre it had.
    /// </remarks>
    private async void OnArrayClick(object sender, RoutedEventArgs e)
    {
        if (Canvas.Drawing is not { } drawing) return;

        if (Canvas.Selection.IsEmpty)
        {
            StatusText.Text = "Select something first, then pick array.";
            Canvas.Focus();
            return;
        }

        // A modify tool half way through would be drawing its own preview
        // over the array's.
        if (Canvas.Mode != CanvasMode.Select) Canvas.UseSelect();

        var selected = Canvas.Selection.Ordered;
        var bounds = selected.Aggregate(Bounds2.Empty, (all, entity) => all.Union(entity.Bounds));
        _arrayChoices.FitTo(bounds);

        while (true)
        {
            var dialog = new ArrayWindow(_arrayChoices, drawing.Units, selected.Count,
                pattern => Canvas.ArrayPreview = pattern?.Copies(bounds))
            { Owner = this };

            bool accepted = dialog.ShowDialog() == true;

            if (dialog.PickCentreRequested)
            {
                // The ring stays on screen while the centre is chosen, so the
                // click can be aimed with the result in view.
                var point = await Canvas.PickPointAsync("Array: pick the centre to array round  (Escape to go back)");
                if (point is { } centre) _arrayChoices.Center = centre;
                continue;
            }

            Canvas.ArrayPreview = null;

            if (accepted && dialog.Chosen is { } chosen && Canvas.ArraySelection(chosen))
                StatusText.Text = $"{Canvas.Commands?.UndoName}: {chosen.Items - 1} copies. Ctrl+Z takes the whole array back.";

            break;
        }

        Canvas.Focus();
    }

    private void OnDimensionStyleClick(object sender, RoutedEventArgs e)
    {
        EditDimensionStyle(this);
        Canvas.Focus();
    }

    /// <summary>
    /// Opens the dimension style dialog over <paramref name="owner"/> and
    /// applies what comes back as one undoable step.
    /// </summary>
    private void EditDimensionStyle(Window owner)
    {
        if (Canvas.Drawing is not { } drawing) return;

        var dialog = new DimensionStyleWindow(DimensionSettings.For(drawing), drawing.Units,
            ChangeDimensionSettings.CountRestylable(drawing))
        { Owner = owner };

        if (dialog.ShowDialog() != true || dialog.Chosen is not { } chosen) return;
        if (chosen == drawing.Dimensions && !dialog.RestyleExisting) return;

        Canvas.SetDimensionSettings(chosen, dialog.RestyleExisting);

        var style = chosen.StyleIn(drawing.Units);
        StatusText.Text = $"Dimensions: {Units.Describe(style.TextHeight, drawing.Units, 4)} text, "
            + $"{Units.Describe(style.ArrowSize, drawing.Units, 4)} arrows"
            + (dialog.RestyleExisting ? ", applied to the dimensions already drawn." : ", for new dimensions.");
    }

    // ---- the overlay beside the cursor -------------------------------------

    /// <summary>
    /// Whether each box holds something typed rather than the live figure.
    /// A typed box is left alone as the mouse moves; an untyped one follows
    /// it, so Tab into the height shows the height as it stands.
    /// </summary>
    private bool _typedFirst, _typedSecond;

    /// <summary>Set while the shell writes a box, so that is not mistaken for typing.</summary>
    private bool _writingEntry;

    /// <summary>
    /// Shows the number the tool in hand is working to, beside the cursor.
    /// </summary>
    /// <remarks>
    /// Which number that is comes from the canvas rather than being worked
    /// out here, so there is one answer to it: a length while a point is
    /// being placed, a radius while a fillet is waiting for its edges, a
    /// distance for a chamfer, a width and a height for a rectangle.
    /// <para>
    /// Left alone once it has been typed into: overwriting what someone is
    /// halfway through entering, twenty times a second as the mouse moves,
    /// would make it impossible to use.
    /// </para>
    /// </remarks>
    private void UpdateLengthOverlay()
    {
        if (!_ready) return;

        if (Canvas.EntryValue is not { } value || Canvas.Drawing is not { } drawing)
        {
            LengthOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        bool size = Canvas.Entry == CursorEntry.Size;

        LengthOverlay.Visibility = Visibility.Visible;
        LengthCaption.Text = Canvas.Entry switch
        {
            CursorEntry.Radius => "Radius",
            CursorEntry.Distance => "Distance",
            CursorEntry.Size => "Width",
            _ => "Length",
        };

        SecondCaption.Visibility = SecondBox.Visibility = size ? Visibility.Visible : Visibility.Collapsed;

        if (!_typedFirst) ShowLive(LengthBox, value, drawing);
        if (size && !_typedSecond && Canvas.PendingSize is { } pending) ShowLive(SecondBox, pending.Y, drawing);

        ShowUnitHint();

        // Just below and right of the cursor, clear of the crosshair and of
        // the snap marker sitting on it. A corner size is not attached to a
        // point being placed, so it follows the cursor itself.
        var at = Canvas.Camera.WorldToScreen(
            Canvas.Entry is CursorEntry.Length or CursorEntry.Size ? Canvas.PendingPoint : Canvas.CursorWorld);

        LengthOverlay.Margin = new Thickness(at.X + 18, at.Y + 18, 0, 0);
    }

    /// <summary>
    /// The figure as it stands. A box with the keyboard keeps everything
    /// selected, so the first key typed replaces the figure rather than
    /// adding to it.
    /// </summary>
    private void ShowLive(TextBox box, double value, SceneDrawing drawing)
    {
        // A length box with the keyboard is being typed into, whatever the
        // flag says; overwriting it twenty times a second would make it
        // impossible to use.
        if (box.IsKeyboardFocusWithin && Canvas.Entry != CursorEntry.Size) return;

        _writingEntry = true;
        box.Text = Units.Format(value, drawing.Units, drawing.LinearPrecision);
        _writingEntry = false;

        if (box.IsKeyboardFocusWithin) box.SelectAll();
    }

    /// <summary>
    /// The drawing's unit after the boxes -- or, once a length in some other
    /// unit has been typed, what it comes to in this one, so 12" in a
    /// millimetre drawing says = 304.8 mm before it is committed.
    /// </summary>
    private void ShowUnitHint()
    {
        if (Canvas.Drawing is not { } drawing) return;

        string suffix = Units.Suffix(drawing.Units);
        var box = SecondBox.IsKeyboardFocusWithin ? SecondBox : LengthBox;
        bool typed = ReferenceEquals(box, SecondBox) ? _typedSecond : _typedFirst;

        LengthUnit.Text =
            typed && Units.TryParseLength(box.Text, drawing.Units, out double value, out var named)
                  && named is { } unit && unit != drawing.Units
                ? $"= {Units.Format(value, drawing.Units, drawing.LinearPrecision)} {suffix}".TrimEnd()
                : suffix;
    }

    private void StartTypingLength(string typed)
    {
        UpdateLengthOverlay();

        // Into whichever box is not already in hand: a digit typed at the
        // canvas starts the width, or the height if that is where Tab left it.
        var box = SecondBox.IsKeyboardFocusWithin ? SecondBox : LengthBox;
        box.Focus();
        box.Text = typed;
        box.CaretIndex = box.Text.Length;
    }

    /// <summary>Puts the keyboard in a box showing its live figure, all selected.</summary>
    private void FocusEntryBox(TextBox box)
    {
        if (ReferenceEquals(box, SecondBox) ? !_typedSecond : !_typedFirst) UpdateLengthOverlay();

        box.Focus();
        box.SelectAll();
        ShowUnitHint();
    }

    /// <summary>
    /// Typing into a rectangle's box holds that side at once, so the preview
    /// shows the size while the other side is still aimed with the mouse.
    /// </summary>
    private void OnEntryTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _writingEntry || Canvas.Drawing is not { } drawing) return;

        bool second = ReferenceEquals(sender, SecondBox);
        if (second) _typedSecond = true;
        else _typedFirst = true;

        if (Canvas.Entry == CursorEntry.Size)
        {
            double? typed = Units.TryParseLength(((TextBox)sender).Text, drawing.Units, out double value) && value > 0
                ? value
                : null;

            Canvas.LockSize(second ? Canvas.LockedWidth : typed, second ? typed : Canvas.LockedHeight);
        }

        ShowUnitHint();
    }

    private void OnLengthKey(object sender, KeyEventArgs e)
    {
        bool size = Canvas.Entry == CursorEntry.Size;

        switch (e.Key)
        {
            case Key.Tab when size:
                // Width and height, either way round, as many times as it
                // takes. Never out of the box: focus travel would land on
                // some button the user was not thinking about.
                FocusEntryBox(ReferenceEquals(sender, SecondBox) ? LengthBox : SecondBox);
                e.Handled = true;
                return;

            case Key.Enter when size:
                CommitTypedSize((TextBox)sender);
                e.Handled = true;
                return;

            case Key.Enter:
                CommitTypedLength();
                e.Handled = true;
                return;

            case Key.Escape:
                // Back to the drawing, with the tool still running: only the
                // half-typed numbers are abandoned.
                if (size) Canvas.LockSize(null, null);
                _typedFirst = _typedSecond = false;
                Canvas.Focus();
                UpdateLengthOverlay();
                e.Handled = true;
                return;
        }
    }

    private void CommitTypedLength()
    {
        if (Canvas.Drawing is not { } drawing) return;

        // A corner size of zero is a sharp corner and a perfectly good
        // answer; a length of zero is not a length.
        bool wantsPositive = Canvas.Entry == CursorEntry.Length;

        if (!Units.TryParseLength(LengthBox.Text, drawing.Units, out double value)
            || value < 0 || (wantsPositive && value == 0))
        {
            StatusText.Text = $"'{LengthBox.Text}' is not a length. Try 50, 12\", 3'6\", 120cm or 0.5m.";
            return;
        }

        // The canvas takes the keyboard back so that Escape, Enter and the
        // next digit all reach the tool rather than the box.
        Canvas.Focus();
        _typedFirst = false;
        Canvas.ApplyEntry(value);
        UpdateLengthOverlay();
    }

    /// <summary>
    /// Enter in either of a rectangle's boxes places the corner: a side that
    /// was typed is held to it and a side that was not is where the cursor
    /// put it, which is how one dimension is typed and the other aimed.
    /// </summary>
    private void CommitTypedSize(TextBox box)
    {
        if (Canvas.Drawing is not { } drawing) return;

        bool typed = ReferenceEquals(box, SecondBox) ? _typedSecond : _typedFirst;
        if (typed && !(Units.TryParseLength(box.Text, drawing.Units, out double value) && value > 0))
        {
            StatusText.Text = $"'{box.Text}' is not a length. Try 50, 12\", 3'6\", 120cm or 0.5m.";
            return;
        }

        Canvas.Focus();
        _typedFirst = _typedSecond = false;
        Canvas.PlaceSizedCorner();
        UpdateLengthOverlay();
    }

    // ---- the text box ------------------------------------------------------

    /// <summary>
    /// Existing text being edited in the box, or null when the box belongs
    /// to the text tool. One box, two jobs, and this says which.
    /// </summary>
    private SText? _editingText;

    /// <summary>Set while the shell writes the box, so that is not mistaken for typing.</summary>
    private bool _writingText;

    /// <summary>
    /// Shows the text box where the text is, or hides it. It opens empty,
    /// with the keyboard, the moment the text tool has its point: typing is
    /// the next thing that happens, and having to click into a box first
    /// would be a step nobody expects.
    /// </summary>
    private void UpdateTextOverlay()
    {
        if (!_ready) return;

        bool tool = Canvas.Entry == CursorEntry.Text;

        if (!tool && _editingText is null)
        {
            if (TextOverlay.Visibility == Visibility.Visible)
            {
                TextOverlay.Visibility = Visibility.Collapsed;
                SetTextBox("");
                if (TextEntryBox.IsKeyboardFocusWithin) Canvas.Focus();
            }
            return;
        }

        var anchor = _editingText?.Position ?? Canvas.PendingFrom ?? Vec2.Zero;
        var at = Canvas.Camera.WorldToScreen(anchor);

        TextCaption.Text = _editingText is null ? "Text" : "Edit text";

        // On the side the text is not growing towards, so the box never sits
        // on the words it is showing: above text that hangs from its top,
        // below text that stands on its baseline.
        var hang = _editingText?.AnchorY ?? Canvas.TextJustification.Y;
        double tall = TextOverlay.ActualHeight > 0 ? TextOverlay.ActualHeight : 72;
        TextOverlay.Margin = hang is TextAnchorY.Top or TextAnchorY.Middle
            ? new Thickness(at.X, Math.Max(0, at.Y - tall - 10), 0, 0)
            : new Thickness(at.X, at.Y + 10, 0, 0);

        if (TextOverlay.Visibility != Visibility.Visible)
        {
            ShowTextFormat(_editingFormat ?? Canvas.TextFormat);
            TextOverlay.Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(() => TextEntryBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    /// <summary>
    /// The look of the text being edited, held until Enter. Null while the
    /// box belongs to the tool, whose look is the canvas's and the drawing's.
    /// </summary>
    private TextFormat? _editingFormat;

    /// <summary>Puts a format into the row of controls under the words.</summary>
    private void ShowTextFormat(TextFormat format)
    {
        _writingText = true;

        TextFontBox.ItemsSource = TextChoices.Fonts(format.FontFamily);
        TextFontBox.SelectedItem = TextChoices.FontName(format.FontFamily);
        TextBoldToggle.IsChecked = format.Bold;
        TextItalicToggle.IsChecked = format.Italic;
        TextHeightBox.Text = PropertyRow.Number(format.Height);
        TextRotationBox.Text = PropertyRow.Number(format.Rotation * 180 / Math.PI);
        TextJustifyBox.ItemsSource = TextChoices.JustificationNames;
        TextJustifyBox.SelectedItem = TextChoices.JustificationName(format.AnchorX, format.AnchorY);

        _writingText = false;
    }

    /// <summary>
    /// Reads the row of controls back as a format, keeping whatever of
    /// <paramref name="current"/> a box holds nothing sensible for.
    /// </summary>
    private TextFormat ReadTextFormat(TextFormat current)
    {
        var format = current with
        {
            FontFamily = TextFontBox.SelectedItem is string font ? TextChoices.FamilyOf(font) : current.FontFamily,
            Bold = TextBoldToggle.IsChecked == true,
            Italic = TextItalicToggle.IsChecked == true,
        };

        if (Canvas.Drawing is { } drawing &&
            Units.TryParseLength(TextHeightBox.Text, drawing.Units, out double height) && height > 0)
            format = format with { Height = height };

        if (PropertyRow.TryNumber(TextRotationBox.Text, out double degrees))
            format = format with { Rotation = degrees * Math.PI / 180 };

        if (TextJustifyBox.SelectedItem is string name && TextChoices.TryJustification(name, out var x, out var y))
            format = format with { AnchorX = x, AnchorY = y };

        return format;
    }

    /// <summary>
    /// Applies the row: to the text being edited, held until Enter; or to the
    /// tool, where the typeface and height are the drawing's -- an undoable
    /// change that is saved -- and the turn and justification are the
    /// canvas's, chosen per piece of text.
    /// </summary>
    private void ApplyTextFormat()
    {
        if (_editingText is not null)
        {
            _editingFormat = ReadTextFormat(_editingFormat ?? TextFormat.Of(_editingText));
            return;
        }

        if (Canvas.Drawing is not { } drawing) return;

        var format = ReadTextFormat(Canvas.TextFormat);
        Canvas.TextRotation = format.Rotation;
        Canvas.TextJustification = (format.AnchorX, format.AnchorY);

        var settings = drawing.Text with { FontFamily = format.FontFamily, Bold = format.Bold, Italic = format.Italic };
        if (Math.Abs(format.Height - Canvas.TextHeight) > 1e-12) settings = settings with { Height = format.Height };
        Canvas.SetTextSettings(settings);

        UpdateTextOverlay();
    }

    private void OnTextFormatChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready || _writingText || TextOverlay.Visibility != Visibility.Visible) return;

        ApplyTextFormat();

        // Back to the words: choosing a font is a pause in typing, not the end of it.
        Dispatcher.BeginInvoke(() => TextEntryBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnTextNumberKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                // Takes the number, not the text: Enter here is the end of a
                // height, and the words are still being written.
                ApplyTextFormat();
                TextEntryBox.Focus();
                e.Handled = true;
                return;

            case Key.Escape:
                ShowTextFormat(_editingFormat ?? Canvas.TextFormat);
                TextEntryBox.Focus();
                e.Handled = true;
                return;
        }
    }

    private void OnTextNumberLeft(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_ready && !_writingText && TextOverlay.Visibility == Visibility.Visible) ApplyTextFormat();
    }

    private void SetTextBox(string text)
    {
        _writingText = true;
        TextEntryBox.Text = text;
        TextEntryBox.CaretIndex = text.Length;
        _writingText = false;
    }

    /// <summary>The box's text as lines, however the line breaks arrived.</summary>
    private IReadOnlyList<string> TypedLines() =>
        TextEntryBox.Text.Replace("\r\n", "\n").Split('\n');

    /// <summary>Opens the box on text that is already in the drawing, its words ready to change.</summary>
    private void StartEditingText(SText text)
    {
        // A tool half way through something would be a second owner for the
        // keyboard; editing happens from the pointer.
        if (Canvas.Mode != CanvasMode.Select) Canvas.UseSelect();

        _editingText = text;
        _editingFormat = TextFormat.Of(text);
        SetTextBox(string.Join(Environment.NewLine, text.Lines));
        UpdateTextOverlay();
        TextEntryBox.SelectAll();
        StatusText.Text = "Edit text: Enter to keep the change, Shift+Enter for a new line, Escape to leave it as it was.";
    }

    private void StopEditingText()
    {
        _editingText = null;
        _editingFormat = null;
        UpdateTextOverlay();
        Canvas.Focus();
    }

    private void OnTextEntryChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _writingText) return;

        // The tool previews the words in the drawing as they are typed.
        if (_editingText is null) Canvas.SetTypedText(TypedLines());
    }

    private void OnTextEntryKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            // Shift+Enter falls through to the box, which makes a new line.
            case Key.Enter when !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                e.Handled = true;

                if (_editingText is { } editing)
                {
                    if (Canvas.ReplaceText(editing, TypedLines(), _editingFormat)) StatusText.Text = "Text changed.";
                    StopEditingText();
                    return;
                }

                // The tool's own early finish, so the text goes in exactly as
                // a polyline does when Enter ends it.
                Canvas.SetTypedText(TypedLines());
                Canvas.FinishTool();
                Canvas.Focus();
                return;

            case Key.Escape:
                e.Handled = true;

                if (_editingText is not null)
                {
                    StopEditingText();
                    return;
                }

                // The words and the point go; the tool stays, ready for the next.
                Canvas.CancelCurrent();
                Canvas.Focus();
                return;
        }
    }


    private void OnZoomWindowToggled(object sender, RoutedEventArgs e)
    {
        if (ZoomWindowToggle.IsChecked == true) Canvas.UseZoomWindow();
        else Canvas.UseSelect();

        Canvas.Focus();
    }

    private void OnLineweightsToggled(object sender, RoutedEventArgs e)
    {
        Canvas.Settings.ShowLineweights = LineweightsToggle.IsChecked == true;
        Canvas.Redraw();
    }

    private void OnBackgroundToggled(object sender, RoutedEventArgs e)
    {
        Canvas.Settings.Background = LightBackgroundToggle.IsChecked == true
            ? LightBackground
            : DarkBackground;
        Canvas.Redraw();
    }

    private void UpdateStatus()
    {
        // A tool that is mid-pick has one thing to say and it beats the
        // statistics: the user is waiting to be told what to click next.
        if (Canvas.ToolPrompt is { } prompt)
        {
            StatusText.Text = prompt;
            return;
        }

        var drawing = Canvas.Drawing;
        if (drawing is null)
        {
            StatusText.Text = "No drawing loaded.";
            return;
        }

        Bounds2 bounds = drawing.Bounds;

        string source = _diagnostics is null
            ? $"{drawing.Entities.Count} entities"
            : _diagnostics.Summary();

        string extents = bounds.IsEmpty ? "empty" : $"{bounds.Width:0.##} x {bounds.Height:0.##}";
        string blocks = drawing.Blocks.Count == 0 ? "" : $"   |   {drawing.Blocks.Count} blocks";
        string layer = drawing.CurrentLayer is { } current ? $"   |   layer {current.Name}" : "";
        string selected = Canvas.Selection.IsEmpty ? "" : $"   |   {Canvas.Selection.Count} selected";
        string radius = $"   |   R {Canvas.FilletRadius:0.###}   |   C {Canvas.ChamferDistance:0.###}";

        StatusText.Text =
            $"{source}{blocks}   |   {drawing.ActiveLayout.Name}{layer}   |   extents {extents}{selected}{radius}";
    }

    /// <summary>Minimal ICommand shim so a keyboard shortcut can invoke an async method.</summary>
    private sealed class RelayCommand : ICommand
    {
        private readonly Func<Task> _execute;

        public RelayCommand(Func<Task> execute) => _execute = execute;

        public RelayCommand(Action execute) => _execute = () =>
        {
            execute();
            return Task.CompletedTask;
        };

        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public async void Execute(object? parameter) => await _execute();
    }
}
