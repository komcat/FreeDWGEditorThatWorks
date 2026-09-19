using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Styling;
using FreeDwg.Core.Tools;
using FreeDwg.Interop.Acad;
using FreeDWGEditorThatWorks.ViewModels;
using Microsoft.Win32;
using SceneDrawing = FreeDwg.Core.Scene.Drawing;
using SceneLayout = FreeDwg.Core.Scene.Layout;

namespace FreeDWGEditorThatWorks;

public partial class MainWindow : Window
{
    private static readonly Rgb DarkBackground = new(33, 40, 48);
    private static readonly Rgb LightBackground = new(255, 255, 255);

    private readonly ObservableCollection<LayerItem> _layers = new();
    private ImportDiagnostics? _diagnostics;
    private string _documentName = "Untitled";

    public MainWindow()
    {
        InitializeComponent();

        LayerList.ItemsSource = _layers;

        Canvas.CursorMoved += (_, world) =>
            CoordinateText.Text = $"X {world.X,12:0.###}   Y {world.Y,12:0.###}";

        Canvas.SelectionChanged += (_, _) => UpdateStatus();

        // The canvas owns whether a zoom window is still pending; the toggle
        // only reflects it, so that Escape or a stray click releases both.
        Canvas.ZoomWindowDisarmed += (_, _) => ZoomWindowToggle.IsChecked = false;

        // A tool asking for its next point, and an edit landing, both change
        // what the status bar and the undo buttons should say.
        Canvas.ToolChanged += (_, _) => UpdateStatus();
        Canvas.DrawingEdited += (_, _) =>
        {
            if (Canvas.Drawing is { } drawing) PopulateLayers(drawing);
            UpdateHistoryButtons();
            UpdateTitle();
            UpdateStatus();
            UpdateEmptyHint();
        };

        InputBindings.Add(new KeyBinding(new RelayCommand(OpenAsync), Key.O, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(NewDrawing), Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(Canvas.Undo), Key.Z, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(Canvas.Redo), Key.Y, ModifierKeys.Control));

        NewDrawing();
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
        SelectTool.IsChecked = true;
        Canvas.Tool = null;

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
        Canvas.Tool = (sender as FrameworkElement)?.Name switch
        {
            nameof(LineTool) => new LineTool(),
            nameof(PolylineTool) => new PolylineTool(),
            nameof(RectangleTool) => new RectangleTool(),
            nameof(CircleTool) => new CircleTool(),
            nameof(ArcTool) => new ArcTool(),
            nameof(EllipseTool) => new EllipseTool(),
            _ => null,
        };

        Canvas.Focus();
        UpdateStatus();
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
            SelectTool.IsChecked = true;

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
        Canvas.ZoomWindowArmed = ZoomWindowToggle.IsChecked == true;
        if (Canvas.ZoomWindowArmed) Canvas.Focus();
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

        StatusText.Text =
            $"{source}{blocks}   |   {drawing.ActiveLayout.Name}{layer}   |   extents {extents}{selected}";
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
