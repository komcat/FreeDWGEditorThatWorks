using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Styling;
using FreeDwg.Interop.Acad;
using Microsoft.Win32;
using SceneDrawing = FreeDwg.Core.Scene.Drawing;

namespace FreeDWGEditorThatWorks;

public partial class MainWindow : Window
{
    private static readonly Rgb DarkBackground = new(33, 40, 48);
    private static readonly Rgb LightBackground = new(255, 255, 255);

    private ImportDiagnostics? _diagnostics;

    public MainWindow()
    {
        InitializeComponent();

        Canvas.CursorMoved += (_, world) =>
            CoordinateText.Text = $"X {world.X,12:0.###}   Y {world.Y,12:0.###}";

        InputBindings.Add(new KeyBinding(new RoutedCommandStub(OpenAsync), Key.O, ModifierKeys.Control));
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

    private void OnZoomExtentsClick(object sender, RoutedEventArgs e) => Canvas.ZoomExtents();

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

        Bounds2 bounds = Canvas.Drawing?.Bounds ?? Bounds2.Empty;
        string extents = bounds.IsEmpty
            ? "empty"
            : $"{bounds.Width:0.##} x {bounds.Height:0.##}";

        StatusText.Text = $"{_diagnostics.Summary()}   |   extents {extents}   |   {Canvas.LastStats.Drawn} drawn";
    }

    /// <summary>Minimal ICommand shim so a keyboard shortcut can invoke an async method.</summary>
    private sealed class RoutedCommandStub : ICommand
    {
        private readonly Func<Task> _execute;
        public RoutedCommandStub(Func<Task> execute) => _execute = execute;

        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public async void Execute(object? parameter) => await _execute();
    }
}
