using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Styling;
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

        InputBindings.Add(new KeyBinding(new RelayCommand(OpenAsync), Key.O, ModifierKeys.Control));
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
            Canvas.Drawing = drawing;
            EmptyHint.Visibility = Visibility.Collapsed;
            Title = $"FreeDWG Editor - {Path.GetFileName(path)}";

            PopulateLayouts(drawing);
            PopulateLayers(drawing);
            UpdateStatus();
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
        if (_diagnostics is null) return;

        var drawing = Canvas.Drawing;
        Bounds2 bounds = drawing?.Bounds ?? Bounds2.Empty;

        string extents = bounds.IsEmpty ? "empty" : $"{bounds.Width:0.##} x {bounds.Height:0.##}";
        string blocks = drawing is null || drawing.Blocks.Count == 0
            ? ""
            : $"   |   {drawing.Blocks.Count} blocks";
        string layout = drawing is null ? "" : $"   |   {drawing.ActiveLayout.Name}";
        string selected = Canvas.Selection.IsEmpty ? "" : $"   |   {Canvas.Selection.Count} selected";

        StatusText.Text =
            $"{_diagnostics.Summary()}{blocks}{layout}   |   extents {extents}   |   {Canvas.LastStats.Drawn} drawn{selected}";
    }

    /// <summary>Minimal ICommand shim so a keyboard shortcut can invoke an async method.</summary>
    private sealed class RelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        public RelayCommand(Func<Task> execute) => _execute = execute;

        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public async void Execute(object? parameter) => await _execute();
    }
}
