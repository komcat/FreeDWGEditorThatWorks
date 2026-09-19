using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Rendering;
using FreeDWGEditorThatWorks.Rendering;
using SceneDrawing = FreeDwg.Core.Scene.Drawing;

namespace FreeDWGEditorThatWorks.Controls;

/// <summary>
/// The model-space viewport. A single element that paints the whole scene in
/// <see cref="OnRender"/> -- one Shape per entity would put tens of thousands
/// of objects in the visual tree and fall over.
/// </summary>
public sealed class CadCanvas : FrameworkElement
{
    private SceneDrawing? _drawing;
    private SolidColorBrush _backgroundBrush = Brushes.Black;
    private uint _backgroundBrushKey = uint.MaxValue;

    private bool _isPanning;
    private Point _panAnchor;
    private bool _zoomExtentsPending;

    public CadCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public Camera Camera { get; } = new();
    public RenderSettings Settings { get; } = new();
    public RenderStats LastStats { get; private set; }

    /// <summary>Fires with the cursor position in world units.</summary>
    public event EventHandler<Vec2>? CursorMoved;

    /// <summary>Fires after any pan or zoom, so the shell can refresh its readout.</summary>
    public event EventHandler? ViewChanged;

    public SceneDrawing? Drawing
    {
        get => _drawing;
        set
        {
            _drawing = value;
            ZoomExtents();
        }
    }

    public void ZoomExtents()
    {
        SyncViewport();

        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            // Called before the first layout pass; retry once we have a size.
            _zoomExtentsPending = true;
            return;
        }

        _zoomExtentsPending = false;
        Camera.ZoomToFit(_drawing?.Bounds ?? Bounds2.Empty);
        Redraw();
    }

    public void Redraw()
    {
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        if (_zoomExtentsPending) ZoomExtents();
        else InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Also makes the element hit-testable, which a FrameworkElement with no
        // painted background is not.
        dc.DrawRectangle(GetBackgroundBrush(), null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (_drawing is null) return;

        SyncViewport();
        var sink = new WpfDrawingSink(dc, Camera, Settings, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        LastStats = SceneRenderer.Render(_drawing, Camera, sink);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        double factor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
        Point p = e.GetPosition(this);
        Camera.ZoomAtScreenPoint(new Vec2(p.X, p.Y), factor);
        Redraw();
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        // Middle-drag is the CAD convention; left-drag stands in until the
        // editor phase claims it for selection.
        if (e.ChangedButton is MouseButton.Middle or MouseButton.Left)
        {
            _isPanning = true;
            _panAnchor = e.GetPosition(this);
            CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point p = e.GetPosition(this);

        if (_isPanning)
        {
            Camera.PanByScreenDelta(p.X - _panAnchor.X, p.Y - _panAnchor.Y);
            _panAnchor = p;
            Redraw();
        }

        CursorMoved?.Invoke(this, Camera.ScreenToWorld(new Vec2(p.X, p.Y)));
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);

        if (_isPanning)
        {
            _isPanning = false;
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
        }
    }

    private void SyncViewport()
    {
        Camera.ViewportWidth = ActualWidth;
        Camera.ViewportHeight = ActualHeight;
    }

    private SolidColorBrush GetBackgroundBrush()
    {
        var bg = Settings.Background;
        if (_backgroundBrushKey != bg.Packed)
        {
            _backgroundBrush = new SolidColorBrush(Color.FromRgb(bg.R, bg.G, bg.B));
            _backgroundBrush.Freeze();
            _backgroundBrushKey = bg.Packed;
        }
        return _backgroundBrush;
    }
}
