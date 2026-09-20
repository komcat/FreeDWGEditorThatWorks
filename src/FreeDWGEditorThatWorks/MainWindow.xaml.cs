using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Data;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Styling;
using FreeDwg.Core.Snapping;
using FreeDwg.Core.Tools;
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

    public MainWindow()
    {
        InitializeComponent();

        LayerList.ItemsSource = _layers;

        // Grouped in the view rather than the collection, so the rows stay a
        // flat list that is simple to rebuild.
        var grouped = new CollectionViewSource { Source = _properties };
        grouped.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PropertyRow.Category)));
        PropertyGrid.ItemsSource = grouped.View;

        _toolButtons =
        [
            SelectButton, LineButton, PolylineButton, RectangleButton,
            CircleButton, ArcButton, EllipseButton,
            MoveButton, CopyButton, RotateButton, ScaleButton, MirrorButton,
            TrimButton, ExtendButton, FilletButton, ChamferButton, TangentButton,
        ];

        Canvas.CursorMoved += (_, world) =>
            CoordinateText.Text = $"X {world.X,12:0.###}   Y {world.Y,12:0.###}";

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
            SyncModeButtons();
            UpdateStatus();
            RebuildProperties();
        };
        Canvas.DrawingEdited += (_, _) =>
        {
            if (Canvas.Drawing is { } drawing) PopulateLayers(drawing);
            UpdateHistoryButtons();
            UpdateTitle();
            UpdateStatus();
            UpdateEmptyHint();
            RebuildProperties();
        };

        Canvas.Snapping.Modes = SnapModes.Objects;

        InputBindings.Add(new KeyBinding(new RelayCommand(OpenAsync), Key.O, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(NewDrawing), Key.N, ModifierKeys.Control));
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
        var drawing = SceneDrawing.CreateEmpty();

        _diagnostics = null;
        _documentName = "Untitled";
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
        var modes = SnapModes.None;

        if (EndpointSnapToggle.IsChecked == true) modes |= SnapModes.Endpoint;
        if (MidpointSnapToggle.IsChecked == true) modes |= SnapModes.Midpoint;
        if (CenterSnapToggle.IsChecked == true) modes |= SnapModes.Center;
        if (QuadrantSnapToggle.IsChecked == true) modes |= SnapModes.Quadrant;
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
            SceneDrawing drawing = await Task.Run(() => DwgLoader.Load(path, diagnostics));

            _diagnostics = diagnostics;
            _documentName = Path.GetFileName(path);
            Canvas.Drawing = drawing;

            PopulateLayouts(drawing);
            PopulateLayers(drawing);
            Canvas.UseSelect();

            UpdateHistoryButtons();
            UpdateTitle();
            UpdateStatus();
            UpdateEmptyHint();
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

        _layers.Clear();
        for (int i = 0; i < drawing.Layers.Count; i++)
            _layers.Add(new LayerItem(drawing.Layers[i], counts[i], OnLayerVisibilityChanged));

        LayerCountText.Text = _layers.Count.ToString();

        // Selecting a row is how the current layer is chosen, so the list has
        // to start on whatever the drawing says that is.
        if ((uint)drawing.CurrentLayerIndex < (uint)_layers.Count)
            LayerList.SelectedIndex = drawing.CurrentLayerIndex;
    }

    /// <summary>The selected row is the layer new geometry is drawn on.</summary>
    private void OnLayerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (Canvas.Drawing is not { } drawing) return;
        if (LayerList.SelectedIndex < 0) return;

        drawing.CurrentLayerIndex = LayerList.SelectedIndex;
        UpdateStatus();
    }

    private void OnLayerVisibilityChanged()
    {
        Canvas.Redraw();
        UpdateStatus();
    }

    private void OnZoomExtentsClick(object sender, RoutedEventArgs e) => Canvas.ZoomExtents();

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
        string radius = $"   |   R {Canvas.CornerRadius:0.###}";

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
